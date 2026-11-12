using Newtonsoft.Json.Linq;
using PaceLaps.SimHub.Core;

namespace PaceLaps.SimHub.Core.Tests;

/// <summary>The synchronized display protocol end to end, with a fake server, socket and clock.</summary>
public sealed class RaceControlClientTests : IDisposable
{
    private readonly FakeTime _time = new();
    private readonly FakeApi _api;
    private readonly FakeRealtime _realtime = new();
    private readonly RaceControlClient _client;
    private readonly List<RaceMessage> _shown = [];
    private readonly List<string> _log = [];

    public RaceControlClientTests()
    {
        _api = new FakeApi(_time);
        _client = new RaceControlClient(_api, _realtime, _time, _time, new Uri("https://app.pacelaps.com/"), "0.1.0", _log.Add);
        _client.CallShown += _shown.Add;
    }

    public void Dispose() => _client.Dispose();

    private async Task Connected()
    {
        await _client.Start();
        _realtime.Connect();
    }

    private void Push(JObject message, string? channel = null) => _realtime.Push(channel ?? Snapshots.BannerChannels[0], "message", message);

    [Fact]
    public async Task It_loads_the_state_then_connects_to_the_sites_realtime_server()
    {
        await _client.Start();

        Assert.Equal(ConnectionStatus.Connecting, _client.State.Status);
        Assert.Equal(41, _client.State.SessionId);
        Assert.Equal("12", _client.State.Device.Car);
        Assert.Equal(2_000, _client.State.Clock!.Value.OffsetMs);
        Assert.Equal(new Uri("wss://app.pacelaps.com/app/pacelaps-key?protocol=7&client=pacelaps-simhub&version=0.1.0&flash=false"), _realtime.Uri);
    }

    [Fact]
    public async Task On_connect_it_joins_the_devices_channels_with_their_auth()
    {
        await Connected();

        string[] expected = [.. Snapshots.BannerChannels.Select(c => "private-" + c), "private-" + Snapshots.LiveChannel, "private-" + Snapshots.ControlChannel];
        Assert.Equal(expected.Order(), _realtime.Subscribed.Order());
        Assert.Equal(expected.Order(), _api.Authorized.Order());
        Assert.Equal(ConnectionStatus.Connected, _client.State.Status);
    }

    [Fact]
    public async Task A_call_is_shown_when_the_server_clock_reaches_its_show_at()
    {
        await Connected();
        var showAt = _api.ServerNowMs + 1_500;

        Push(Snapshots.Message("m1", 11, showAt, "safety_car", sticky: true, text: "SAFETY CAR"));
        _time.Advance(1_499);
        Assert.Empty(_shown);
        Assert.Null(_client.State.Current);

        _time.Advance(1);
        Assert.Equal("m1", Assert.Single(_shown).Id);
        Assert.Equal("SAFETY CAR", _client.State.Current!.Message.Text);
        var report = Assert.Single(_api.Shown);
        Assert.False(report.Late);
        Assert.Equal(showAt, report.ShownAtMs);
        Assert.Equal(2_000, report.OffsetMs);
    }

    [Fact]
    public async Task A_call_that_arrives_after_its_show_at_is_shown_at_once_and_reported_late()
    {
        await Connected();

        Push(Snapshots.Message("m1", 11, _api.ServerNowMs - 300));

        Assert.Single(_shown);
        Assert.True(Assert.Single(_api.Shown).Late);
    }

    [Fact]
    public async Task The_same_call_on_two_channels_is_shown_once()
    {
        await Connected();
        var message = Snapshots.Message("m1", 11, _api.ServerNowMs);

        Push(message, Snapshots.BannerChannels[0]);
        Push(message, Snapshots.BannerChannels[2]);

        Assert.Single(_shown);
    }

    [Fact]
    public async Task A_one_off_call_goes_after_its_time_and_a_state_stays()
    {
        await Connected();
        Push(Snapshots.Message("sc", 11, _api.ServerNowMs, "safety_car", sticky: true));
        Push(Snapshots.Message("pits", 12, _api.ServerNowMs, "info"));
        Assert.Equal("pits", _client.State.Current!.Message.Id);

        _time.Advance(8_000);

        Assert.Equal("sc", _client.State.Current!.Message.Id);
    }

    [Fact]
    public async Task Events_on_channels_it_did_not_join_are_ignored()
    {
        await Connected();

        _realtime.Push("event.41.car.33", "message", Snapshots.Message("m1", 11, _api.ServerNowMs));

        Assert.Empty(_shown);
    }

    [Fact]
    public async Task Offline_at_once_and_on_return_it_resyncs_from_its_cursor()
    {
        await Connected();
        Push(Snapshots.Message("m1", 11, _api.ServerNowMs));

        _realtime.Drop();
        Assert.Equal(ConnectionStatus.Offline, _client.State.Status);

        var missed = Snapshots.Message("m2", 12, _api.ServerNowMs - 1_000, "penalty", sticky: true, aboutCars: ["12"]);
        _api.StateJson = cursor => Snapshots.State(_api.ServerNowMs, 12, banners: [missed], recent: [missed]);
        _realtime.Sent.Clear();
        _realtime.Connect("789.1");

        Assert.Equal("c_41_11", _api.StateCursors.Last());
        Assert.Equal(ConnectionStatus.Connected, _client.State.Status);
        Assert.Equal(5, _realtime.Subscribed.Count());
        Assert.Equal(["m1", "m2"], _shown.Select(m => m.Id));
        Assert.True(_api.Shown.Last().Late);
        Assert.Equal("m2", _client.State.MyPenalty!.Message.Id);
        Assert.Equal("m2", _client.State.LastPersonal!.Id);
    }

    [Fact]
    public async Task The_race_state_follows_the_live_channel()
    {
        await Connected();

        _realtime.Push(Snapshots.LiveChannel, "race.state", RaceState("yellow", ("7", 1, 1), ("12", 2, 1)));

        Assert.Equal("yellow", _client.State.Race!.FlagState);
        Assert.Equal(2, _client.State.MyCar!.Pos);
    }

    [Fact]
    public async Task A_target_change_reloads_the_cars_sorting_target()
    {
        await Connected();
        _api.StateJson = _ => Snapshots.State(_api.ServerNowMs, 11, target: new JObject { ["posClass"] = 3, ["followCar"] = "7" });

        _realtime.Push(Snapshots.BannerChannels[2], "target.changed", new JObject());

        Assert.Equal(3, _client.State.Target!.PosClass);
        Assert.Equal("7", _client.State.Target.FollowCar);
    }

    [Fact]
    public async Task A_new_session_loads_its_state_from_scratch()
    {
        await Connected();
        Push(Snapshots.Message("m1", 11, _api.ServerNowMs, "safety_car", sticky: true));
        _api.StateJson = _ => Snapshots.State(_api.ServerNowMs, 0, session: null);

        _realtime.Push(Snapshots.ControlChannel, "session.changed", new JObject());

        Assert.Null(_api.StateCursors.Last());
        Assert.Null(_client.State.SessionId);
        Assert.Null(_client.State.Current);
    }

    [Fact]
    public async Task A_url_that_no_longer_works_stops_everything()
    {
        _api.StateFails = FakeApi.SignedOut();

        await _client.Start();

        Assert.Equal(ConnectionStatus.SignedOut, _client.State.Status);
        Assert.Contains("no longer works", _client.State.Problem, StringComparison.Ordinal);
        Assert.Null(_realtime.Uri);
    }

    [Fact]
    public async Task An_unreachable_server_is_retried()
    {
        _api.StateFails = new DeviceApiException("GET /api/v1/device/state: connection refused");
        await _client.Start();
        Assert.Equal("Cannot reach race control.", _client.State.Problem);

        _api.StateFails = null;
        _time.Advance(RaceControlClient.RetryMs);

        Assert.Null(_client.State.Problem);
        Assert.Equal(41, _client.State.SessionId);
    }

    [Fact]
    public async Task It_pings_once_a_minute_and_measures_fully_every_five()
    {
        await Connected();
        _time.Advance(1_000);
        var start = _api.ClockPings;
        Assert.Equal(8, start); // 1, then 7 more

        _time.Advance(RaceControlClient.HeartbeatMs);
        Assert.Equal(start + 1, _api.ClockPings);

        _time.Advance(RaceControlClient.ClockResyncMs - RaceControlClient.HeartbeatMs);
        Assert.Equal(start + 4 + 4, _api.ClockPings);
    }

    [Fact]
    public async Task The_token_never_reaches_the_log()
    {
        _api.StateFails = new DeviceApiException("GET /api/v1/device/state: 500");
        await _client.Start();

        Assert.All(_log, line => Assert.DoesNotContain("X-Overlay-Token", line, StringComparison.Ordinal));
    }

    private static JObject RaceState(string flag, params (string Num, int Pos, int PosClass)[] cars) => new()
    {
        ["v"] = 1,
        ["kind"] = "race_state",
        ["at"] = Iso.FromMs(1_800_000_000_000),
        ["flag"] = new JObject { ["state"] = flag, ["label"] = "Caution", ["detail"] = "" },
        ["session"] = new JObject { ["type"] = "Race", ["track"] = "Spa", ["lap"] = 4, ["laps"] = 20, ["timeLeftSec"] = null, ["finished"] = false },
        ["classes"] = new JArray(),
        ["cars"] = new JArray(cars.Select(c => new JObject
        {
            ["num"] = c.Num, ["classId"] = 4098, ["pos"] = c.Pos, ["posClass"] = c.PosClass, ["onPitRoad"] = false, ["connected"] = true,
        })),
    };
}
