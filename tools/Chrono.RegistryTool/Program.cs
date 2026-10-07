using System.Globalization;
using System.Text;
using Chrono.RegistryTool.Metadata;
using Chrono.RegistryTool.Requests;
using Jellyfin.Plugin.Chrono.Composition;
using Jellyfin.Plugin.Chrono.Definitions;
using Jellyfin.Plugin.Chrono.Sources;

var command = args.FirstOrDefault() ?? "help";
var registry = Path.GetFullPath(args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : "registry");
var output = OptionValue("--output");

return command switch
{
    "validate" => Validate(registry),
    "discover" => await DiscoverAsync(registry, output),
    "apply-issue" => await ApplyIssueAsync(registry, OptionValue("--kind"), OptionValue("--body"), output),
    _ => Help()
};

static int Help()
{
    Console.WriteLine("""
        Chrono registry tool

          validate [registry-dir]                    Validate index.json and every universe file.
          discover [registry-dir] [--output file]    List Wikidata titles that are not curated yet (markdown).
          apply-issue [registry-dir] --kind <add-title|move-title|exclude-title|new-universe> --body <issue.md> [--output summary.md]
                                                     Apply a GitHub issue-form request to the registry files.
        """);
    return 1;
}

static int Validate(string registry)
{
    var errors = new List<string>();
    var index = DefinitionJson.ParseIndex(File.ReadAllText(Path.Combine(registry, "index.json")));
    var listed = new HashSet<string>(StringComparer.Ordinal);
    foreach (var item in index.Universes)
    {
        var path = Path.Combine(registry, item.File);
        listed.Add(Path.GetFullPath(path));
        if (!File.Exists(path))
        {
            errors.Add($"{item.File}: listed in index.json but missing.");
            continue;
        }

        UniverseDefinition universe;
        try
        {
            universe = DefinitionJson.ParseUniverse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException)
        {
            errors.Add($"{item.File}: {ex.Message}");
            continue;
        }

        if (universe.Id != item.Id)
        {
            errors.Add($"{item.File}: id '{universe.Id}' does not match index id '{item.Id}'.");
        }

        errors.AddRange(DefinitionValidator.Validate(universe).Select(e => $"{item.File}: {e}"));
        errors.AddRange(universe.Entries.Where(e => e.ReleaseDate is null).Select(e => $"{item.File}: entry '{e.Id}' has no release date."));
        errors.AddRange(universe.Entries
            .GroupBy(e => (e.Type == EntryType.Movie, e.Tmdb, e.Season))
            .Where(g => g.Count() > 1)
            .Select(g => $"{item.File}: entries {string.Join(", ", g.Select(e => e.Id))} point at the same title."));
        var orders = OrderEngine.Evaluate(universe, []);
        var (rows, pages) = UniverseComposer.HubLayout(universe);
        errors.AddRange(rows.Concat(pages.SelectMany(p => p.Rows)).Where(r => orders[r].Count == 0).Select(r => $"{item.File}: hub row '{r}' is empty."));
        Console.WriteLine($"{item.File}: {universe.Entries.Count} entries, {universe.Orders.Count} orders, revision {universe.Revision}");
    }

    foreach (var file in Directory.EnumerateFiles(Path.Combine(registry, "universes"), "*.json").Where(f => !listed.Contains(Path.GetFullPath(f))))
    {
        errors.Add($"{Path.GetRelativePath(registry, file)}: not listed in index.json.");
    }

    foreach (var error in errors)
    {
        Console.Error.WriteLine("error: " + error);
    }

    Console.WriteLine(errors.Count == 0 ? "Registry is valid." : $"{errors.Count} error(s).");
    return errors.Count == 0 ? 0 : 1;
}

static async Task<int> DiscoverAsync(string registry, string? output)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd(CachedHttp.UserAgent);
    var index = DefinitionJson.ParseIndex(File.ReadAllText(Path.Combine(registry, "index.json")));
    var report = new StringBuilder();
    var horizon = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(2);
    var total = 0;
    foreach (var item in index.Universes)
    {
        var universe = DefinitionJson.ParseUniverse(File.ReadAllText(Path.Combine(registry, item.File)));
        var anchors = universe.Discover?.Wikidata ?? [];
        if (anchors.Count == 0)
        {
            continue;
        }

        var discovered = await FranchiseAsync(client, anchors);
        var merged = UniverseComposer.AddDiscovered(universe, discovered);
        var candidates = merged.Entries
            .Where(e => e.Flags.Contains(UniverseComposer.DiscoveredFlag))
            .Where(e => e.ReleaseDate is null || e.ReleaseDate <= horizon)
            .OrderByDescending(e => e.ReleaseDate ?? DateOnly.MaxValue)
            .ToList();
        if (candidates.Count == 0)
        {
            continue;
        }

        total += candidates.Count;
        report.AppendLine(CultureInfo.InvariantCulture, $"## {universe.Name} ({candidates.Count})").AppendLine();
        report.AppendLine("| Title | Type | Released | TMDB |").AppendLine("|---|---|---|---|");
        foreach (var entry in candidates)
        {
            var kind = entry.Type == EntryType.Movie ? "movie" : "tv";
            report.AppendLine(CultureInfo.InvariantCulture, $"| {entry.Title} | {kind} | {entry.Released ?? "?"} | [{entry.Tmdb}](https://www.themoviedb.org/{kind}/{entry.Tmdb}) |");
        }

        report.AppendLine();
    }

    var text = total == 0
        ? "Every Wikidata title is curated.\n"
        : $"# Titles to review\n\nWikidata lists {total} title(s) that are not in the registry. Add the ones that belong in the universe, or ignore them (documentaries, parodies, non-canon specials).\n\n" + report;
    if (output is not null)
    {
        await File.WriteAllTextAsync(output, text);
    }

    Console.Write(text);
    return 0;
}

static async Task<SourceResult> FranchiseAsync(HttpClient client, IReadOnlyCollection<string> anchors)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "https://query.wikidata.org/sparql")
    {
        Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("query", WikidataClient.BuildFranchiseQuery(anchors))])
    };
    request.Headers.Accept.ParseAdd("application/sparql-results+json");
    using var response = await client.SendAsync(request);
    response.EnsureSuccessStatusCode();
    return WikidataClient.ParseFranchise(await response.Content.ReadAsStringAsync(), anchors);
}

static async Task<int> ApplyIssueAsync(string registry, string? kind, string? bodyPath, string? output)
{
    if (kind is null || bodyPath is null)
    {
        return Help();
    }

    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    client.DefaultRequestHeaders.UserAgent.ParseAdd(CachedHttp.UserAgent);
    var editor = new RegistryEditor(registry, MetadataResolver.Create(), anchors => FranchiseAsync(client, anchors)!, DateOnly.FromDateTime(DateTime.UtcNow));
    var text = new StringBuilder();
    int exitCode;
    try
    {
        await editor.ApplyAsync(kind, IssueForm.Parse(await File.ReadAllTextAsync(bodyPath)));
        var files = editor.Save();
        text.AppendLine("### Changes").AppendLine();
        foreach (var line in editor.Summary)
        {
            text.AppendLine("- " + line);
        }

        if (editor.Warnings.Count > 0)
        {
            text.AppendLine().AppendLine("### Warnings").AppendLine();
            foreach (var warning in editor.Warnings)
            {
                text.AppendLine("- " + warning);
            }
        }

        text.AppendLine().AppendLine("Files: " + string.Join(", ", files.Select(f => $"`registry/{f}`")));
        exitCode = 0;
    }
    catch (Exception ex) when (ex is RequestException or HttpRequestException or System.Text.Json.JsonException)
    {
        text.AppendLine("The request could not be applied:").AppendLine().AppendLine("> " + ex.Message.Replace("\n", "\n> ", StringComparison.Ordinal));
        exitCode = 2;
    }

    if (output is not null)
    {
        await File.WriteAllTextAsync(output, text.ToString());
    }

    Console.Write(text);
    return exitCode;
}

string? OptionValue(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
