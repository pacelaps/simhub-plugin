using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace PaceLaps.SimHub.Core
{
    /// <summary>The car's place while the field is sorted: place in class and the car to follow.</summary>
    public sealed class SortingTarget
    {
        public SortingTarget(int posClass, string? followCar)
        {
            PosClass = posClass;
            FollowCar = followCar;
        }

        public int PosClass { get; }
        public string? FollowCar { get; }

        internal static SortingTarget? From(JObject? o)
        {
            var pos = o?.Int("posClass");
            return pos == null ? null : new SortingTarget(pos.Value, o!.Str("followCar"));
        }
    }

    public sealed class RaceCar
    {
        public RaceCar(string num, int? classId, int pos, int posClass, bool onPitRoad, bool connected)
        {
            Num = num;
            ClassId = classId;
            Pos = pos;
            PosClass = posClass;
            OnPitRoad = onPitRoad;
            Connected = connected;
        }

        public string Num { get; }
        public int? ClassId { get; }
        public int Pos { get; }
        public int PosClass { get; }
        public bool OnPitRoad { get; }
        public bool Connected { get; }
    }

    /// <summary>
    /// The race as every driver may see it (contract raceState): the flag, the session, the running order by car
    /// number. Pushed as race.state on the live channel.
    /// </summary>
    public sealed class RaceState
    {
        private RaceState(long atMs, string flagState, string flagLabel, string flagDetail, string? sessionType, string? track,
            int? lap, int? laps, double? timeLeftSec, bool finished, IReadOnlyList<RaceCar> cars)
        {
            AtMs = atMs;
            FlagState = flagState;
            FlagLabel = flagLabel;
            FlagDetail = flagDetail;
            SessionType = sessionType;
            Track = track;
            Lap = lap;
            Laps = laps;
            TimeLeftSec = timeLeftSec;
            Finished = finished;
            Cars = cars;
        }

        public long AtMs { get; }

        /// <summary>green, green_held, yellow, red, white, grey.</summary>
        public string FlagState { get; }

        public string FlagLabel { get; }
        public string FlagDetail { get; }
        public string? SessionType { get; }
        public string? Track { get; }
        public int? Lap { get; }
        public int? Laps { get; }

        /// <summary>As of <see cref="AtMs"/>.</summary>
        public double? TimeLeftSec { get; }

        public bool Finished { get; }
        public IReadOnlyList<RaceCar> Cars { get; }

        public RaceCar? Car(string? num) => num == null ? null : Cars.FirstOrDefault(c => c.Num == num);

        public static RaceState? FromJson(string json) => From(Json.Object(json));

        /// <summary>From an object read with dates kept as strings (<see cref="FromJson"/>; JObject.Parse turns them into dates).</summary>
        internal static RaceState? From(JObject? o)
        {
            var flag = o?.Obj("flag");
            var session = o?.Obj("session");
            if (o == null || flag == null || session == null || !Iso.TryToMs(o.Str("at"), out var at))
            {
                return null;
            }

            var cars = o.Objects("cars")
                .Where(c => c.Str("num") != null && c.Int("pos") != null)
                .Select(c => new RaceCar(c.Str("num")!, c.Int("classId"), c.Int("pos")!.Value, c.Int("posClass") ?? 0,
                    c.Bool("onPitRoad"), c.Bool("connected")))
                .ToList();
            return new RaceState(at, flag.Str("state") ?? "grey", flag.Str("label") ?? "", flag.Str("detail") ?? "",
                session.Str("type"), session.Str("track"), session.Int("lap"), session.Int("laps"), session.Num("timeLeftSec"),
                session.Bool("finished"), cars);
        }
    }

    /// <summary>The device's channels by purpose (state snapshot channels).</summary>
    public sealed class DeviceChannels
    {
        public static readonly DeviceChannels None = new DeviceChannels(new List<string>(), new List<string>());

        public DeviceChannels(IReadOnlyList<string> banner, IReadOnlyList<string> live)
        {
            Banner = banner;
            Live = live;
        }

        /// <summary>Calls for this device: all, its class, its car (or the spectators').</summary>
        public IReadOnlyList<string> Banner { get; }

        /// <summary>The race state (and the driver page's map, lap and weather data, which the plugin ignores).</summary>
        public IReadOnlyList<string> Live { get; }
    }

    public sealed class DeviceInfo
    {
        public DeviceInfo(int? league, string? car, int? classId, bool spectator)
        {
            League = league;
            Car = car;
            ClassId = classId;
            Spectator = spectator;
        }

        public int? League { get; }
        public string? Car { get; }
        public int? ClassId { get; }
        public bool Spectator { get; }
    }

    /// <summary>
    /// The state on connect or resync (contract stateSnapshot, plus the fields the server adds for its views: session,
    /// channels, device, oneOffSeconds).
    /// </summary>
    public sealed class StateSnapshot
    {
        public string Cursor { get; private set; } = "";
        public long ServerTimeMs { get; private set; }
        public string? Phase { get; private set; }
        public string? Procedure { get; private set; }
        public IReadOnlyList<RaceMessage> Banners { get; private set; } = new List<RaceMessage>();
        public IReadOnlyList<RaceMessage> RecentMessages { get; private set; } = new List<RaceMessage>();
        public SortingTarget? Target { get; private set; }

        /// <summary>Whether the server sent a race state at all (older servers send none; null means no telemetry).</summary>
        public bool HasRace { get; private set; }

        public RaceState? Race { get; private set; }
        public int? SessionId { get; private set; }
        public string? SessionName { get; private set; }
        public DeviceChannels Channels { get; private set; } = DeviceChannels.None;
        public DeviceInfo Device { get; private set; } = new DeviceInfo(null, null, null, false);
        public int OneOffSeconds { get; private set; } = 8;

        internal static StateSnapshot? From(JObject? o)
        {
            if (o == null || o.Str("cursor") == null || !Iso.TryToMs(o.Str("serverTime"), out var serverTime))
            {
                return null;
            }

            var state = o.Obj("state");
            var session = o.Obj("session");
            var channels = o.Obj("channels");
            var device = o.Obj("device");
            var car = device?["car"];
            return new StateSnapshot
            {
                Cursor = o.Str("cursor")!,
                ServerTimeMs = serverTime,
                Phase = state?.Str("phase"),
                Procedure = state?.Str("procedure"),
                Banners = Messages(o, "banners"),
                RecentMessages = Messages(o, "recentMessages"),
                Target = SortingTarget.From(o.Obj("target")),
                HasRace = o.ContainsKey("race"),
                Race = RaceState.From(o.Obj("race")),
                SessionId = session?.Int("id"),
                SessionName = session?.Str("name"),
                Channels = channels == null ? DeviceChannels.None : new DeviceChannels(channels.Strings("banner"), channels.Strings("live")),
                Device = new DeviceInfo(device?.Int("league"), car == null || car.Type == JTokenType.Null ? null : car.ToString(),
                    device?.Int("classId"), device?.Bool("spectator") ?? false),
                OneOffSeconds = o.Int("oneOffSeconds") ?? 8,
            };
        }

        public static StateSnapshot? FromJson(string json) => From(Json.Object(json));

        private static IReadOnlyList<RaceMessage> Messages(JObject o, string name) =>
            o.Objects(name).Select(RaceMessage.From).Where(m => m != null).Select(m => m!).ToList();
    }
}
