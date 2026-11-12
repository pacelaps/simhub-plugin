using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PaceLaps.Overlays;

/// <summary>
/// A Dash Studio overlay as SimHub stores it (a .simhubdash is a ZIP of "&lt;title&gt;/&lt;title&gt;.djson", its
/// ".metadata" and "_SHFonts/"). Only what the PaceLaps overlays use: rectangles and texts with JavaScript bindings.
/// The format follows dashes saved by SimHub 9.13.
/// </summary>
internal sealed class Dash(string title, string description, int width, int height)
{
    private const string ItemNamespace = "SimHub.Plugins.OutputPlugins.GraphicalDash.Models.";

    public string Title { get; } = title;
    public List<Item> Items { get; } = [];

    public T Add<T>(T item)
        where T : Item
    {
        Items.Add(item);
        return item;
    }

    /// <summary>Every formula, for checks.</summary>
    public IEnumerable<string> Formulas => Items.SelectMany(i => i.Bindings.Values);

    public JObject Metadata(string simHubVersion) => new()
    {
        ["SettingsBuilder"] = new JObject { ["Settings"] = new JArray(), ["IsEditMode"] = false },
        ["ScreenCount"] = 1.0,
        ["InGameScreensIndexs"] = new JArray(0),
        ["IdleScreensIndexs"] = new JArray(0),
        ["MainPreviewIndex"] = 0,
        ["IsOverlay"] = true,
        ["OverlaySizeWarning"] = false,
        ["MetadataVersion"] = 2.0,
        ["EnableOnDashboardMessaging"] = true,
        ["PitScreensIndexs"] = new JArray(),
        ["PreferredTouchMode"] = 0,
        ["SimHubVersion"] = simHubVersion,
        ["Title"] = Title,
        ["Description"] = description,
        ["Author"] = "PaceLaps",
        ["Width"] = (double)width,
        ["Height"] = (double)height,
        ["DashboardVersion"] = "V1",
    };

    public JObject Djson(string simHubVersion) => new()
    {
        ["Version"] = 2,
        ["Id"] = StableId(Title),
        ["BaseHeight"] = height,
        ["BaseWidth"] = width,
        ["BackgroundColor"] = "#00000000",
        ["Screens"] = new JArray(new JObject
        {
            ["RenderingSkip"] = 0,
            ["Name"] = "Screen",
            ["InGameScreen"] = true,
            ["IdleScreen"] = true,
            ["PitScreen"] = false,
            ["ScreenId"] = StableId(Title + "/screen"),
            ["AllowOverlays"] = true,
            ["IsForegroundLayer"] = false,
            ["IsOverlayLayer"] = false,
            ["OverlayTriggerExpression"] = new JObject { ["Expression"] = "" },
            ["ScreenEnabledExpression"] = new JObject { ["Expression"] = "" },
            ["OverlayMaxDuration"] = 0,
            ["OverlayMinDuration"] = 0,
            ["IsBackgroundLayer"] = false,
            ["BackgroundColor"] = "#00FFFFFF",
            ["Items"] = new JArray(Items.Select(i => i.ToJson(ItemNamespace))),
            ["MinimumRefreshIntervalMS"] = 0.0,
        }),
        ["SnapToGrid"] = false,
        ["HideLabels"] = false,
        ["ShowForeground"] = true,
        ["ForegroundOpacity"] = 100.0,
        ["ShowBackground"] = true,
        ["BackgroundOpacity"] = 100.0,
        ["ShowBoundingRectangles"] = false,
        ["GridSize"] = 10,
        ["Images"] = new JArray(),
        ["Metadata"] = Metadata(simHubVersion),
        ["ShowOnScreenControls"] = true,
        ["IsOverlay"] = true,
        ["EnableClickThroughOverlay"] = true,
        ["EnableOnDashboardMessaging"] = true,
        ["UseStrictJSIsolation"] = false,
        ["UseStrictJSIsolationWarning"] = true,
    };

    /// <summary>Writes "&lt;title&gt;.simhubdash" into <paramref name="folder"/> with the fonts and their licence.</summary>
    public string Write(string folder, IReadOnlyList<string> fontFiles, string simHubVersion)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Title + ".simhubdash");
        File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Text(zip, $"{Title}/{Title}.djson", Djson(simHubVersion).ToString(Formatting.None));
        Text(zip, $"{Title}/{Title}.djson.metadata", Metadata(simHubVersion).ToString(Formatting.Indented));
        foreach (var font in fontFiles)
        {
            var name = Path.GetFileName(font);
            var target = name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ? $"{Title}/_SHFonts/{name}" : $"{Title}/{name}";
            zip.CreateEntryFromFile(font, target, CompressionLevel.Optimal);
        }

        return path;
    }

    private static void Text(ZipArchive zip, string name, string text)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        var bytes = new UTF8Encoding(false).GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>The same id on every build, so importing a new version replaces the old one.</summary>
    private static string StableId(string seed)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("pacelaps-overlay/" + seed));
        return new Guid(hash).ToString();
    }
}

internal abstract class Item(string name, double left, double top, double width, double height)
{
    public string Name { get; } = name;
    public double Left { get; } = left;
    public double Top { get; } = top;
    public double Width { get; } = width;
    public double Height { get; } = height;

    /// <summary>Property name to JavaScript body (with the PaceLaps helpers in front).</summary>
    public Dictionary<string, string> Bindings { get; } = [];

    public Item Bind(string property, string js)
    {
        Bindings[property] = js;
        return this;
    }

    protected abstract string Type { get; }

    protected abstract void Fill(JObject o);

    public JObject ToJson(string ns)
    {
        var o = new JObject { ["$type"] = ns + Type + ", SimHub.Plugins" };
        Fill(o);
        o["Height"] = Height;
        o["Left"] = Left;
        o["Top"] = Top;
        o["Visible"] = true;
        o["BlinkPhasisInverted"] = false;
        o["Width"] = Width;
        o["Name"] = Name;
        o["RenderingSkip"] = 0;
        o["MinimumRefreshIntervalMS"] = 0.0;
        var bindings = new JObject();
        foreach (var (property, js) in Bindings)
        {
            bindings[property] = new JObject
            {
                ["Formula"] = new JObject { ["JSExt"] = 0, ["Interpreter"] = 1, ["Expression"] = Js.Helpers + js },
                ["Mode"] = 2,
            };
        }

        o["Bindings"] = bindings;
        return o;
    }
}

internal sealed class Rect(string name, double left, double top, double width, double height) : Item(name, left, top, width, height)
{
    public string Background { get; init; } = "#00FFFFFF";
    public string BorderColor { get; init; } = "#00FFFFFF";
    public int BorderWidth { get; init; }
    public int RadiusTopLeft { get; init; }
    public int RadiusTopRight { get; init; }
    public int RadiusBottomLeft { get; init; }
    public int RadiusBottomRight { get; init; }
    public double SkewX { get; init; }

    public int Radius
    {
        init => RadiusTopLeft = RadiusTopRight = RadiusBottomLeft = RadiusBottomRight = value;
    }

    protected override string Type => "RectangleItem";

    protected override void Fill(JObject o)
    {
        o["IsRectangleItem"] = true;
        o["BackgroundColor"] = Background;
        o["BorderStyle"] = new JObject
        {
            ["BorderColor"] = BorderColor,
            ["BorderTop"] = BorderWidth,
            ["BorderBottom"] = BorderWidth,
            ["BorderLeft"] = BorderWidth,
            ["BorderRight"] = BorderWidth,
            ["RadiusTopLeft"] = RadiusTopLeft,
            ["RadiusTopRight"] = RadiusTopRight,
            ["RadiusBottomLeft"] = RadiusBottomLeft,
            ["RadiusBottomRight"] = RadiusBottomRight,
        };
        if (SkewX != 0)
        {
            o["SkewAngleX"] = SkewX;
        }
    }
}

internal sealed class Label(string name, double left, double top, double width, double height) : Item(name, left, top, width, height)
{
    public string Weight { get; init; } = "Bold";
    public bool Italic { get; init; }
    public double Size { get; init; } = 14;
    public string Color { get; init; } = "#FFFFFFFF";

    /// <summary>0 left, 1 centre, 2 right.</summary>
    public int Align { get; init; }

    public bool Wrap { get; init; }

    protected override string Type => "TextItem";

    protected override void Fill(JObject o)
    {
        o["IsTextItem"] = true;
        o["Font"] = "Inter";
        o["FontWeight"] = Weight;
        if (Italic)
        {
            o["FontStyle"] = "Italic";
        }

        o["FontSize"] = Size;
        // WPF TextWrapping: 1 NoWrap, 2 Wrap.
        o["TextWrapping"] = Wrap ? 2 : 1;
        o["Text"] = "";
        o["TextColor"] = Color;
        o["HorizontalAlignment"] = Align;
        // Vertically centred.
        o["VerticalAlignment"] = 1;
        o["BackgroundColor"] = "#00FFFFFF";
    }
}
