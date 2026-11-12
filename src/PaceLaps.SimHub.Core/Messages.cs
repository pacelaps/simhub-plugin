using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace PaceLaps.Plugin.Core
{
    /// <summary>Who a message is about or reaches (contract common audience): all, class, car, ...</summary>
    public sealed class Audience
    {
        public static readonly Audience All = new Audience("all", new List<string>(), new List<int>());

        public Audience(string scope, IReadOnlyList<string> cars, IReadOnlyList<int> classes)
        {
            Scope = scope;
            Cars = cars;
            Classes = classes;
        }

        public string Scope { get; }
        public IReadOnlyList<string> Cars { get; }
        public IReadOnlyList<int> Classes { get; }

        internal static Audience From(JObject? o) =>
            o == null ? All : new Audience(o.Str("scope") ?? "all", o.Strings("cars"), o.Ints("classes"));
    }

    /// <summary>A race control call (contract message.schema.json). Unknown fields are ignored.</summary>
    public sealed class RaceMessage
    {
        private RaceMessage(string id, string cursor, string type, string text, string? detail, string showAt, long showAtMs,
            bool sticky, string? sound, IReadOnlyList<string> supersedes, Audience affects, Audience visibleTo)
        {
            Id = id;
            Cursor = cursor;
            Type = type;
            Text = text;
            Detail = detail;
            ShowAt = showAt;
            ShowAtMs = showAtMs;
            Sticky = sticky;
            Sound = sound;
            Supersedes = supersedes;
            Affects = affects;
            VisibleTo = visibleTo;
        }

        public string Id { get; }
        public string Cursor { get; }

        /// <summary>flag, safety_car, penalty, wave_around, info, system (others may come: shown as info).</summary>
        public string Type { get; }

        public string Text { get; }
        public string? Detail { get; }
        public string ShowAt { get; }
        public long ShowAtMs { get; }

        /// <summary>A state (a banner until superseded) rather than a one-off call.</summary>
        public bool Sticky { get; }

        public string? Sound { get; }
        public IReadOnlyList<string> Supersedes { get; }
        public Audience Affects { get; }
        public Audience VisibleTo { get; }

        /// <summary>The message, or null when a required field is missing or malformed.</summary>
        internal static RaceMessage? From(JObject? o)
        {
            if (o == null)
            {
                return null;
            }

            var id = o.Str("id");
            var cursor = o.Str("cursor");
            var type = o.Str("type");
            var text = o.Str("text");
            var showAt = o.Str("showAt");
            if (id == null || cursor == null || type == null || text == null || !Iso.TryToMs(showAt, out var showAtMs))
            {
                return null;
            }

            return new RaceMessage(id, cursor, type, text, o.Str("detail"), showAt!, showAtMs, o.Bool("sticky"), o.Str("sound"),
                o.Strings("supersedes"), Audience.From(o.Obj("affects")), Audience.From(o.Obj("visibleTo")));
        }

        public static RaceMessage? FromJson(string json) => From(Json.Object(json));

        /// <summary>A call that names this car (a penalty, a personal message).</summary>
        public bool IsAbout(string? car) => car != null && Affects.Scope == "car" && Affects.Cars.Contains(car);
    }
}
