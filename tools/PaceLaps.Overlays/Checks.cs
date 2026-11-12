using System.Text.RegularExpressions;
using PaceLaps.Plugin.Core;

namespace PaceLaps.Overlays;

/// <summary>What an overlay must get right before it is written: only the plugin's own properties, items inside it.</summary>
internal static class Checks
{
    private static readonly Regex PropertyCall = new(@"\bp\('([A-Za-z.]+)'\)", RegexOptions.CultureInvariant);

    public static List<string> Problems(Dash dash)
    {
        var known = PluginView.Properties.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var name in PropertiesUsed(dash).Where(n => !known.Contains(n)))
        {
            problems.Add($"uses PaceLapsPlugin.{name}, which the plugin does not have");
        }

        foreach (var item in dash.Items.Where(i => i.Left < 0 || i.Top < 0))
        {
            problems.Add($"{item.Name} starts outside the overlay");
        }

        foreach (var dup in dash.Items.GroupBy(i => i.Name).Where(g => g.Count() > 1))
        {
            problems.Add($"two items are called {dup.Key}");
        }

        return problems;
    }

    /// <summary>Property names read by the formulas, including the helpers' "Banner."/"Notice." + field reads.</summary>
    public static IEnumerable<string> PropertiesUsed(Dash dash)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var js in dash.Formulas.Append(Js.Helpers))
        {
            foreach (Match m in PropertyCall.Matches(js))
            {
                used.Add(m.Groups[1].Value);
            }

            // main('X') and chip('X') read Banner.X and Notice.X.
            foreach (Match m in Regex.Matches(js, @"main\('([A-Za-z]+)'\)"))
            {
                used.Add("Banner." + m.Groups[1].Value);
                used.Add("Notice." + m.Groups[1].Value);
            }

            foreach (Match m in Regex.Matches(js, @"chip\('([A-Za-z]+)'\)"))
            {
                used.Add(m.Groups[1].Value + ".Mine");
            }
        }

        return used;
    }
}
