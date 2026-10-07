# Chrono HTTP API

All routes are relative to the Jellyfin server base URL and require a normal Jellyfin access token, except the static client assets. Send the full client header (`Authorization: MediaBrowser Client="…", Device="…", DeviceId="…", Version="…", Token="…"`, as built by `ApiClient.setRequestHeaders`): *Play* finds the caller's session through its `DeviceId`.

Errors are returned as `application/problem+json` with `title`, `detail` and `status`.

## Types

```ts
type EntryType = 'movie' | 'series' | 'season';

type RequestStatus =
  | 'available'   // fully available in the library (Seerr view)
  | 'partial'     // partially available
  | 'processing'  // approved, downloading
  | 'pending'     // requested, awaiting approval
  | 'none'        // not requested
  | 'unknown';

interface UniverseSummary {
  id: string;
  name: string;
  description?: string;
  entryCount: number;
  ownedCount: number;
  posterUrls: string[];   // up to 6 poster URLs for a collage (owned first)
  backdropUrl?: string;
}

interface HubEntry {
  id: string;             // entry id, unique within the universe
  type: EntryType;
  title: string;          // movie/series title
  subtitle?: string;      // e.g. "Season 2"
  seasonNumber?: number;
  released?: string;      // yyyy-MM-dd
  year?: number;
  overview?: string;
  flags: string[];
  groups: string[];
  tmdbId: number;
  mediaType: 'movie' | 'tv';
  owned: boolean;         // present in the library (for seasons: season exists with at least one episode)
  itemId?: string;        // Jellyfin item id when owned (movie, series or season item)
  posterUrl?: string;     // server-relative ("Items/…/Images/Primary?…") when owned, absolute (TMDB) otherwise
  backdropUrl?: string;
  played: boolean;        // movie watched / all episodes watched
  progress?: number;      // 0..1, movie resume position or fraction of episodes watched
  unplayedCount?: number; // seasons/series: unwatched episode count
  runtimeMinutes?: number;
  episodeCount?: number;  // seasons/series: owned episode count
}

interface HubRow {
  id: string;             // order id
  title: string;
  description?: string;
  basis?: string;
  entryIds: string[];     // in display order, already filtered by server-side admin settings
  playable: boolean;      // at least one owned entry
}

interface HubPage {
  id: string;
  title: string;
  rows: HubRow[];
}

interface UniverseHub {
  id: string;
  name: string;
  description?: string;
  revision?: string;
  attribution: { name: string; url?: string; license?: string }[];
  flagLabels: Record<string, string>;   // flag id -> label, for client-side filter toggles
  backdropUrl?: string;
  primaryRowId: string;                  // the main timeline row
  nextEntryId?: string;                  // first owned, not fully played entry in the primary row; absent when everything owned is watched
  entries: Record<string, HubEntry>;
  rows: HubRow[];
  pages: HubPage[];
  requestsEnabled: boolean;              // Seerr configured and requests allowed
}
```

URLs that are not absolute (`http…`) are relative to the server base URL; prefix them with `ApiClient.serverAddress() + '/'`. Image URLs do not need an auth token.

## Endpoints

| Method & path | Body / query | Response |
|---|---|---|
| `GET /Chrono/Universes` | — | `UniverseSummary[]` (enabled universes only) |
| `GET /Chrono/Universes/{id}` | — | `UniverseHub` |
| `GET /Chrono/Universes/{id}/RequestStatus` | — | `Record<entryId, RequestStatus>` for entries that are not owned. Empty object when Seerr is not configured. |
| `POST /Chrono/Universes/{id}/Entries/{entryId}/Request` | — | `{ status: RequestStatus }`. 400 when the entry is owned or requests are disabled. |
| `POST /Chrono/Universes/{id}/Play` | `{ rowId: string, fromEntryId?: string, excludeFlags?: string[] }` | 204. The server sends a *PlayNow* command with the episode-expanded owned items of the row (starting at `fromEntryId`, at most 250 items) to the calling client's session. Entries with any of `excludeFlags` are skipped; when `fromEntryId` itself is excluded, playback starts at the next included entry. Inside a partly watched season, playback starts at the first unwatched episode. `rowId` may be any row, including rows that only appear on pages. 404 when no session is found. |
| `GET /Chrono/Client/chrono.js`, `GET /Chrono/Client/chrono.css` | — | Static web client bundle (anonymous) |

### Admin (requires administrator)

| Method & path | Body / query | Response |
|---|---|---|
| `GET /Chrono/Admin/Status` | — | Per-universe resolution report |
| `POST /Chrono/Admin/Refresh` | — | Triggers a full refresh (sources, resolution, playlists, collections) |
| `GET /Chrono/Admin/Available` | — | Universes available from registries |
| `GET /Chrono/Admin/Suggestions` | — | Universe suggestions discovered from the library |
| `GET /Chrono/Admin/Wikidata/Search?q=` | — | `{ id, label, description }[]` |
| `POST /Chrono/Admin/Seerr/Test` | `{ url, apiKey }` | `{ ok: boolean, message: string }` |

## Web client integration

The plugin injects `<script src="…/Chrono/Client/chrono.js">` into jellyfin-web's `index.html` and adds a "Universes" menu link to `config.json`. The client:

* renders on hash routes `#/chrono` (universe gallery), `#/chrono/{universeId}` and `#/chrono/{universeId}/{pageId}` as an overlay above jellyfin-web's (fallback) page;
* uses the global `window.ApiClient` for the server address, access token and user id;
* navigates to items with `#/details?id={itemId}&serverId={ApiClient.serverId()}`.
