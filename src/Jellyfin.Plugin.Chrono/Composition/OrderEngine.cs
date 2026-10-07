using Jellyfin.Plugin.Chrono.Definitions;

namespace Jellyfin.Plugin.Chrono.Composition;

public static class OrderEngine
{
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Evaluate(UniverseDefinition universe, IReadOnlyCollection<string> excludedFlags)
    {
        var entries = universe.Entries
            .GroupBy(e => e.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var orders = universe.Orders
            .GroupBy(o => o.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var groupChildren = universe.Groups
            .Where(g => g.Parent is not null)
            .ToLookup(g => g.Parent!, g => g.Id, StringComparer.Ordinal);

        var results = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var evaluating = new HashSet<string>(StringComparer.Ordinal);

        IReadOnlyList<string> EvaluateOrder(string orderId)
        {
            if (results.TryGetValue(orderId, out var cached))
            {
                return cached;
            }

            if (!orders.TryGetValue(orderId, out var order) || !evaluating.Add(orderId))
            {
                return [];
            }

            IReadOnlyList<string> ids;
            if (order.Items is not null)
            {
                ids = order.Items.Where(entries.ContainsKey).Distinct(StringComparer.Ordinal).ToList();
            }
            else if (order.Derive is not null)
            {
                ids = Derive(order.Derive);
            }
            else
            {
                ids = [];
            }

            evaluating.Remove(orderId);
            results[orderId] = ids;
            return ids;
        }

        IReadOnlyList<string> Derive(DeriveDefinition derive)
        {
            IEnumerable<EntryDefinition> source = derive.From is null
                ? universe.Entries
                : EvaluateOrder(derive.From).Select(id => entries[id]);

            if (derive.Types is { Count: > 0 } types)
            {
                source = source.Where(e => types.Contains(e.Type));
            }

            if (derive.Groups is { Count: > 0 } groups)
            {
                var expanded = ExpandGroups(groups, groupChildren);
                source = source.Where(e => e.Groups.Any(expanded.Contains));
            }

            if (derive.Flags is { Count: > 0 } flags)
            {
                source = source.Where(e => e.Flags.Any(flags.Contains));
            }

            if (derive.ExcludeFlags is { Count: > 0 } excluded)
            {
                source = source.Where(e => !e.Flags.Any(excluded.Contains));
            }

            var list = source.DistinctBy(e => e.Id, StringComparer.Ordinal).ToList();
            return derive.SortBy switch
            {
                OrderSort.Released => list
                    .Select((entry, index) => (entry, index))
                    .OrderBy(x => x.entry.ReleaseDate ?? DateOnly.MaxValue)
                    .ThenBy(x => x.index)
                    .Select(x => x.entry.Id)
                    .ToList(),
                OrderSort.Title => list
                    .OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Season ?? 0)
                    .Select(e => e.Id)
                    .ToList(),
                _ => list.Select(e => e.Id).ToList()
            };
        }

        foreach (var order in universe.Orders)
        {
            EvaluateOrder(order.Id);
        }

        if (excludedFlags.Count == 0)
        {
            return results;
        }

        return results.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value.Where(id => !entries[id].Flags.Any(excludedFlags.Contains)).ToList(),
            StringComparer.Ordinal);
    }

    private static HashSet<string> ExpandGroups(IEnumerable<string> groups, ILookup<string, string> children)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(groups);
        while (queue.Count > 0)
        {
            var group = queue.Dequeue();
            if (!result.Add(group))
            {
                continue;
            }

            foreach (var child in children[group])
            {
                queue.Enqueue(child);
            }
        }

        return result;
    }
}
