namespace PaceLaps.Overlays;

/// <summary>
/// The JavaScript every formula starts with. The "main" call is the lasting state (Banner) if one is up, else the
/// one-off (Notice); a second line shows the one-off beside a state. Only the plugin's properties are read.
/// </summary>
internal static class Js
{
    public const string Helpers =
        "function p(n) { return $prop('PaceLapsPlugin.' + n); }\n"
        + "function on() { return p('Banner.Active') || p('Notice.Active'); }\n"
        + "function main(f) { return p('Banner.Active') ? p('Banner.' + f) : p('Notice.' + f); }\n"
        + "function both() { return p('Banner.Active') && p('Notice.Active'); }\n"
        + "function upper(t) { return (t || '').toUpperCase(); }\n"
        + "function fit(t, big) { var n = (t || '').length; return n <= 14 ? big : n <= 22 ? big * 0.82 : n <= 32 ? big * 0.66 : big * 0.54; }\n"
        + "function short(t, max) { t = t || ''; return t.length <= max ? t : t.substring(0, max - 1) + '…'; }\n"
        + "function chip(prefix) { return p(prefix + '.Mine') ? 'YOU' : (prefix == 'Notice' && p('Notice.MyClass')) ? 'YOUR CLASS' : ''; }\n"
        + "function mainChip() { return p('Banner.Active') ? chip('Banner') : chip('Notice'); }\n"
        + "function lap() { var l = p('Race.Lap'), n = p('Race.Laps'); return n > 0 ? 'LAP ' + l + ' / ' + n : l > 0 ? 'LAP ' + l : ''; }\n";

    public const string MainVisible = "return on();";
    public const string NoticeVisible = "return both();";
    public const string OfflineVisible = "return p('Offline');";
}

/// <summary>The three PaceLaps overlays. Colours and type follow the web service's tokens (dark theme) and Inter.</summary>
internal static class Designs
{
    private const string Glass = "#E60C0C0F";
    private const string Hairline = "#1FFFFFFF";
    private const string White = "#FFFFFFFF";
    private const string Muted = "#FFD4D4D8";
    private const string Subtle = "#FFA1A1AA";
    private const string Faint = "#FF71717A";
    private const string Amber = "#FFFBBF24";
    private const string Brand = "#FF155DFC";

    public static IReadOnlyList<Dash> All() => [Card(), Solid(), Strip()];

    /// <summary>A, the default: dark glass, a colour bar, a small race control header.</summary>
    public static Dash Card()
    {
        var d = new Dash("PaceLaps Card",
            "PaceLaps race control: the current call on dark glass with a colour bar. Needs the PaceLaps plugin.", 460, 200);

        d.Add(new Rect("Card", 0, 0, 460, 112) { Background = Glass, BorderColor = Hairline, BorderWidth = 1, Radius = 10 })
            .Bind("Visible", Js.MainVisible);
        d.Add(new Rect("Mark", 18, 12, 9, 9) { Background = Brand, SkewX = -20 }).Bind("Visible", Js.MainVisible);
        d.Add(new Label("Header", 34, 6, 220, 22) { Size = 11, Color = Subtle })
            .Bind("Visible", Js.MainVisible).Bind("Text", "return 'RACE CONTROL';");
        d.Add(new Label("Lap", 256, 6, 188, 22) { Size = 11, Color = Faint, Align = 2 })
            .Bind("Visible", Js.MainVisible).Bind("Text", "return lap();");
        d.Add(new Rect("Bar", 16, 34, 6, 62) { Radius = 3 })
            .Bind("Visible", Js.MainVisible).Bind("BackgroundColor", "return main('Color');");
        d.Add(new Label("Text", 34, 30, 326, 48) { Weight = "ExtraBold", Italic = true, Size = 32, Wrap = true })
            .Bind("Visible", Js.MainVisible)
            .Bind("Text", "return upper(main('Text'));")
            .Bind("FontSize", "return fit(main('Text'), 32);")
            .Bind("Top", "return main('Detail') ? 30 : 40;");
        d.Add(new Label("Chip", 360, 30, 86, 30) { Size = 11, Color = Muted, Align = 2 })
            .Bind("Visible", Js.MainVisible).Bind("Text", "return mainChip();");
        d.Add(new Label("Detail", 34, 74, 412, 32) { Weight = "Medium", Size = 13, Color = Muted, Wrap = true })
            .Bind("Visible", Js.MainVisible).Bind("Text", "return short(main('Detail'), 110);");

        d.Add(new Rect("NoticeCard", 0, 120, 460, 44) { Background = Glass, BorderColor = Hairline, BorderWidth = 1, Radius = 10 })
            .Bind("Visible", Js.NoticeVisible);
        d.Add(new Rect("NoticeBar", 16, 131, 6, 22) { Radius = 3 })
            .Bind("Visible", Js.NoticeVisible).Bind("BackgroundColor", "return p('Notice.Color');");
        d.Add(new Label("NoticeText", 34, 120, 316, 44) { Size = 16 })
            .Bind("Visible", Js.NoticeVisible)
            .Bind("Text", "return upper(p('Notice.Text'));")
            .Bind("FontSize", "return fit(p('Notice.Text'), 16);");
        d.Add(new Label("NoticeChip", 350, 120, 96, 44) { Size = 11, Color = Muted, Align = 2 })
            .Bind("Visible", Js.NoticeVisible).Bind("Text", "return chip('Notice');");

        Offline(d, 0, 172);
        return d;
    }

    /// <summary>B: the driver page's banners, filled in the call's colour.</summary>
    public static Dash Solid()
    {
        var d = new Dash("PaceLaps Solid",
            "PaceLaps race control: the current call as a banner in its colour, like the driver page. Needs the PaceLaps plugin.", 460, 200);

        d.Add(new Rect("Banner", 0, 0, 460, 112) { Radius = 10 })
            .Bind("Visible", Js.MainVisible).Bind("BackgroundColor", "return main('Color');");
        d.Add(new Rect("Ring", 0, 0, 460, 112) { BorderColor = White, BorderWidth = 3, Radius = 10 })
            .Bind("Visible", "return on() && mainChip() != '';");
        d.Add(new Label("Text", 20, 14, 336, 56) { Weight = "ExtraBold", Italic = true, Size = 34, Wrap = true })
            .Bind("Visible", Js.MainVisible)
            .Bind("Text", "return upper(main('Text'));")
            .Bind("TextColor", "return main('TextColor');")
            .Bind("FontSize", "return fit(main('Text'), 34);")
            .Bind("Top", "return main('Detail') ? 14 : 28;");
        d.Add(new Label("Chip", 356, 14, 88, 30) { Size = 12, Align = 2 })
            .Bind("Visible", Js.MainVisible).Bind("Text", "return mainChip();").Bind("TextColor", "return main('TextColor');");
        d.Add(new Label("Detail", 20, 68, 420, 38) { Weight = "Medium", Size = 13, Wrap = true })
            .Bind("Visible", Js.MainVisible)
            .Bind("Text", "return short(main('Detail'), 110);")
            .Bind("TextColor", "return main('TextColor');");

        d.Add(new Rect("Notice", 0, 120, 460, 44) { Radius = 10 })
            .Bind("Visible", Js.NoticeVisible).Bind("BackgroundColor", "return p('Notice.Color');");
        d.Add(new Rect("NoticeRing", 0, 120, 460, 44) { BorderColor = White, BorderWidth = 3, Radius = 10 })
            .Bind("Visible", "return both() && chip('Notice') != '';");
        d.Add(new Label("NoticeText", 20, 120, 330, 44) { Size = 16 })
            .Bind("Visible", Js.NoticeVisible)
            .Bind("Text", "return upper(p('Notice.Text'));")
            .Bind("TextColor", "return p('Notice.TextColor');")
            .Bind("FontSize", "return fit(p('Notice.Text'), 16);");
        d.Add(new Label("NoticeChip", 350, 120, 94, 44) { Size = 11, Align = 2 })
            .Bind("Visible", Js.NoticeVisible).Bind("Text", "return chip('Notice');").Bind("TextColor", "return p('Notice.TextColor');");

        Offline(d, 0, 172);
        return d;
    }

    /// <summary>C: one slim line across the top of the screen.</summary>
    public static Dash Strip()
    {
        var d = new Dash("PaceLaps Strip",
            "PaceLaps race control: the current call in one slim line for the top of the screen. Needs the PaceLaps plugin.", 960, 84);

        d.Add(new Rect("Strip", 0, 0, 960, 56) { Background = Glass, BorderColor = Hairline, BorderWidth = 1, Radius = 8 })
            .Bind("Visible", Js.MainVisible);
        d.Add(new Rect("Block", 0, 0, 300, 56) { RadiusTopLeft = 8, RadiusBottomLeft = 8 })
            .Bind("Visible", Js.MainVisible).Bind("BackgroundColor", "return main('Color');");
        d.Add(new Label("Text", 14, 0, 278, 56) { Weight = "ExtraBold", Italic = true, Size = 22, Wrap = true })
            .Bind("Visible", Js.MainVisible)
            .Bind("Text", "return upper(main('Text'));")
            .Bind("TextColor", "return main('TextColor');")
            .Bind("FontSize", "return fit(main('Text'), 22);");
        d.Add(new Label("Detail", 314, 0, 372, 56) { Weight = "Medium", Size = 13, Color = Muted, Wrap = true })
            .Bind("Visible", Js.MainVisible)
            .Bind("Text", "var c = mainChip(); return (c ? c + ' · ' : '') + short(main('Detail'), 90);");
        d.Add(new Rect("Notice", 700, 0, 260, 56) { RadiusTopRight = 8, RadiusBottomRight = 8 })
            .Bind("Visible", Js.NoticeVisible).Bind("BackgroundColor", "return p('Notice.Color');");
        d.Add(new Label("NoticeText", 712, 0, 236, 56) { Size = 14, Wrap = true })
            .Bind("Visible", Js.NoticeVisible)
            .Bind("Text", "var c = chip('Notice'); return upper(p('Notice.Text')) + (c ? ' · ' + c : '');")
            .Bind("TextColor", "return p('Notice.TextColor');")
            .Bind("FontSize", "return fit(p('Notice.Text'), 14);");

        Offline(d, 0, 60);
        return d;
    }

    /// <summary>A small line while the connection is lost: calls may be missing.</summary>
    private static void Offline(Dash d, double left, double top)
    {
        d.Add(new Rect("OfflineBox", left, top, 300, 24) { Background = Glass, BorderColor = Hairline, BorderWidth = 1, Radius = 6 })
            .Bind("Visible", Js.OfflineVisible);
        d.Add(new Label("OfflineText", left + 10, top, 284, 24) { Size = 11, Color = Amber })
            .Bind("Visible", Js.OfflineVisible).Bind("Text", "return 'PACELAPS OFFLINE · CALLS MAY BE MISSING';");
    }
}
