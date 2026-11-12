using System.Linq;

namespace PaceLaps.Plugin.Core
{
    public enum ConnectionStatus
    {
        /// <summary>No overlay URL yet.</summary>
        NotSetUp,

        /// <summary>Starting: never connected yet.</summary>
        Connecting,

        Connected,

        /// <summary>Was connected and lost it: the overlay must warn, calls may be missed until it is back.</summary>
        Offline,

        /// <summary>The overlay URL no longer works (renewed or turned off on the driver page).</summary>
        SignedOut,
    }

    /// <summary>
    /// Everything the plugin shows, as one immutable value: SimHub reads its properties from the latest one while the
    /// client builds the next on its own threads.
    /// </summary>
    public sealed class DriverState
    {
        public static readonly DriverState NotSetUp = new DriverState { Status = ConnectionStatus.NotSetUp };

        public ConnectionStatus Status { get; internal set; } = ConnectionStatus.Connecting;

        /// <summary>A short text for the settings page and the overlay when something is wrong, else null.</summary>
        public string? Problem { get; internal set; }

        public int? SessionId { get; internal set; }
        public string? SessionName { get; internal set; }
        public DeviceInfo Device { get; internal set; } = new DeviceInfo(null, null, null, false);

        /// <summary>The running procedure's phase and name (null when none runs).</summary>
        public string? Phase { get; internal set; }

        public string? Procedure { get; internal set; }
        public CallDisplay Display { get; internal set; } = CallDisplay.Empty;
        public SortingTarget? Target { get; internal set; }

        /// <summary>The race state; null without telemetry.</summary>
        public RaceState? Race { get; internal set; }

        public ClockSample? Clock { get; internal set; }

        public bool Connected => Status == ConnectionStatus.Connected;

        /// <summary>The call a one-line display shows now.</summary>
        public ShownCall? Current => Display.Current;

        /// <summary>This car's own penalty banner, if one is up.</summary>
        public ShownCall? MyPenalty => Display.Banners.FirstOrDefault(c => c.Message.Type == "penalty" && c.Message.IsAbout(Device.Car));

        /// <summary>The newest call that names this car.</summary>
        public RaceMessage? LastPersonal => Display.History.FirstOrDefault(m => m.IsAbout(Device.Car));

        public RaceCar? MyCar => Race?.Car(Device.Car);

        internal DriverState Copy() => (DriverState)MemberwiseClone();

        /// <summary>The kinds of test call the plugin can show for designing overlays and LEDs.</summary>
        public static readonly string[] PreviewKinds = { "safety_car", "penalty", "wave_around", "flag", "info" };

        /// <summary>
        /// A connected state showing one test call of <paramref name="kind"/> since <paramref name="serverNowMs"/>, for
        /// designing overlays and LEDs without a session. Never sent anywhere, never reported as shown.
        /// </summary>
        public static DriverState Preview(string kind, string? car, long serverNowMs)
        {
            var mine = car ?? "0";
            var (text, sticky, about) = kind switch
            {
                "safety_car" => ("SAFETY CAR: TEST", true, (string?)null),
                "penalty" => ($"DRIVE-THROUGH #{mine}: TEST", true, mine),
                "wave_around" => ("WAVE AROUND: TEST", false, null),
                "flag" => ("GREEN FLAG: TEST", false, null),
                _ => ("TEST CALL FROM PACELAPS", false, null),
            };
            var o = new Newtonsoft.Json.Linq.JObject
            {
                ["v"] = 1,
                ["id"] = "test_" + kind,
                ["cursor"] = "c_0_0",
                ["type"] = PreviewKinds.Contains(kind) ? kind : "info",
                ["text"] = text,
                ["affects"] = about == null
                    ? new Newtonsoft.Json.Linq.JObject { ["scope"] = "all" }
                    : new Newtonsoft.Json.Linq.JObject { ["scope"] = "car", ["cars"] = new Newtonsoft.Json.Linq.JArray(about) },
                ["visibleTo"] = new Newtonsoft.Json.Linq.JObject { ["scope"] = "all" },
                ["showAt"] = Iso.FromMs(serverNowMs),
                ["sticky"] = sticky,
                ["sound"] = kind == "wave_around" ? "wave" : kind,
            };
            if (kind == "flag")
            {
                // As a league's procedure would give its green flag (a flag call alone is neutral).
                o["colour"] = "green";
            }

            if (kind == "penalty")
            {
                o["detail"] = "A test: nothing to serve.";
            }

            var message = RaceMessage.From(o)!;
            return new DriverState
            {
                Status = ConnectionStatus.Connected,
                Device = new DeviceInfo(null, mine, null, false),
                Display = CallDisplay.Empty.Show(message, serverNowMs, 8),
            };
        }

        /// <summary>The preview's call, for its tone and events.</summary>
        public RaceMessage? PreviewCall => Display.Current?.Message;
    }
}
