using PaceLaps.Plugin.Core;

namespace PaceLaps.Plugin.Core.Tests;

/// <summary>The same cases as the web views' clock tests: both must agree on the server's time.</summary>
public sealed class ClockSyncTests
{
    [Fact]
    public void The_offset_assumes_the_server_read_its_clock_half_a_round_trip_after_sending()
    {
        var s = ClockSync.SampleFrom(1000, 1040, 5020);
        Assert.Equal(4000, s.OffsetMs);
        Assert.Equal(40, s.RttMs);
    }

    [Fact]
    public void A_device_clock_that_is_ahead_gives_a_negative_offset() =>
        Assert.Equal(-500, ClockSync.SampleFrom(10_000, 10_020, 9_510).OffsetMs);

    [Fact]
    public void The_server_receive_time_takes_its_processing_out_of_the_estimate()
    {
        var s = ClockSync.SampleFrom(1000, 1170, 5160, 5010);
        Assert.Equal(4000, s.OffsetMs);
        Assert.Equal(20, s.RttMs);

        var withoutReceive = ClockSync.SampleFrom(1000, 1170, 5160);
        Assert.Equal(4075, withoutReceive.OffsetMs);
        Assert.Equal(170, withoutReceive.RttMs);
    }

    [Fact]
    public void The_sample_with_the_shortest_round_trip_wins()
    {
        var best = ClockSync.BestOf([new ClockSample(30, 80), new ClockSample(12, 15), new ClockSample(25, 40)]);
        Assert.Equal(12, best!.Value.OffsetMs);
        Assert.Null(ClockSync.BestOf([]));
    }

    [Fact]
    public void Only_the_most_recent_samples_are_kept()
    {
        var time = new FakeTime();
        var clock = new ClockSync(time, time, (_, _) => throw new InvalidOperationException());
        clock.Add(new ClockSample(99, 1)); // the best, but the oldest
        for (var i = 0; i < ClockSync.Keep; i++)
        {
            clock.Add(new ClockSample(i, 10));
        }

        Assert.Equal(0, clock.Best!.Value.OffsetMs);
    }

    [Fact]
    public async Task A_device_clock_ahead_is_corrected_back_to_server_time()
    {
        var time = new FakeTime();
        var api = new FakeApi(time) { ServerAheadMs = -5_000 };
        var clock = new ClockSync(time, time, api.Clock);

        await clock.Burst(1, 0, CancellationToken.None);

        Assert.Equal(-5_000, clock.OffsetMs);
        Assert.Equal(api.ServerNowMs, clock.ServerNowMs);
    }

    [Fact]
    public async Task A_failed_ping_gives_no_sample()
    {
        var time = new FakeTime();
        var clock = new ClockSync(time, time, (_, _) => Task.FromException<ClockPong>(new DeviceApiException("down")));

        Assert.False(await clock.Ping(CancellationToken.None));
        Assert.Null(clock.Best);
        Assert.Equal(time.NowMs, clock.ServerNowMs);
    }
}
