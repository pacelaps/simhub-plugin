using System.Text.Json;
using Json.Schema;
using PaceLaps.Plugin.Core;

namespace PaceLaps.Plugin.Core.Tests;

/// <summary>The plugin reads the shared examples and sends what the shared schemas accept.</summary>
public sealed class ContractTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "contracts");

    private static readonly Lazy<Dictionary<string, JsonSchema>> Schemas = new(() =>
    {
        var options = new BuildOptions { SchemaRegistry = new SchemaRegistry() };
        return Directory.GetFiles(Folder, "*.schema.json")
            .OrderBy(f => Path.GetFileName(f) == "common.schema.json" ? 0 : 1)
            .ToDictionary(f => Path.GetFileName(f), f => JsonSchema.FromText(File.ReadAllText(f), options), StringComparer.Ordinal);
    });

    private static string Example(string name) => File.ReadAllText(Path.Combine(Folder, "examples", name));

    private static void AssertValid(string schemaFile, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = Schemas.Value[schemaFile].Evaluate(doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var errors = (result.Details ?? []).Where(d => d.Errors is { Count: > 0 })
            .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}"));
        Assert.True(result.IsValid, $"{schemaFile} rejected:\n{string.Join("\n", errors)}\n{json}");
    }

    public static TheoryData<string> MessageExamples() =>
        new(Directory.GetFiles(Path.Combine(Folder, "examples"), "message.*.json").Select(Path.GetFileName)!);

    [Theory]
    [MemberData(nameof(MessageExamples))]
    public void Every_message_example_is_read(string file)
    {
        var message = RaceMessage.FromJson(Example(file));

        Assert.NotNull(message);
        Assert.False(string.IsNullOrEmpty(message.Text));
        Assert.True(message.ShowAtMs > 0);
    }

    [Fact]
    public void A_penalty_example_names_its_car_and_supersedes_nothing()
    {
        var penalty = RaceMessage.FromJson(Example("message.penalty.json"))!;

        Assert.Equal("penalty", penalty.Type);
        Assert.True(penalty.Sticky);
        Assert.True(penalty.IsAbout("12"));
        Assert.False(penalty.IsAbout("7"));
        Assert.Equal("Drive-through penalty: serve it within 3 laps by driving through the pit lane at the speed limit.", penalty.Detail);
        Assert.Equal(Iso.ToMs("2026-11-07T19:08:30.000Z"), penalty.ShowAtMs);
    }

    [Fact]
    public void A_call_with_a_league_colour_is_read() =>
        Assert.Equal("turquoise", RaceMessage.FromJson(Example("message.colour.json"))!.Colour);

    [Fact]
    public void The_state_snapshot_example_is_read()
    {
        var snapshot = StateSnapshot.FromJson(Example("state_snapshot.json"))!;

        Assert.Equal("c_5521", snapshot.Cursor);
        Assert.Equal("green", snapshot.Phase);
        Assert.Single(snapshot.RecentMessages);
        Assert.Equal([4098], snapshot.RecentMessages[0].Affects.Classes);
        Assert.Null(snapshot.Target);
        Assert.False(snapshot.HasRace);
    }

    [Fact]
    public void The_race_state_example_is_read()
    {
        var race = RaceState.FromJson(Example("race_state.json"))!;

        Assert.Contains(race.FlagState, new[] { "green", "green_held", "yellow", "red", "white", "grey" });
        Assert.NotEmpty(race.Cars);
        Assert.All(race.Cars, c => Assert.True(c.Pos > 0));
    }

    [Fact]
    public void The_shown_report_is_valid()
    {
        var report = new ShownReport("msg_77", Iso.ToMs("2026-11-07T19:03:13.502Z"), Iso.ToMs("2026-11-07T19:03:12.081Z"), -42.46, 18.21, false);

        var json = report.ToJson().ToString();

        AssertValid("device-messages.schema.json", json);
        Assert.Contains("\"shownAt\": \"2026-11-07T19:03:13.502Z\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_clock_ping_the_api_sends_is_valid() =>
        AssertValid("device-messages.schema.json",
            new Newtonsoft.Json.Linq.JObject { ["v"] = 1, ["kind"] = "clock_ping", ["clientSentAt"] = Iso.FromMs(1_800_000_000_123) }.ToString());

    [Fact]
    public void The_test_messages_follow_the_message_schema() =>
        AssertValid("message.schema.json", Snapshots.Message("msg_1", 1, 1_800_000_000_000, "penalty", true, aboutCars: ["12"]).ToString());

    /// <summary>The copy in contracts/ is the web service's (scripts/sync-contracts.ps1); skipped without the workspace.</summary>
    [Fact]
    public void The_contract_copy_matches_the_web_service()
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var source = Path.GetFullPath(Path.Combine(repo, "..", "webservice", "contracts"));
        Assert.SkipUnless(Directory.Exists(source), "the webservice repository is not next to this one");

        var copy = Path.Combine(repo, "contracts");
        foreach (var file in Directory.GetFiles(source, "*.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var copied = Path.Combine(copy, relative);
            Assert.True(File.Exists(copied), $"{relative} is missing from contracts/: run scripts/sync-contracts.ps1");
            Assert.True(File.ReadAllText(file) == File.ReadAllText(copied), $"{relative} differs from the web service's: run scripts/sync-contracts.ps1");
        }
    }
}
