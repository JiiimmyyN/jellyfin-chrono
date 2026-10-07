namespace Jellyfin.Plugin.Chrono.Hub;

public sealed class UniverseSummaryDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int EntryCount { get; set; }

    public int OwnedCount { get; set; }

    public List<string> PosterUrls { get; set; } = [];

    public string? BackdropUrl { get; set; }
}

public sealed class HubEntryDto
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Subtitle { get; set; }

    public int? SeasonNumber { get; set; }

    public string? Released { get; set; }

    public int? Year { get; set; }

    public string? Overview { get; set; }

    public List<string> Flags { get; set; } = [];

    public List<string> Groups { get; set; } = [];

    public int TmdbId { get; set; }

    public string MediaType { get; set; } = "movie";

    public bool Owned { get; set; }

    public string? ItemId { get; set; }

    public string? PosterUrl { get; set; }

    public string? BackdropUrl { get; set; }

    public bool Played { get; set; }

    public double? Progress { get; set; }

    public int? UnplayedCount { get; set; }

    public int? RuntimeMinutes { get; set; }

    public int? EpisodeCount { get; set; }
}

public sealed class HubRowDto
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Basis { get; set; }

    public List<string> EntryIds { get; set; } = [];

    public bool Playable { get; set; }
}

public sealed class HubPageDto
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public List<HubRowDto> Rows { get; set; } = [];
}

public sealed class AttributionDto
{
    public string Name { get; set; } = string.Empty;

    public string? Url { get; set; }

    public string? License { get; set; }
}

public sealed class UniverseHubDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Revision { get; set; }

    public List<AttributionDto> Attribution { get; set; } = [];

    public Dictionary<string, string> FlagLabels { get; set; } = [];

    public string? BackdropUrl { get; set; }

    public string PrimaryRowId { get; set; } = string.Empty;

    public string? NextEntryId { get; set; }

    public Dictionary<string, HubEntryDto> Entries { get; set; } = [];

    public List<HubRowDto> Rows { get; set; } = [];

    public List<HubPageDto> Pages { get; set; } = [];

    public bool RequestsEnabled { get; set; }
}

public sealed class PlayRequestDto
{
    public string RowId { get; set; } = string.Empty;

    public string? FromEntryId { get; set; }

    public List<string> ExcludeFlags { get; set; } = [];
}
