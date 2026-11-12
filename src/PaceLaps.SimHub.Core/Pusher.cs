using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PaceLaps.SimHub.Core
{
    /// <summary>A frame of the Pusher protocol (version 7), which Reverb speaks.</summary>
    public sealed class PusherFrame
    {
        public PusherFrame(string @event, string? channel, string? data)
        {
            Event = @event;
            Channel = channel;
            Data = data;
        }

        public string Event { get; }
        public string? Channel { get; }

        /// <summary>The event's data as JSON text (Pusher sends it as a string holding JSON; objects are re-serialized).</summary>
        public string? Data { get; }
    }

    /// <summary>The Pusher protocol's frames, without any socket: tested on their own.</summary>
    public static class PusherProtocol
    {
        public const int Version = 7;

        /// <summary>wss://host/app/key?protocol=7... as pusher-js builds it (Caddy passes /app/* to Reverb).</summary>
        public static Uri SocketUri(Uri site, string key, string clientVersion)
        {
            var scheme = site.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
            var builder = new UriBuilder(site) { Scheme = scheme, Path = "/app/" + Uri.EscapeDataString(key) };
            builder.Query = "protocol=" + Version + "&client=pacelaps-simhub&version=" + Uri.EscapeDataString(clientVersion) + "&flash=false";
            if (site.IsDefaultPort)
            {
                builder.Port = -1;
            }

            return builder.Uri;
        }

        public static PusherFrame? Parse(string text)
        {
            JObject? o;
            try
            {
                o = Json.Object(text);
            }
            catch (JsonException)
            {
                return null;
            }

            var name = o?.Str("event");
            if (o == null || name == null)
            {
                return null;
            }

            o.TryGetValue("data", out var data);
            var dataText = data == null || data.Type == JTokenType.Null
                ? null
                : data.Type == JTokenType.String ? (string?)data : data.ToString(Formatting.None);
            return new PusherFrame(name, o.Str("channel"), dataText);
        }

        /// <summary>The socket id and the activity timeout (seconds) from pusher:connection_established.</summary>
        public static bool TryConnectionEstablished(PusherFrame frame, out string socketId, out int activityTimeoutSec)
        {
            socketId = "";
            activityTimeoutSec = 120;
            if (frame.Event != "pusher:connection_established" || frame.Data == null)
            {
                return false;
            }

            var o = Json.Object(frame.Data);
            var id = o?.Str("socket_id");
            if (id == null)
            {
                return false;
            }

            socketId = id;
            activityTimeoutSec = o!.Int("activity_timeout") ?? 120;
            return true;
        }

        public static string Subscribe(string channel, string auth) =>
            Frame("pusher:subscribe", new JObject { ["channel"] = channel, ["auth"] = auth });

        public static string Unsubscribe(string channel) => Frame("pusher:unsubscribe", new JObject { ["channel"] = channel });

        public static string Ping() => Frame("pusher:ping", new JObject());

        public static string Pong() => Frame("pusher:pong", new JObject());

        private static string Frame(string name, JObject data) =>
            new JObject { ["event"] = name, ["data"] = data }.ToString(Formatting.None);
    }
}
