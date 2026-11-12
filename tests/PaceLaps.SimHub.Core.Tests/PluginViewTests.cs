using PaceLaps.Plugin.Core;

namespace PaceLaps.Plugin.Core.Tests;

public sealed class PluginViewTests
{
    private const long T = 1_800_000_000_000;

    private static RaceMessage M(string id, string type, bool sticky, string[]? aboutCars = null) =>
        RaceMessage.From(Snapshots.Message(id, 1, T, type, sticky, text: id.ToUpperInvariant(), aboutCars: aboutCars))!;

    private static DriverState State(ConnectionStatus status = ConnectionStatus.Connected, CallDisplay? display = null, RaceState? race = null) =>
        new()
        {
            Status = status,
            Device = new DeviceInfo(7, "12", 4098, false),
            Display = display ?? CallDisplay.Empty,
            Race = race,
        };

    private static RaceState Race(string flag, long at = T, double? timeLeft = null) => RaceState.FromJson($$"""
        {"v":1,"kind":"race_state","at":"{{Iso.FromMs(at)}}","flag":{"state":"{{flag}}","label":"L","detail":""},
         "session":{"type":"Race","track":"Spa","lap":4,"laps":20,"timeLeftSec":{{(timeLeft == null ? "null" : timeLeft.ToString())}},"finished":false},
         "classes":[],"cars":[{"num":"12","classId":4098,"pos":3,"posClass":2,"onPitRoad":true,"connected":true}]}
        """)!;

    [Fact]
    public void Nothing_set_up_shows_nothing()
    {
        var v = PluginView.NotSetUp;

        Assert.Equal("not_set_up", v.Status);
        Assert.False(v.CallActive);
        Assert.Equal("", v.CallText);
        Assert.Equal("", v.LedColour);
    }

    [Fact]
    public void A_safety_car_call_is_yellow_with_dark_text_and_new_for_five_seconds()
    {
        var display = CallDisplay.Empty.Show(M("sc", "safety_car", true), T, 8);

        var v = PluginView.From(State(display: display), T + 4_000);

        Assert.True(v.CallActive);
        Assert.Equal("SC", v.CallText);
        Assert.Equal("safety_car", v.CallKind);
        Assert.Equal(Palette.Yellow, v.CallColour);
        Assert.Equal(Palette.Dark, v.CallTextColour);
        Assert.Equal(4, v.CallAgeSeconds);
        Assert.True(v.CallNew);
        Assert.Equal(Palette.Yellow, v.LedColour);
        Assert.True(v.LedFlash);
        Assert.False(PluginView.From(State(display: display), T + 5_000).LedFlash);
    }

    [Fact]
    public void This_cars_penalty_wins_the_leds_over_a_newer_call()
    {
        var display = CallDisplay.Empty
            .Show(M("dt", "penalty", true, ["12"]), T, 8)
            .Show(M("blue", "wave_around", false), T + 10_000, 8);

        var v = PluginView.From(State(display: display), T + 11_000);

        Assert.Equal("BLUE", v.CallText);
        Assert.Equal(Palette.Blue, v.CallColour);
        Assert.True(v.PenaltyActive);
        Assert.Equal("DT", v.PenaltyText);
        Assert.Equal("DT", v.PersonalText);
        Assert.Equal(Palette.Red, v.LedColour);
        Assert.False(v.LedFlash);
    }

    [Fact]
    public void A_flag_call_takes_the_races_flag_colour()
    {
        var display = CallDisplay.Empty.Show(M("green", "flag", false), T, 8);

        Assert.Equal(Palette.Green, PluginView.From(State(display: display, race: Race("green")), T).CallColour);
        Assert.Equal(Palette.White, PluginView.From(State(display: display), T).CallColour);
    }

    [Fact]
    public void Without_a_call_the_leds_show_a_yellow_or_red_flag_only()
    {
        Assert.Equal(Palette.Yellow, PluginView.From(State(race: Race("yellow")), T).LedColour);
        Assert.Equal(Palette.Red, PluginView.From(State(race: Race("red")), T).LedColour);
        Assert.Equal("", PluginView.From(State(race: Race("green")), T).LedColour);
    }

    [Fact]
    public void Offline_the_leds_go_dark_and_the_overlay_can_warn()
    {
        var display = CallDisplay.Empty.Show(M("sc", "safety_car", true), T, 8);

        var v = PluginView.From(State(ConnectionStatus.Offline, display, Race("yellow")), T);

        Assert.True(v.Offline);
        Assert.Equal("offline", v.Status);
        Assert.True(v.CallActive);
        Assert.Equal("", v.LedColour);
    }

    [Fact]
    public void The_race_gives_this_cars_place_and_the_time_left_counts_down()
    {
        var v = PluginView.From(State(race: Race("green", T, 600)), T + 30_000);

        Assert.Equal(3, v.Position);
        Assert.Equal(2, v.ClassPosition);
        Assert.True(v.OnPitRoad);
        Assert.Equal(4, v.Lap);
        Assert.Equal(20, v.Laps);
        Assert.Equal(570, v.TimeLeftSeconds);
    }

    [Fact]
    public void A_test_penalty_looks_like_this_cars_own()
    {
        var v = PluginView.From(DriverState.Preview("penalty", "12", T), T + 1_000);

        Assert.Equal("DRIVE-THROUGH #12: TEST", v.CallText);
        Assert.True(v.CallMine);
        Assert.True(v.PenaltyActive);
        Assert.Equal(Palette.Red, v.LedColour);
        Assert.True(v.LedFlash);
    }

    [Theory]
    [InlineData("safety_car", "#FACC15")]
    [InlineData("wave_around", "#155DFC")]
    [InlineData("info", "#71717A")]
    public void Each_test_call_has_its_colour(string kind, string colour) =>
        Assert.Equal(colour, PluginView.From(DriverState.Preview(kind, null, T), T).CallColour);

    [Theory]
    [InlineData("wave", "info", 3)]
    [InlineData(null, "penalty", 2)]
    [InlineData("unknown", "unknown", 1)]
    public void Each_call_has_its_tone(string? sound, string type, int steps) => Assert.Equal(steps, Tones.For(sound, type).Count);
}
