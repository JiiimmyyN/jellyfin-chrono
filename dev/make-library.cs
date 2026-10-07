using System.Diagnostics;
using System.Text.Json;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "media"));
var registry = Path.GetFullPath(args.Length > 1 ? args[1] : "registry/universes");
var ownedPercent = args.Length > 2 ? int.Parse(args[2]) : 70;
const int EpisodesPerSeason = 3;

var sample = Path.Combine(root, ".sample.mkv");
Directory.CreateDirectory(root);
if (!File.Exists(sample))
{
    Run("ffmpeg", $"-loglevel error -f lavfi -i color=c=black:s=320x180:d=2 -f lavfi -i anullsrc=r=22050:cl=mono -t 2 -c:v libx264 -preset ultrafast -c:a aac -shortest \"{sample}\"");
}

var today = DateOnly.FromDateTime(DateTime.UtcNow);
var created = 0;
foreach (var file in Directory.EnumerateFiles(registry, "*.json"))
{
    using var document = JsonDocument.Parse(File.ReadAllText(file));
    foreach (var entry in document.RootElement.GetProperty("entries").EnumerateArray())
    {
        var id = entry.GetProperty("id").GetString()!;
        var released = DateOnly.Parse(entry.GetProperty("released").GetString()!);
        if (released > today || StableBucket(id) >= ownedPercent)
        {
            continue;
        }

        var title = Safe(entry.GetProperty("title").GetString()!);
        var tmdb = entry.GetProperty("tmdb").GetInt32();
        var type = entry.GetProperty("type").GetString();
        if (type == "movie")
        {
            var name = $"{title} ({released.Year}) [tmdbid-{tmdb}]";
            created += Link(Path.Combine(root, "movies", name, name + ".mkv"));
        }
        else
        {
            var season = entry.TryGetProperty("season", out var s) ? s.GetInt32() : 1;
            var showFolder = Path.Combine(root, "shows", $"{title} [tmdbid-{tmdb}]");
            for (var episode = 1; episode <= EpisodesPerSeason; episode++)
            {
                created += Link(Path.Combine(showFolder, $"Season {season:D2}", $"{title} S{season:D2}E{episode:D2}.mkv"));
            }
        }
    }
}

foreach (var (title, year, tmdb) in new[]
{
    ("John Wick", 2014, 245891), ("John Wick - Chapter 2", 2017, 324552), ("John Wick - Chapter 3 - Parabellum", 2019, 458156), ("John Wick - Chapter 4", 2023, 603692),
    ("The Lord of the Rings - The Fellowship of the Ring", 2001, 120), ("The Lord of the Rings - The Two Towers", 2002, 121), ("The Lord of the Rings - The Return of the King", 2003, 122),
    ("The Hobbit - An Unexpected Journey", 2012, 49051)
})
{
    var name = $"{title} ({year}) [tmdbid-{tmdb}]";
    created += Link(Path.Combine(root, "movies", name, name + ".mkv"));
}

Console.WriteLine($"Library at {root}: {created} new files.");

int Link(string path)
{
    if (File.Exists(path))
    {
        return 0;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.Copy(sample, path);
    return 1;
}

static int StableBucket(string id)
{
    var hash = 17;
    foreach (var c in id)
    {
        hash = unchecked(hash * 31 + c);
    }

    return Math.Abs(hash % 100);
}

static string Safe(string name)
{
    name = name.Replace(":", " -");
    foreach (var c in new[] { "/", "\\", "?", "*", "\"", "<", ">", "|" })
    {
        name = name.Replace(c, "");
    }

    return name.Replace("  ", " ").Trim();
}

static void Run(string file, string arguments)
{
    using var process = Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false })!;
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"{file} exited with {process.ExitCode}");
    }
}
