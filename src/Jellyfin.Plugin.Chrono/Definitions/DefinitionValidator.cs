using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Chrono.Definitions;

public static partial class DefinitionValidator
{
    public static IReadOnlyList<string> Validate(UniverseDefinition universe)
    {
        var errors = new List<string>();
        if (!SlugRegex().IsMatch(universe.Id))
        {
            errors.Add($"Universe id '{universe.Id}' is not a slug.");
        }

        if (string.IsNullOrWhiteSpace(universe.Name))
        {
            errors.Add("Universe name is missing.");
        }

        var entryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in universe.Entries)
        {
            if (!entryIds.Add(entry.Id))
            {
                errors.Add($"Duplicate entry id '{entry.Id}'.");
            }

            if (entry.Tmdb <= 0)
            {
                errors.Add($"Entry '{entry.Id}' has no TMDB id.");
            }

            if (entry.Type == EntryType.Season && entry.Season is null)
            {
                errors.Add($"Season entry '{entry.Id}' has no season number.");
            }

            if (entry.Released is not null && entry.ReleaseDate is null)
            {
                errors.Add($"Entry '{entry.Id}' has an invalid release date '{entry.Released}'.");
            }

            foreach (var flag in entry.Flags.Where(f => !universe.Flags.ContainsKey(f)))
            {
                errors.Add($"Entry '{entry.Id}' uses undeclared flag '{flag}'.");
            }
        }

        var groupIds = universe.Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var group in universe.Groups.Where(g => g.Parent is not null && !groupIds.Contains(g.Parent)))
        {
            errors.Add($"Group '{group.Id}' has unknown parent '{group.Parent}'.");
        }

        foreach (var entry in universe.Entries)
        {
            foreach (var group in entry.Groups.Where(g => !groupIds.Contains(g)))
            {
                errors.Add($"Entry '{entry.Id}' uses unknown group '{group}'.");
            }
        }

        var orderIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var order in universe.Orders)
        {
            if (!orderIds.Add(order.Id))
            {
                errors.Add($"Duplicate order id '{order.Id}'.");
            }

            if ((order.Items is null) == (order.Derive is null))
            {
                errors.Add($"Order '{order.Id}' must have exactly one of items or derive.");
            }

            if (order.Items is not null)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in order.Items)
                {
                    if (!entryIds.Contains(item))
                    {
                        errors.Add($"Order '{order.Id}' references unknown entry '{item}'.");
                    }

                    if (!seen.Add(item))
                    {
                        errors.Add($"Order '{order.Id}' lists entry '{item}' more than once.");
                    }
                }
            }
        }

        foreach (var order in universe.Orders.Where(o => o.Derive is not null))
        {
            var derive = order.Derive!;
            if (derive.From is not null && !orderIds.Contains(derive.From))
            {
                errors.Add($"Order '{order.Id}' derives from unknown order '{derive.From}'.");
            }

            foreach (var group in (derive.Groups ?? []).Where(g => !groupIds.Contains(g)))
            {
                errors.Add($"Order '{order.Id}' filters on unknown group '{group}'.");
            }

            foreach (var flag in (derive.Flags ?? []).Concat(derive.ExcludeFlags ?? []).Where(f => !universe.Flags.ContainsKey(f)))
            {
                errors.Add($"Order '{order.Id}' filters on undeclared flag '{flag}'.");
            }
        }

        foreach (var key in (universe.Discover?.Exclude ?? []).Where(k => !ExcludeRegex().IsMatch(k)))
        {
            errors.Add($"Discover exclusion '{key}' must look like movie:<tmdb> or tv:<tmdb>.");
        }

        if (HasDeriveCycle(universe))
        {
            errors.Add("Derived orders form a cycle.");
        }

        if (universe.Hub is not null)
        {
            foreach (var row in universe.Hub.Rows.Concat(universe.Hub.Pages.SelectMany(p => p.Rows)).Where(r => !orderIds.Contains(r)))
            {
                errors.Add($"Hub references unknown order '{row}'.");
            }
        }

        return errors;
    }

    private static bool HasDeriveCycle(UniverseDefinition universe)
    {
        var from = universe.Orders
            .Where(o => o.Derive?.From is not null)
            .GroupBy(o => o.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Derive!.From!, StringComparer.Ordinal);
        foreach (var start in from.Keys)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { start };
            var current = start;
            while (from.TryGetValue(current, out var next))
            {
                if (!visited.Add(next))
                {
                    return true;
                }

                current = next;
            }
        }

        return false;
    }

    [GeneratedRegex("^(movie|tv):[0-9]+$")]
    private static partial Regex ExcludeRegex();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugRegex();
}
