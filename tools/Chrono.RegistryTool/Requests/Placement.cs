using System.Globalization;
using System.Text.RegularExpressions;

namespace Chrono.RegistryTool.Requests;

public enum PlacementKind
{
    After,
    Before,
    First,
    Last
}

public sealed record Placement(string OrderId, PlacementKind Kind, string? Anchor)
{
    public static IReadOnlyList<Placement> ParseAll(string text)
    {
        var placements = new List<Placement>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cleaned = line.Trim('`', '-', '*', ' ');
            if (cleaned.Length == 0)
            {
                continue;
            }

            var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var kind = parts.Length >= 2 ? parts[1].ToLowerInvariant() : string.Empty;
            placements.Add((kind, parts.Length) switch
            {
                ("after", 3) => new Placement(parts[0], PlacementKind.After, parts[2]),
                ("before", 3) => new Placement(parts[0], PlacementKind.Before, parts[2]),
                ("first", 2) => new Placement(parts[0], PlacementKind.First, null),
                ("last", 2) => new Placement(parts[0], PlacementKind.Last, null),
                _ => throw new RequestException($"Cannot read the placement \"{line}\". Use \"<order-id> after <entry-id>\", \"<order-id> before <entry-id>\", \"<order-id> first\" or \"<order-id> last\".")
            });
        }

        return placements;
    }
}

public sealed partial record TitleReference(bool IsMovie, int Tmdb, IReadOnlyList<int>? Seasons)
{
    public static TitleReference Parse(string text)
    {
        var match = ReferenceRegex().Match(text.Trim().Trim('`'));
        if (!match.Success)
        {
            throw new RequestException($"Cannot read \"{text}\". Use movie:<tmdb id>, tv:<tmdb id> (all seasons), tv:<tmdb id>:s2 or tv:<tmdb id>:s1-3.");
        }

        var tmdb = int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture);
        if (match.Groups["kind"].Value == "movie")
        {
            if (match.Groups["seasons"].Success)
            {
                throw new RequestException($"\"{text}\": movies have no seasons.");
            }

            return new TitleReference(true, tmdb, null);
        }

        return new TitleReference(false, tmdb, match.Groups["seasons"].Success ? ParseSeasons(match.Groups["seasons"].Value) : null);
    }

    public static IReadOnlyList<int>? ParseSeasons(string text)
    {
        var value = text.Trim();
        if (value.Length == 0 || value.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var seasons = new List<int>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.TrimStart('s', 'S').Split('-', StringSplitOptions.TrimEntries);
            if (!int.TryParse(range[0].TrimStart('s', 'S'), CultureInfo.InvariantCulture, out var from)
                || (range.Length == 2 && !int.TryParse(range[1].TrimStart('s', 'S'), CultureInfo.InvariantCulture, out _))
                || range.Length > 2)
            {
                throw new RequestException($"Cannot read the seasons \"{text}\". Use e.g. 1, 1-3 or 1,3.");
            }

            var to = range.Length == 2 ? int.Parse(range[1].TrimStart('s', 'S'), CultureInfo.InvariantCulture) : from;
            for (var season = from; season <= to; season++)
            {
                if (!seasons.Contains(season))
                {
                    seasons.Add(season);
                }
            }
        }

        return seasons;
    }

    [GeneratedRegex(@"^(?<kind>movie|tv):(?<id>\d+)(?::(?<seasons>s?\d+(?:-s?\d+)?(?:,s?\d+(?:-s?\d+)?)*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ReferenceRegex();
}
