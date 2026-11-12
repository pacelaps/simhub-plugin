using System;
using System.Net.Http;
using System.Reflection;
using PaceLaps.Plugin.Core;

namespace PaceLaps.Plugin
{
    /// <summary>
    /// The one connection to race control. Static, because SimHub rebuilds its plugins on every game change: the
    /// connection (and the calls on display) must outlive the plugin instance.
    /// </summary>
    internal static class Host
    {
        private static readonly object Gate = new object();
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        private static RaceControlClient? _client;
        private static string? _url;

        public static readonly string Version =
            typeof(Host).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0";

        /// <summary>A new state (any thread).</summary>
        public static event Action<DriverState>? Changed;

        /// <summary>A call was shown now (any thread).</summary>
        public static event Action<RaceMessage>? CallShown;

        public static DriverState State => _client?.State ?? DriverState.NotSetUp;

        public static long ServerNowMs => _client?.ServerNowMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        /// <summary>The site the plugin connects to, without the token ("" when not set up).</summary>
        public static string Site { get; private set; } = "";

        /// <summary>Connects with an overlay URL (again only if it changed); returns a problem to show, or null.</summary>
        public static string? Use(string? overlayUrl)
        {
            if (string.IsNullOrWhiteSpace(overlayUrl))
            {
                Stop();
                return null;
            }

            var link = OverlayLink.Parse(overlayUrl, out var problem);
            if (link == null)
            {
                return problem;
            }

            RaceControlClient client;
            lock (Gate)
            {
                if (_client != null && _url == overlayUrl!.Trim() && _client.State.Status != ConnectionStatus.SignedOut)
                {
                    return null;
                }

                StopLocked();
                _url = overlayUrl!.Trim();
                Site = link.ToString();
                client = new RaceControlClient(new HttpDeviceApi(Http, link), new PusherSocket(new ThreadTimers(), Log.Info),
                    new SystemClock(), new ThreadTimers(), link.Site, Version, Log.Info);
                client.Changed += s => Changed?.Invoke(s);
                client.CallShown += m => CallShown?.Invoke(m);
                _client = client;
            }

            Log.Info("connecting to " + link);
            System.Threading.Tasks.Task.Run(client.Start);
            return null;
        }

        public static void Stop()
        {
            lock (Gate)
            {
                StopLocked();
            }

            Changed?.Invoke(DriverState.NotSetUp);
        }

        private static void StopLocked()
        {
            _client?.Dispose();
            _client = null;
            _url = null;
            Site = "";
        }
    }

    /// <summary>SimHub's log, with our prefix. Never pass a token or an overlay URL.</summary>
    internal static class Log
    {
        public static void Info(string text) => global::SimHub.Logging.Current.Info("PaceLaps: " + text);
    }
}
