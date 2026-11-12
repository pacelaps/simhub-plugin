using System.Net;
using Newtonsoft.Json.Linq;
using PaceLaps.Plugin.Core;

namespace PaceLaps.Plugin.Core.Tests;

/// <summary>
/// The device clock and its timers, moved by hand. Timers and delays fire in time order on <see cref="Advance"/>,
/// inline, so everything the client does in reaction has run when Advance returns.
/// </summary>
internal sealed class FakeTime : ISystemClock, ITimers
{
    private readonly List<(long At, long Seq, Action Fire)> _due = [];
    private long _seq;

    public FakeTime(long nowMs = 1_800_000_000_000) => NowMs = nowMs;

    public long NowMs { get; private set; }

    public int Waiting
    {
        get
        {
            lock (_due)
            {
                return _due.Count;
            }
        }
    }

    public IDisposable After(long delayMs, Action callback)
    {
        var entry = Add(delayMs, callback);
        return new Cancel(() => Remove(entry));
    }

    public Task Delay(long delayMs, CancellationToken cancellation)
    {
        var done = new TaskCompletionSource();
        var entry = Add(delayMs, () => done.TrySetResult());
        cancellation.Register(() =>
        {
            Remove(entry);
            done.TrySetCanceled(cancellation);
        });
        return done.Task;
    }

    /// <summary>Moves the clock on, firing everything due on the way.</summary>
    public void Advance(long ms)
    {
        var end = NowMs + ms;
        while (true)
        {
            (long At, long Seq, Action Fire) next;
            lock (_due)
            {
                if (_due.Count == 0)
                {
                    break;
                }

                next = _due.OrderBy(d => d.At).ThenBy(d => d.Seq).First();
                if (next.At > end)
                {
                    break;
                }

                _due.Remove(next);
            }

            NowMs = Math.Max(NowMs, next.At);
            next.Fire();
        }

        NowMs = end;
    }

    private (long, long, Action) Add(long delayMs, Action fire)
    {
        lock (_due)
        {
            var entry = (NowMs + Math.Max(0, delayMs), _seq++, fire);
            _due.Add(entry);
            return entry;
        }
    }

    private void Remove((long, long, Action) entry)
    {
        lock (_due)
        {
            _due.Remove(entry);
        }
    }

    private sealed class Cancel(Action cancel) : IDisposable
    {
        public void Dispose() => cancel();
    }
}

/// <summary>The server's device API: its clock runs <see cref="ServerAheadMs"/> ahead of the device's.</summary>
internal sealed class FakeApi(FakeTime time) : IDeviceApi
{
    public long ServerAheadMs { get; set; } = 2_000;
    public long ServerNowMs => time.NowMs + ServerAheadMs;
    public Func<string?, string>? StateJson { get; set; }
    public Exception? StateFails { get; set; }
    public List<string?> StateCursors { get; } = [];
    public List<ShownReport> Shown { get; } = [];
    public List<string> Authorized { get; } = [];
    public int ClockPings { get; private set; }

    public Task<ClockPong> Clock(long clientSentMs, CancellationToken cancellation)
    {
        ClockPings++;
        return Task.FromResult(new ClockPong(ServerNowMs, ServerNowMs));
    }

    public Task<StateSnapshot> State(string? cursor, CancellationToken cancellation)
    {
        StateCursors.Add(cursor);
        if (StateFails != null)
        {
            return Task.FromException<StateSnapshot>(StateFails);
        }

        var json = StateJson?.Invoke(cursor) ?? Snapshots.State(ServerNowMs);
        return Task.FromResult(StateSnapshot.FromJson(json)!);
    }

    Task IDeviceApi.Shown(ShownReport report, CancellationToken cancellation)
    {
        Shown.Add(report);
        return Task.CompletedTask;
    }

    public Task<string> Authorize(string socketId, string channel, CancellationToken cancellation)
    {
        Authorized.Add(channel);
        return Task.FromResult("key:sig-" + channel);
    }

    public Task<string> RealtimeKey(CancellationToken cancellation) => Task.FromResult("pacelaps-key");

    public static DeviceApiException SignedOut() => new("GET /api/v1/device/state: 401", HttpStatusCode.Unauthorized);
}

/// <summary>A realtime connection driven by the test.</summary>
internal sealed class FakeRealtime : IRealtime
{
    public event Action<string>? Connected;
    public event Action? Disconnected;
    public event Action<PusherFrame>? Received;

    public Uri? Uri { get; private set; }
    public List<string> Sent { get; } = [];
    public bool Up { get; private set; }

    public Task Run(Uri socketUri, CancellationToken cancellation)
    {
        Uri = socketUri;
        var done = new TaskCompletionSource();
        cancellation.Register(() => done.TrySetResult());
        return done.Task;
    }

    public Task<bool> Send(string frame, CancellationToken cancellation)
    {
        if (Up)
        {
            Sent.Add(frame);
        }

        return Task.FromResult(Up);
    }

    public void Connect(string socketId = "123.456")
    {
        Up = true;
        Connected?.Invoke(socketId);
    }

    public void Drop()
    {
        Up = false;
        Disconnected?.Invoke();
    }

    public void Push(string channel, string @event, JObject data) =>
        Received?.Invoke(new PusherFrame(@event, "private-" + channel, data.ToString(Newtonsoft.Json.Formatting.None)));

    public IEnumerable<string> Subscribed => Sent.Select(f => JObject.Parse(f))
        .Where(f => (string?)f["event"] == "pusher:subscribe")
        .Select(f => (string)f["data"]!["channel"]!);
}

/// <summary>Builders for server messages, by the contracts.</summary>
internal static class Snapshots
{
    public const int Session = 41;
    public const string Car = "12";
    public const int ClassId = 4098;
    public static readonly string[] BannerChannels = [$"event.{Session}.all", $"event.{Session}.class.{ClassId}", $"event.{Session}.car.{Car}"];
    public static readonly string LiveChannel = $"event.{Session}.live";
    public const string ControlChannel = "league-devices.7";

    public static JObject Message(string id, int seq, long showAtMs, string type = "flag", bool sticky = false, string text = "YELLOW FLAG",
        string[]? supersedes = null, string[]? aboutCars = null)
    {
        var o = new JObject
        {
            ["v"] = 1,
            ["id"] = id,
            ["cursor"] = $"c_{Session}_{seq}",
            ["type"] = type,
            ["text"] = text,
            ["affects"] = aboutCars == null ? new JObject { ["scope"] = "all" } : new JObject { ["scope"] = "car", ["cars"] = new JArray(aboutCars) },
            ["visibleTo"] = new JObject { ["scope"] = "all" },
            ["showAt"] = Iso.FromMs(showAtMs),
            ["sticky"] = sticky,
            ["sound"] = type,
        };
        if (supersedes != null)
        {
            o["supersedes"] = new JArray(supersedes);
        }

        return o;
    }

    public static string State(long serverNowMs, int seq = 10, JObject[]? banners = null, JObject[]? recent = null, int? session = Session,
        JObject? race = null, JObject? target = null) =>
        new JObject
        {
            ["v"] = 1,
            ["kind"] = "state_snapshot",
            ["serverTime"] = Iso.FromMs(serverNowMs),
            ["cursor"] = $"c_{session ?? 0}_{seq}",
            ["state"] = new JObject { ["phase"] = null, ["procedure"] = null },
            ["banners"] = new JArray(banners ?? []),
            ["recentMessages"] = new JArray(recent ?? []),
            ["target"] = target,
            ["race"] = race,
            ["session"] = session == null ? null : new JObject { ["id"] = session, ["name"] = "Race 3" },
            ["channels"] = session == null
                ? new JObject { ["banner"] = new JArray(), ["audit"] = new JArray() }
                : new JObject { ["banner"] = new JArray(BannerChannels), ["audit"] = new JArray(), ["live"] = new JArray(LiveChannel) },
            ["device"] = new JObject { ["league"] = 7, ["car"] = Car, ["classId"] = ClassId, ["spectator"] = false },
            ["oneOffSeconds"] = 8,
        }.ToString();
}
