using PaceLaps.Overlays;

// Builds the PaceLaps Dash Studio overlays into overlay/dist (run from the repository root):
//   dotnet run --project tools/PaceLaps.Overlays [-- <output folder>]
var root = FindRoot();
var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(root, "overlay", "dist");
var fonts = Directory.GetFiles(Path.Combine(root, "overlay", "fonts")).Order().ToList();

foreach (var dash in Designs.All())
{
    var problems = Checks.Problems(dash);
    if (problems.Count > 0)
    {
        Console.Error.WriteLine($"{dash.Title}:\n  " + string.Join("\n  ", problems));
        return 1;
    }

    Console.WriteLine(dash.Write(output, fonts, "9.13.3"));
}

return 0;

static string FindRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "PaceLaps.SimHub.sln")))
        {
            return dir.FullName;
        }
    }

    return Directory.GetCurrentDirectory();
}
