using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace PaceLaps.SimHub.Core
{
    /// <summary>A device API request that failed; <see cref="SignedOut"/> when the token no longer works.</summary>
    public sealed class DeviceApiException : Exception
    {
        public DeviceApiException(string message, HttpStatusCode? status = null)
            : base(message)
        {
            Status = status;
        }

        public HttpStatusCode? Status { get; }

        /// <summary>The overlay URL was renewed or turned off (or never existed).</summary>
        public bool SignedOut => Status == HttpStatusCode.Unauthorized || Status == HttpStatusCode.Forbidden || Status == HttpStatusCode.NotFound;
    }

    /// <summary>The device API as the web views use it (routes /api/v1/device/...), with the overlay token.</summary>
    public interface IDeviceApi
    {
        Task<ClockPong> Clock(long clientSentMs, CancellationToken cancellation);

        /// <summary>The state; with a cursor only what came after it (resync).</summary>
        Task<StateSnapshot> State(string? cursor, CancellationToken cancellation);

        Task Shown(ShownReport report, CancellationToken cancellation);

        /// <summary>Authorizes a private channel for this socket; returns the auth string for pusher:subscribe.</summary>
        Task<string> Authorize(string socketId, string channel, CancellationToken cancellation);

        /// <summary>The realtime server's public key, read from the overlay page.</summary>
        Task<string> RealtimeKey(CancellationToken cancellation);
    }

    /// <summary>Contract shown: when a call was displayed, for the stewards' lateness statistics.</summary>
    public sealed class ShownReport
    {
        public ShownReport(string messageId, long shownAtMs, long receivedAtMs, double offsetMs, double rttMs, bool late)
        {
            MessageId = messageId;
            ShownAtMs = shownAtMs;
            ReceivedAtMs = receivedAtMs;
            OffsetMs = offsetMs;
            RttMs = rttMs;
            Late = late;
        }

        public string MessageId { get; }

        /// <summary>Server clock (device clock corrected by the offset).</summary>
        public long ShownAtMs { get; }

        /// <summary>Server clock.</summary>
        public long ReceivedAtMs { get; }

        public double OffsetMs { get; }
        public double RttMs { get; }
        public bool Late { get; }

        public JObject ToJson() => new JObject
        {
            ["v"] = 1,
            ["kind"] = "shown",
            ["messageId"] = MessageId,
            ["shownAt"] = Iso.FromMs(ShownAtMs),
            ["receivedAt"] = Iso.FromMs(ReceivedAtMs),
            ["offsetMs"] = Math.Round(OffsetMs, 1),
            ["rttMs"] = Math.Round(Math.Max(0, RttMs), 1),
            ["late"] = Late,
        };
    }

    public sealed class HttpDeviceApi : IDeviceApi
    {
        private static readonly Regex KeyMeta = new Regex("<meta\\s+name=\"reverb-key\"\\s+content=\"([^\"]+)\"", RegexOptions.CultureInvariant);

        private readonly HttpClient _http;
        private readonly OverlayLink _link;

        public HttpDeviceApi(HttpClient http, OverlayLink link)
        {
            _http = http;
            _link = link;
        }

        public async Task<ClockPong> Clock(long clientSentMs, CancellationToken cancellation)
        {
            var pong = await Send(HttpMethod.Post, "api/v1/device/clock",
                new JObject { ["v"] = 1, ["kind"] = "clock_ping", ["clientSentAt"] = Iso.FromMs(clientSentMs) }, cancellation).ConfigureAwait(false);
            var o = Json.Object(pong) ?? throw new DeviceApiException("clock: not JSON");
            if (!Iso.TryToMs(o.Str("serverTime"), out var serverTime))
            {
                throw new DeviceApiException("clock: no serverTime");
            }

            return new ClockPong(serverTime, Iso.TryToMs(o.Str("serverReceivedAt"), out var received) ? received : (long?)null);
        }

        public async Task<StateSnapshot> State(string? cursor, CancellationToken cancellation)
        {
            var path = "api/v1/device/state" + (cursor == null ? "" : "?cursor=" + Uri.EscapeDataString(cursor));
            var body = await Send(HttpMethod.Get, path, null, cancellation).ConfigureAwait(false);
            return StateSnapshot.FromJson(body) ?? throw new DeviceApiException("state: not a state snapshot");
        }

        public Task Shown(ShownReport report, CancellationToken cancellation) =>
            Send(HttpMethod.Post, "api/v1/device/shown", report.ToJson(), cancellation);

        public async Task<string> Authorize(string socketId, string channel, CancellationToken cancellation)
        {
            using var request = Request(HttpMethod.Post, "api/v1/device/broadcasting/auth");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("socket_id", socketId),
                new KeyValuePair<string, string>("channel_name", channel),
            });
            var body = await Read(request, cancellation).ConfigureAwait(false);
            return Json.Object(body)?.Str("auth") ?? throw new DeviceApiException("channel auth: no auth for " + channel);
        }

        public async Task<string> RealtimeKey(CancellationToken cancellation)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _link.Page);
            request.Headers.Accept.ParseAdd("text/html");
            var html = await Read(request, cancellation).ConfigureAwait(false);
            var match = KeyMeta.Match(html);
            return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : throw new DeviceApiException("overlay page: no realtime key");
        }

        private async Task<string> Send(HttpMethod method, string path, JObject? body, CancellationToken cancellation)
        {
            using var request = Request(method, path);
            if (body != null)
            {
                request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            }

            return await Read(request, cancellation).ConfigureAwait(false);
        }

        private HttpRequestMessage Request(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, new Uri(_link.Site, path));
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Add("X-Overlay-Token", _link.Token);
            return request;
        }

        private async Task<string> Read(HttpRequestMessage request, CancellationToken cancellation)
        {
            // The path only: the overlay page's URL holds the token.
            var what = request.Method + " " + (request.RequestUri?.AbsolutePath.StartsWith("/overlay/", StringComparison.Ordinal) == true
                ? "/overlay/…"
                : request.RequestUri?.AbsolutePath);
            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellation).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                throw new DeviceApiException(what + ": " + e.Message);
            }

            using (response)
            {
                var text = response.Content == null ? "" : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new DeviceApiException(what + ": " + (int)response.StatusCode, response.StatusCode);
                }

                return text;
            }
        }
    }
}
