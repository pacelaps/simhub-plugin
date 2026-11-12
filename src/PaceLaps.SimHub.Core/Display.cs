using System;
using System.Collections.Generic;
using System.Linq;

namespace PaceLaps.SimHub.Core
{
    /// <summary>A call on display: the message, when it was shown (server ms) and, for one-offs, until when.</summary>
    public sealed class ShownCall
    {
        public ShownCall(RaceMessage message, long shownAtMs, long? untilMs)
        {
            Message = message;
            ShownAtMs = shownAtMs;
            UntilMs = untilMs;
        }

        public RaceMessage Message { get; }
        public long ShownAtMs { get; }
        public long? UntilMs { get; }
    }

    /// <summary>
    /// What a driver view shows, as plain immutable data (as the web views' display.js): the sticky banners (states,
    /// until superseded), one-off calls (shown briefly) and the history, newest first.
    /// </summary>
    public sealed class CallDisplay
    {
        public const int HistoryLimit = 50;

        public static readonly CallDisplay Empty =
            new CallDisplay(new List<ShownCall>(), new List<ShownCall>(), new List<RaceMessage>());

        public CallDisplay(IReadOnlyList<ShownCall> banners, IReadOnlyList<ShownCall> oneOffs, IReadOnlyList<RaceMessage> history)
        {
            Banners = banners;
            OneOffs = oneOffs;
            History = history;
        }

        public IReadOnlyList<ShownCall> Banners { get; }
        public IReadOnlyList<ShownCall> OneOffs { get; }
        public IReadOnlyList<RaceMessage> History { get; }

        /// <summary>The call a one-line display shows: the newest one-off while it lasts, else the main banner.</summary>
        public ShownCall? Current => OneOffs.FirstOrDefault() ?? Banners.FirstOrDefault();

        /// <summary>
        /// The display after a message is shown. Sticky messages become banners, newest first; one-offs last
        /// <paramref name="oneOffSeconds"/>. Superseded messages leave the banners. Each message enters the history once.
        /// </summary>
        public CallDisplay Show(RaceMessage message, long serverNowMs, int oneOffSeconds)
        {
            var gone = new HashSet<string>(message.Supersedes) { message.Id };
            var banners = Banners.Where(c => !gone.Contains(c.Message.Id)).ToList();
            var oneOffs = OneOffs.Where(c => !gone.Contains(c.Message.Id)).ToList();
            if (message.Sticky)
            {
                banners.Insert(0, new ShownCall(message, serverNowMs, null));
            }
            else
            {
                oneOffs.Insert(0, new ShownCall(message, serverNowMs, serverNowMs + oneOffSeconds * 1000L));
            }

            return new CallDisplay(banners, oneOffs, WithHistory(History, message));
        }

        /// <summary>Drops one-offs whose time is up; the same instance when nothing changed.</summary>
        public CallDisplay Expire(long serverNowMs)
        {
            var oneOffs = OneOffs.Where(c => c.UntilMs > serverNowMs).ToList();
            return oneOffs.Count == OneOffs.Count ? this : new CallDisplay(Banners, oneOffs, History);
        }

        /// <summary>The banners as the server has them now (resync); one-offs and history stay.</summary>
        public CallDisplay WithBanners(IEnumerable<RaceMessage> banners) =>
            new CallDisplay(banners.Select(m => new ShownCall(m, m.ShowAtMs, null)).ToList(), OneOffs, History);

        /// <summary>
        /// The display from a state snapshot: its banners and the history of its recent messages. Messages still to
        /// come (showAt in the future) are returned in <paramref name="due"/> to be scheduled.
        /// </summary>
        public static CallDisplay FromSnapshot(StateSnapshot snapshot, long serverNowMs, out IReadOnlyList<RaceMessage> due)
        {
            var later = new List<RaceMessage>();
            var shown = new Dictionary<string, RaceMessage>();
            foreach (var m in snapshot.RecentMessages)
            {
                if (m.ShowAtMs > serverNowMs)
                {
                    later.Add(m);
                }
                else
                {
                    shown[m.Id] = m;
                }
            }

            due = later;
            var history = shown.Values.OrderByDescending(m => m.ShowAtMs).Take(HistoryLimit).ToList();
            return new CallDisplay(snapshot.Banners.Select(m => new ShownCall(m, m.ShowAtMs, null)).ToList(), new List<ShownCall>(), history);
        }

        private static IReadOnlyList<RaceMessage> WithHistory(IReadOnlyList<RaceMessage> history, RaceMessage message) =>
            history.Any(m => m.Id == message.Id)
                ? history
                : new[] { message }.Concat(history).Take(HistoryLimit).ToList();
    }

    /// <summary>Message cursors ("c_&lt;session&gt;_&lt;seq&gt;"): a reconnecting device asks for what came after its own.</summary>
    public static class Cursors
    {
        /// <summary>The later of two cursors of the same session; across sessions the newer one (b).</summary>
        public static string? Later(string? a, string? b)
        {
            if (a == null)
            {
                return b;
            }

            if (b == null)
            {
                return a;
            }

            var pa = a.Split('_');
            var pb = b.Split('_');
            var sessionA = pa.Length > 1 ? pa[1] : "";
            var sessionB = pb.Length > 1 ? pb[1] : "";
            if (sessionA != sessionB)
            {
                return b;
            }

            return Seq(pb) > Seq(pa) ? b : a;
        }

        private static long Seq(string[] parts) => parts.Length > 2 && long.TryParse(parts[2], out var n) ? n : -1;
    }

    /// <summary>Milliseconds from now (device clock) until a showAt (server clock); &lt;= 0: show now.</summary>
    public static class Schedule
    {
        public static long DelayUntil(long showAtMs, long deviceNowMs, double offsetMs) =>
            showAtMs - (deviceNowMs + (long)Math.Round(offsetMs));
    }
}
