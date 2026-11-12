using PaceLaps.SimHub.Core;

namespace PaceLaps.SimHub.Core.Tests;

public sealed class DisplayTests
{
    private const long T = 1_800_000_000_000;

    private static RaceMessage M(string id, int seq, bool sticky, long showAt = T, string[]? supersedes = null, string type = "flag") =>
        RaceMessage.From(Snapshots.Message(id, seq, showAt, type, sticky, supersedes: supersedes))!;

    [Fact]
    public void A_state_becomes_the_main_banner_and_a_one_off_shows_over_it_for_its_time()
    {
        var display = CallDisplay.Empty
            .Show(M("sc", 1, sticky: true), T, 8)
            .Show(M("blue", 2, sticky: false), T + 1_000, 8);

        Assert.Equal("blue", display.Current!.Message.Id);
        Assert.Equal(T + 9_000, display.Current.UntilMs);
        Assert.Same(display, display.Expire(T + 9_000 - 1));

        var later = display.Expire(T + 9_000);
        Assert.Equal("sc", later.Current!.Message.Id);
        Assert.Equal(["blue", "sc"], later.History.Select(m => m.Id));
    }

    [Fact]
    public void A_message_that_supersedes_takes_the_old_banner_down()
    {
        var display = CallDisplay.Empty
            .Show(M("sc", 1, sticky: true), T, 8)
            .Show(M("green", 2, sticky: false, supersedes: ["sc"]), T + 5_000, 8);

        Assert.Empty(display.Banners);
        Assert.Equal("green", display.Current!.Message.Id);
    }

    [Fact]
    public void The_same_message_twice_is_in_the_history_once()
    {
        var display = CallDisplay.Empty.Show(M("a", 1, true), T, 8).Show(M("a", 1, true), T, 8);

        Assert.Single(display.Banners);
        Assert.Single(display.History);
    }

    [Fact]
    public void The_history_keeps_the_newest_calls()
    {
        var display = CallDisplay.Empty;
        for (var i = 0; i < CallDisplay.HistoryLimit + 5; i++)
        {
            display = display.Show(M("m" + i, i, false), T + i, 8);
        }

        Assert.Equal(CallDisplay.HistoryLimit, display.History.Count);
        Assert.Equal("m" + (CallDisplay.HistoryLimit + 4), display.History[0].Id);
    }

    [Fact]
    public void A_snapshot_restores_banners_and_history_and_returns_what_is_still_to_come()
    {
        var snapshot = StateSnapshot.FromJson(Snapshots.State(T, banners: [Snapshots.Message("sc", 3, T - 60_000, "safety_car", true)],
            recent: [Snapshots.Message("old", 2, T - 10_000), Snapshots.Message("soon", 4, T + 1_500)]))!;

        var display = CallDisplay.FromSnapshot(snapshot, T, out var due);

        Assert.Equal("sc", display.Current!.Message.Id);
        Assert.Equal(["old"], display.History.Select(m => m.Id));
        Assert.Equal(["soon"], due.Select(m => m.Id));
    }

    [Theory]
    [InlineData(null, "c_5_3", "c_5_3")]
    [InlineData("c_5_3", null, "c_5_3")]
    [InlineData("c_5_3", "c_5_10", "c_5_10")]
    [InlineData("c_5_10", "c_5_3", "c_5_10")]
    [InlineData("c_5_10", "c_6_1", "c_6_1")]
    public void The_later_cursor_wins_within_a_session(string? a, string? b, string expected) =>
        Assert.Equal(expected, Cursors.Later(a, b));

    [Fact]
    public void The_delay_counts_on_the_server_clock() =>
        Assert.Equal(500, Schedule.DelayUntil(showAtMs: T + 2_500, deviceNowMs: T, offsetMs: 2_000));
}
