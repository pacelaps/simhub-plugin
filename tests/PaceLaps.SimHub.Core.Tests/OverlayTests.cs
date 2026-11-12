using System.IO.Compression;
using Newtonsoft.Json.Linq;
using PaceLaps.Overlays;
using PaceLaps.Plugin.Core;

namespace PaceLaps.Plugin.Core.Tests;

/// <summary>The three overlays read only the plugin's properties and package as SimHub expects.</summary>
public sealed class OverlayTests
{
    public static TheoryData<string> Titles() => new(Designs.All().Select(d => d.Title));

    private static Dash Design(string title) => Designs.All().Single(d => d.Title == title);

    [Theory]
    [MemberData(nameof(Titles))]
    public void Each_overlay_reads_only_the_plugins_properties(string title)
    {
        var dash = Design(title);

        Assert.Empty(Checks.Problems(dash));
        Assert.Contains("Banner.Text", Checks.PropertiesUsed(dash));
        Assert.Contains("Notice.Text", Checks.PropertiesUsed(dash));
        Assert.Contains("Offline", Checks.PropertiesUsed(dash));
    }

    [Fact]
    public void A_misspelled_property_is_caught()
    {
        var dash = new Dash("Broken", "", 100, 100);
        dash.Add(new Label("T", 0, 0, 100, 100)).Bind("Text", "return p('Banner.Txt');");

        Assert.Contains(Checks.Problems(dash), p => p.Contains("Banner.Txt", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Titles))]
    public void Each_overlay_is_a_transparent_overlay_with_javascript_bindings(string title)
    {
        var djson = Design(title).Djson("9.13.3");

        Assert.True((bool)djson["IsOverlay"]!);
        Assert.Equal("#00000000", (string?)djson["BackgroundColor"]);
        var items = (JArray)djson["Screens"]![0]!["Items"]!;
        Assert.All(items, i => Assert.StartsWith("SimHub.Plugins.OutputPlugins.GraphicalDash.Models.", (string?)i["$type"], StringComparison.Ordinal));
        Assert.All(items.SelectMany(i => ((JObject)i["Bindings"]!).Properties()), b =>
        {
            Assert.Equal(1, (int)b.Value["Formula"]!["Interpreter"]!);
            Assert.Contains("return ", (string?)b.Value["Formula"]!["Expression"], StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_same_design_keeps_its_id_so_a_new_version_replaces_the_old()
    {
        Assert.Equal((string?)Designs.Card().Djson("9.13.3")["Id"], (string?)Designs.Card().Djson("9.13.3")["Id"]);
        Assert.NotEqual((string?)Designs.Card().Djson("9.13.3")["Id"], (string?)Designs.Solid().Djson("9.13.3")["Id"]);
    }

    [Fact]
    public void The_package_holds_the_dash_its_metadata_and_the_fonts_with_their_licence()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pacelaps-overlay-test-" + Guid.NewGuid().ToString("N"));
        var font = Path.Combine(folder, "in", "Inter-Bold.ttf");
        var licence = Path.Combine(folder, "in", "Inter-OFL.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(font)!);
        File.WriteAllBytes(font, [0, 1, 0, 0]);
        File.WriteAllText(licence, "OFL");
        try
        {
            var path = Designs.Card().Write(Path.Combine(folder, "out"), [font, licence], "9.13.3");

            using var zip = ZipFile.OpenRead(path);
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("PaceLaps Card/PaceLaps Card.djson", names);
            Assert.Contains("PaceLaps Card/PaceLaps Card.djson.metadata", names);
            Assert.Contains("PaceLaps Card/_SHFonts/Inter-Bold.ttf", names);
            Assert.Contains("PaceLaps Card/Inter-OFL.txt", names);
            using var reader = new StreamReader(zip.GetEntry("PaceLaps Card/PaceLaps Card.djson")!.Open());
            Assert.Equal("PaceLaps Card", (string?)JObject.Parse(reader.ReadToEnd())["Metadata"]!["Title"]);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Every_property_the_overlays_use_exists() =>
        Assert.Subset(PluginView.Properties.Select(p => p.Name).ToHashSet(), Designs.All().SelectMany(Checks.PropertiesUsed).ToHashSet());
}
