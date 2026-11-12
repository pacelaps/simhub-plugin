using System;
using System.Collections.Generic;
using System.Linq;

namespace PaceLaps.Plugin.Core
{
    /// <summary>The race state palette (the web service's tokens, dark theme), as #RRGGBB.</summary>
    public static class Palette
    {
        public const string Green = "#22C55E";
        public const string Yellow = "#FACC15";
        public const string Red = "#DC2626";
        public const string Blue = "#155DFC";
        public const string White = "#FAFAFA";
        public const string Grey = "#71717A";
        public const string Dark = "#1C1917";

        /// <summary>A flag state's colour (race state flag.state); grey for anything else.</summary>
        public static string ForFlag(string? state) => state switch
        {
            "green" => Green,
            "green_held" => Green,
            "yellow" => Yellow,
            "red" => Red,
            "white" => White,
            _ => Grey,
        };

        /// <summary>Readable text on <paramref name="background"/>.</summary>
        public static string TextOn(string background) =>
            background == Yellow || background == Green || background == White ? Dark : "#FFFFFF";
    }

    /// <summary>
    /// What the plugin's SimHub properties show, computed from a <see cref="DriverState"/> at a server time. Pure: the
    /// SimHub adapter only binds these values to property names. Strings are never null (SimHub shows "" as empty).
    /// </summary>
    public sealed class PluginView
    {
        /// <summary>How long a new call counts as new (LEDs flash, the overlay highlights it).</summary>
        public const double NewSeconds = 5;

        public static readonly PluginView NotSetUp = From(DriverState.NotSetUp, 0);

        private PluginView()
        {
        }

        public string Status { get; private set; } = "";
        public bool Connected { get; private set; }

        /// <summary>Lost the connection after having it: the overlay must say calls may be missing.</summary>
        public bool Offline { get; private set; }

        public string Problem { get; private set; } = "";
        public string Car { get; private set; } = "";
        public string Session { get; private set; } = "";

        public bool CallActive { get; private set; }
        public string CallText { get; private set; } = "";
        public string CallDetail { get; private set; } = "";

        /// <summary>flag, safety_car, penalty, wave_around, info, system.</summary>
        public string CallKind { get; private set; } = "";

        public string CallColour { get; private set; } = "";
        public string CallTextColour { get; private set; } = "";
        public double CallAgeSeconds { get; private set; }
        public bool CallNew { get; private set; }
        public bool CallSticky { get; private set; }

        /// <summary>The call names this car (a penalty, a personal message).</summary>
        public bool CallMine { get; private set; }

        public string CallId { get; private set; } = "";

        /// <summary>The lasting state on screen (a safety car, Code 60, this car's penalty): the main banner.</summary>
        public bool BannerActive { get; private set; }

        public string BannerText { get; private set; } = "";
        public string BannerDetail { get; private set; } = "";
        public string BannerKind { get; private set; } = "";
        public string BannerColour { get; private set; } = "";
        public string BannerTextColour { get; private set; } = "";
        public bool BannerMine { get; private set; }
        public double BannerAgeSeconds { get; private set; }

        /// <summary>The newest one-off call while it lasts (a wave around, a flag call, an info).</summary>
        public bool NoticeActive { get; private set; }

        public string NoticeText { get; private set; } = "";
        public string NoticeDetail { get; private set; } = "";
        public string NoticeKind { get; private set; } = "";
        public string NoticeColour { get; private set; } = "";
        public string NoticeTextColour { get; private set; } = "";
        public bool NoticeMine { get; private set; }

        /// <summary>The notice concerns this car's class (a class wave around).</summary>
        public bool NoticeMyClass { get; private set; }

        public double NoticeAgeSeconds { get; private set; }

        public string FlagState { get; private set; } = "";
        public string FlagLabel { get; private set; } = "";
        public string FlagColour { get; private set; } = "";

        public string Phase { get; private set; } = "";
        public string Procedure { get; private set; } = "";
        public bool ProcedureRunning { get; private set; }

        public bool SortingActive { get; private set; }
        public int SortingPosition { get; private set; }
        public string SortingFollow { get; private set; } = "";

        public bool PenaltyActive { get; private set; }
        public string PenaltyText { get; private set; } = "";
        public string PenaltyDetail { get; private set; } = "";

        public string PersonalText { get; private set; } = "";

        public int Position { get; private set; }
        public int ClassPosition { get; private set; }
        public bool OnPitRoad { get; private set; }
        public int Lap { get; private set; }
        public int Laps { get; private set; }
        public double TimeLeftSeconds { get; private set; }

        /// <summary>
        /// The colour for LEDs now, "" for none: this car's penalty, else the current call, else a yellow or red race
        /// flag. Nothing while offline (stale colours would mislead).
        /// </summary>
        public string LedColour { get; private set; } = "";

        /// <summary>The LED colour comes from a call that is still new: flash.</summary>
        public bool LedFlash { get; private set; }

        public double ClockOffsetMs { get; private set; }
        public double ClockRttMs { get; private set; }

        public static PluginView From(DriverState s, long serverNowMs)
        {
            var v = new PluginView
            {
                Status = StatusName(s.Status),
                Connected = s.Status == ConnectionStatus.Connected,
                Offline = s.Status == ConnectionStatus.Offline,
                Problem = s.Problem ?? "",
                Car = s.Device.Car ?? "",
                Session = s.SessionName ?? "",
                Phase = s.Phase ?? "",
                Procedure = s.Procedure ?? "",
                ProcedureRunning = s.Procedure != null,
                ClockOffsetMs = s.Clock?.OffsetMs ?? 0,
                ClockRttMs = s.Clock?.RttMs ?? 0,
            };

            var race = s.Race;
            if (race != null)
            {
                v.FlagState = race.FlagState;
                v.FlagLabel = race.FlagLabel;
                v.FlagColour = Palette.ForFlag(race.FlagState);
                v.Lap = race.Lap ?? 0;
                v.Laps = race.Laps ?? 0;
                v.TimeLeftSeconds = race.TimeLeftSec == null ? 0 : Math.Max(0, race.TimeLeftSec.Value - Math.Max(0, serverNowMs - race.AtMs) / 1000.0);
                var car = s.MyCar;
                if (car != null)
                {
                    v.Position = car.Pos;
                    v.ClassPosition = car.PosClass;
                    v.OnPitRoad = car.OnPitRoad;
                }
            }

            var call = s.Current;
            if (call != null)
            {
                var m = call.Message;
                v.CallActive = true;
                v.CallText = m.Text;
                v.CallDetail = m.Detail ?? "";
                v.CallKind = m.Type;
                v.CallColour = ColourOf(m, race);
                v.CallTextColour = Palette.TextOn(v.CallColour);
                v.CallAgeSeconds = Math.Max(0, (serverNowMs - call.ShownAtMs) / 1000.0);
                v.CallNew = v.CallAgeSeconds < NewSeconds;
                v.CallSticky = m.Sticky;
                v.CallMine = m.IsAbout(s.Device.Car);
                v.CallId = m.Id;
            }

            var banner = s.Display.Banners.FirstOrDefault();
            if (banner != null)
            {
                var m = banner.Message;
                v.BannerActive = true;
                v.BannerText = m.Text;
                v.BannerDetail = m.Detail ?? "";
                v.BannerKind = m.Type;
                v.BannerColour = ColourOf(m, race);
                v.BannerTextColour = Palette.TextOn(v.BannerColour);
                v.BannerMine = m.IsAbout(s.Device.Car);
                v.BannerAgeSeconds = Math.Max(0, (serverNowMs - banner.ShownAtMs) / 1000.0);
            }

            var notice = s.Display.OneOffs.FirstOrDefault();
            if (notice != null)
            {
                var m = notice.Message;
                v.NoticeActive = true;
                v.NoticeText = m.Text;
                v.NoticeDetail = m.Detail ?? "";
                v.NoticeKind = m.Type;
                v.NoticeColour = ColourOf(m, race);
                v.NoticeTextColour = Palette.TextOn(v.NoticeColour);
                v.NoticeMine = m.IsAbout(s.Device.Car);
                v.NoticeMyClass = m.Affects.Scope == "class" && s.Device.ClassId != null && m.Affects.Classes.Contains(s.Device.ClassId.Value);
                v.NoticeAgeSeconds = Math.Max(0, (serverNowMs - notice.ShownAtMs) / 1000.0);
            }

            if (s.Target != null)
            {
                v.SortingActive = true;
                v.SortingPosition = s.Target.PosClass;
                v.SortingFollow = s.Target.FollowCar ?? "";
            }

            var penalty = s.MyPenalty;
            if (penalty != null)
            {
                v.PenaltyActive = true;
                v.PenaltyText = penalty.Message.Text;
                v.PenaltyDetail = penalty.Message.Detail ?? "";
            }

            v.PersonalText = s.LastPersonal?.Text ?? "";

            if (!v.Offline && v.Connected)
            {
                if (penalty != null)
                {
                    v.LedColour = Palette.Red;
                    v.LedFlash = Math.Max(0, (serverNowMs - penalty.ShownAtMs) / 1000.0) < NewSeconds;
                }
                else if (call != null)
                {
                    v.LedColour = v.CallColour;
                    v.LedFlash = v.CallNew;
                }
                else if (race != null && (race.FlagState == "yellow" || race.FlagState == "red"))
                {
                    v.LedColour = v.FlagColour;
                }
            }

            return v;
        }

        /// <summary>A call's colour, as on the driver page: a flag call takes the race's flag colour.</summary>
        public static string ColourOf(RaceMessage m, RaceState? race) => m.Type switch
        {
            "safety_car" => Palette.Yellow,
            "penalty" => Palette.Red,
            "wave_around" => Palette.Blue,
            "flag" => race == null ? Palette.White : Palette.ForFlag(race.FlagState),
            _ => Palette.Grey,
        };

        /// <summary>
        /// Every SimHub property, by name (SimHub shows them as PaceLapsPlugin.&lt;name&gt;). The plugin binds exactly these;
        /// the overlays' formulas may use only these (a test checks).
        /// </summary>
        public static readonly IReadOnlyList<(string Name, Func<PluginView, object> Value)> Properties = new (string, Func<PluginView, object>)[]
        {
            ("Status", v => v.Status),
            ("Connected", v => v.Connected),
            ("Offline", v => v.Offline),
            ("Problem", v => v.Problem),
            ("Car", v => v.Car),
            ("Session", v => v.Session),

            ("Call.Active", v => v.CallActive),
            ("Call.Text", v => v.CallText),
            ("Call.Detail", v => v.CallDetail),
            ("Call.Kind", v => v.CallKind),
            ("Call.Color", v => v.CallColour),
            ("Call.TextColor", v => v.CallTextColour),
            ("Call.AgeSeconds", v => v.CallAgeSeconds),
            ("Call.New", v => v.CallNew),
            ("Call.Sticky", v => v.CallSticky),
            ("Call.Mine", v => v.CallMine),

            ("Banner.Active", v => v.BannerActive),
            ("Banner.Text", v => v.BannerText),
            ("Banner.Detail", v => v.BannerDetail),
            ("Banner.Kind", v => v.BannerKind),
            ("Banner.Color", v => v.BannerColour),
            ("Banner.TextColor", v => v.BannerTextColour),
            ("Banner.Mine", v => v.BannerMine),
            ("Banner.AgeSeconds", v => v.BannerAgeSeconds),

            ("Notice.Active", v => v.NoticeActive),
            ("Notice.Text", v => v.NoticeText),
            ("Notice.Detail", v => v.NoticeDetail),
            ("Notice.Kind", v => v.NoticeKind),
            ("Notice.Color", v => v.NoticeColour),
            ("Notice.TextColor", v => v.NoticeTextColour),
            ("Notice.Mine", v => v.NoticeMine),
            ("Notice.MyClass", v => v.NoticeMyClass),
            ("Notice.AgeSeconds", v => v.NoticeAgeSeconds),

            ("Flag.State", v => v.FlagState),
            ("Flag.Label", v => v.FlagLabel),
            ("Flag.Color", v => v.FlagColour),

            ("Procedure.Name", v => v.Procedure),
            ("Procedure.Phase", v => v.Phase),
            ("Procedure.Running", v => v.ProcedureRunning),

            ("Sorting.Active", v => v.SortingActive),
            ("Sorting.Position", v => v.SortingPosition),
            ("Sorting.Follow", v => v.SortingFollow),

            ("Penalty.Active", v => v.PenaltyActive),
            ("Penalty.Text", v => v.PenaltyText),
            ("Penalty.Detail", v => v.PenaltyDetail),
            ("Personal.Text", v => v.PersonalText),

            ("Race.Position", v => v.Position),
            ("Race.ClassPosition", v => v.ClassPosition),
            ("Race.OnPitRoad", v => v.OnPitRoad),
            ("Race.Lap", v => v.Lap),
            ("Race.Laps", v => v.Laps),
            ("Race.TimeLeftSeconds", v => v.TimeLeftSeconds),

            ("Led.Color", v => v.LedColour),
            ("Led.Flash", v => v.LedFlash),

            ("Clock.OffsetMs", v => v.ClockOffsetMs),
            ("Clock.RttMs", v => v.ClockRttMs),
        };

        public static string StatusName(ConnectionStatus status) => status switch
        {
            ConnectionStatus.NotSetUp => "not_set_up",
            ConnectionStatus.Connecting => "connecting",
            ConnectionStatus.Connected => "connected",
            ConnectionStatus.Offline => "offline",
            _ => "signed_out",
        };
    }

    /// <summary>A distinct tone per kind of call, as the web views play them: (frequency Hz, seconds) steps.</summary>
    public static class Tones
    {
        private static readonly Dictionary<string, (double Hz, double Seconds)[]> ByName = new Dictionary<string, (double, double)[]>
        {
            ["flag"] = new[] { (880.0, 0.18), (660.0, 0.18), (880.0, 0.18) },
            ["safety_car"] = new[] { (523.0, 0.25), (523.0, 0.25), (523.0, 0.25) },
            ["penalty"] = new[] { (392.0, 0.35), (330.0, 0.45) },
            ["wave"] = new[] { (660.0, 0.12), (880.0, 0.12), (1046.0, 0.2) },
            ["info"] = new[] { (784.0, 0.15) },
            ["system"] = new[] { (440.0, 0.15) },
        };

        /// <summary>Gap between steps, seconds.</summary>
        public const double Gap = 0.04;

        /// <summary>The message's sound, else its type's, else info's.</summary>
        public static IReadOnlyList<(double Hz, double Seconds)> For(string? sound, string? type) =>
            sound != null && ByName.TryGetValue(sound, out var s) ? s
            : type != null && ByName.TryGetValue(type, out var t) ? t
            : ByName["info"];
    }
}
