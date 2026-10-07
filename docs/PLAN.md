# Jellyfin Chrono — research & plan

A Jellyfin plugin that presents film/TV universes the way Disney+ does: one hub per universe with several ordered rows (complete timeline, movie timeline, sagas/phases, release order), placing movies, series and individual seasons next to each other (never single episodes). MCU is the primary target; Star Wars is the second; the design must generalise to any connected universe or collection, with data loaded dynamically wherever possible.

Research date: 2026-10-07. Target: Jellyfin 12.2 (released 2026-10-05, .NET 10).

## Status (2026-10-07): v0.1 implemented

Everything in milestones M0–M6 is built and was exercised against a real Jellyfin 12.2 server (Docker, generated library) and Seerr 3.5:

* Curated registry: MCU (101 entries: Marvel.com 2026 Disney+ order plus every ABC/Netflix/Hulu/Freeform show placed by in-universe dates) and Star Wars (52 entries, season level). Wikidata discovery with curator exclusions.
* Sources: registry (remote with bundled fallback, multiple registries), local files, Wikidata franchises, TMDB collections, MDBList and Trakt lists; library-based suggestions.
* Hub (SolidJS) injected into jellyfin-web by the plugin itself (no other plugins needed): gallery, hubs, pages, filters, Continue / Play from here via the server's PlayNow command, Seerr requests per movie or season.
* Public playlists and ordered collections (opt-in), admin page, scheduled/after-scan refresh, registry tooling and CI.

Changes from the plan below: episode-level "deep timelines" were dropped (the finest granularity is a season, as requested); the hub ships as its own injected script instead of depending on Plugin Pages / Home Screen Sections; Seerr requests were added.

Not verified: the Android app itself (it loads the same jellyfin-web, which was tested at phone width) and Trakt lists (parser unit-tested; needs a VIP client id).

---

## 1. Research findings

### 1.1 Data sources

No live third-party source provides all three orderings, with stable IDs, at season/episode granularity, under terms that allow a plugin to depend on it.

| Source | Has | Granularity | IDs | Access | Verdict |
|---|---|---|---|---|---|
| **Marvel.com "Complete MCU Timeline on Disney+"** (updated 2026-06-02) | Official chronological order | Season-level, incl. Netflix Defenders shows | none | HTML | Reference for the default MCU timeline |
| **StarWars.com viewing guide** (updated 2026-09-03) + **Clone Wars chronological episode order** | Official release + chronological order; official TCW episode order (film placed between S1E16 and S3E1) | Series-level; TCW episode-level | none | HTML | Reference for the default Star Wars timeline |
| **Wikidata** (CC0, free SPARQL) | MCU phases & sagas as items with release ordinals; TMDB/IMDb/TVDB cross-walk | Films good, TV poor (only 6/22 MCU series linked to a phase); no in-universe chronology | TMDB, IMDb, TVDB | Open SPARQL | Use for phase/saga membership, dates and ID cross-walks at curation time |
| **Fandom wikis** (MCU Wiki, Wookieepedia; CC BY-SA) | Chronological & release viewing timelines, current through 2026 | **Episode-level** with in-universe dates | none (title + S/E) | MediaWiki API | Best reference for episode placement; curation aid, not runtime feed |
| **TMDB** | Collections (movies only), keyword 180547 (MCU, noisy), user lists (stale) | Lists: movie + whole series only | TMDB | API key | ID validation, dates, artwork, new-release discovery. Not an ordering source |
| **Trakt** | Best list model (movie/show/season/episode with rank) | Episode-level | All | **VIP-only app creation since ~2026-07-31**; Kometa removed Trakt in v2.5.0 | Optional bring-your-own-key only |
| **MDBList** | Ranked lists, keyless `/lists/{user}/{slug}/json` | Movie + whole series only | TMDB, IMDb, TVDB | Free key, 1,000 req/day | Optional import for long-tail universes |
| **IMDb lists** (what Kometa uses: MCU `ls539646485`, 571 episode-level entries) | Episode-level timelines | Episode | IMDb | No API, scraping blocked/forbidden | Cross-check only |
| Letterboxd / Simkl / TVDB lists | — | — | — | Closed / paid / stale | Skip |

Chronology is genuinely contested, so the plugin must support **several named orders per universe**:

- MCU: Disney+ vs. the DK "Official Timeline" book vs. Fandom; Captain Marvel vs. Iron Man; where Fantastic Four: First Steps (another Earth), Loki and What If…? (outside time / alternate) go; whether ABC/Netflix shows count; seasons spanning eras (Fandom splits Wonder Man; Eyes of Wakanda spans 1260 BC–1896).
- Star Wars: Clone Wars film interleaved in the series; epilogues jumping ahead (TCW finale after The Bad Batch, Rebels epilogue during Ahsoka); Andor and Rebels concurrent; Tales anthologies span eras.

### 1.2 Jellyfin capabilities (verified against v12.2 source)

**Collections (BoxSet)**
- `DisplayOrder = "Default"` returns children in stored `LinkedChildren` order (12.0 persists it in a relational `LinkedChildren` table with `SortOrder`). A plugin can set exact order: assign `LinkedChildren`, set `DisplayOrder`, `UpdateToRepositoryAsync`. There is no reorder API; `AddToCollectionAsync` only appends.
- Any item type can be a child (Movie, Series, Season, Episode, BoxSet). Nested BoxSets work in code (`FlattenItems` has a cycle guard) but aren't officially "supported".
- **Caveat:** jellyfin-web's collection page splits children into type sections (Movies, Series, Episodes, …, Collections, Other), preserving order *within* each. A mixed "Iron Man → Loki S1 → Thor" row will not render interleaved. Android TV splits similarly.
- **Caveat:** "Play" on a collection requests `Recursive=true` without a sort, so it plays in SortName order on web and Android TV.
- **Caveat:** Jellyfin's collection metadata lookup can rename, re-describe, and stamp a TMDB ID on a collection by name. The built-in "automatically add to collection" can append into a same-named BoxSet. Use distinctive names, lock fields, and strip provider IDs (SmartLists does this, issue #433).

**Playlists**
- Exact insertion order; no client-side resort. Movies and episodes mix freely (`MediaType.Video`, pass it explicitly or it defaults to Audio).
- `OpenAccess` (public) makes them visible to all users; must have an owner (pick an admin).
- Only playable items: a Series/Season expands to episodes *at add time* (snapshot, not live).
- 12.0: positional `AddItemToPlaylistAsync`, duplicates allowed, `MoveItemAsync`.

**Plugin infrastructure**
- Template repo still targets 10.11/.NET 9. Set it manually: `net10.0`, `Jellyfin.Controller`/`Jellyfin.Model` 12.2.0 (`ExcludeAssets=runtime`), `targetAbi 12.0.0.0`.
- DI via `IPluginServiceRegistrator`; `IHostedService`, `IScheduledTask`, `ILibraryPostScanTask`; library events `ItemAdded/Updated/Removed`.
- Plugin assemblies are MVC application parts, so custom `[ApiController]` routes work. Use `IDtoService.GetBaseItemDtos` to return standard `BaseItemDto`s (with user data such as played state).
- Bulk resolution: `InternalItemsQuery.HasAnyProviderIds` (`Dictionary<string,string[]>`) plus `IncludeItemTypes`. TMDB movie and TV IDs are separate namespaces and can collide.
- Plugin config pages are admin-only. There is no native mechanism for server-defined home rows or user pages.

**UI extension (web-based clients only)**
- *File Transformation* rewrites `/web/*` responses in middleware. *Plugin Pages* adds user-facing pages to the menu. *Home Screen Sections* (HSS) replaces the home screen and accepts third-party rows via reflection `RegisterSection` returning ordered `BaseItemDto`s. *JavaScript Injector* injects scripts. All have 12.x builds.
- HSS patches a minified web chunk and had a rough 12.0 rollout, so it's the most fragile piece.
- These reach browser, Jellyfin Desktop/Media Player, the Android/iOS mobile apps, and webOS. They do **not** reach Android TV, Tizen, Swiftfin, Findroid, Kodi, or Roku.

**Upstream risks**
- Open PR #18101: 10.11→12.x migrations can delete playlists and collections. The plugin must be able to rebuild everything from its definitions.
- Plugins need separate builds per Jellyfin major (10.11 vs 12).

### 1.3 Dynamic discovery test (Wikidata, run 2026-10-07)

Querying items linked to a franchise or fictional universe through *media franchise* (P8345), *takes place in fictional universe* (P1434), or *part of the series* (P179, following P361 "part of" chains), and keeping only items that have a TMDB ID, returned:

| Anchor | Movies | TV series |
|---|---|---|
| Marvel Cinematic Universe (Q642878) | 58 | 28 |
| Star Wars franchise (Q462) | 28 | 26 |
| Star Wars universe (Q19786052) | 13 | 17 |
| Wizarding World (Q30739117) | 14 | 1 |

- Sample films are also linked to sub-series such as "Star Wars original trilogy" and "Peter Jackson's Middle-earth film series", which are usable as automatic group rows.
- **Noise:** the Star Wars results include LEGO specials, documentaries, and Family Guy parodies ("Blue Harvest"). Intersecting with the user's library removes most of it, and `instance of` filters can remove documentaries and parodies.
- **Duplicate dates:** films have one publication date (P577) per country. Use the earliest date.
- **Conclusion:** *membership*, *release order* and *sub-series grouping* can be discovered dynamically for almost any franchise. *In-universe chronology* cannot; it needs a curated or user-supplied ranked list.

### 1.4 Prior art

- **No existing plugin delivers Disney+-style multi-order universe hubs.**
- **KassFlute/jellyfin-plugin-mcu-timeline** (GPL-3.0, created 2026-10-06) ships a curated `mcu-timeline.json`: 70 entries with TMDB/IMDb IDs, season-level entries, `chronoOrder`, `phase`, `saga`, and a user override file. Same idea, MCU only, single order. Worth reviewing, and possibly collaborating with, before M1.
- **SmartLists** (AGPL, C#, 12.x): best-in-class order preservation from external lists, including Trakt episode lists. Sets `DisplayOrder=Default` and strips the collection TMDB ID. Good reference code.
- **Nalanda** (AGPL, Python): Kometa-like YAML for Jellyfin. Its `order: source|sort_name|release_date` maps to `DisplayOrder`, and it uses a longest-common-prefix reconcile because collections are append-only.
- **Kometa** (Plex-only): `defaults/both/universe.yml` keys (`mcu`, `star`, `trek`, `xmen`, `middle`, `wizard`, `dcu`, `arrow`, …) use custom order from volunteer IMDb/MDBList lists. Separate "(Timeline Order)" playlists hold movies and episodes.
- **Datasets** (mostly unlicensed, error-prone): AugustoMarcelo/mcuapi (phase/saga/chronology fields), ThatGuySam/marvelorder (TMDB-keyed; links a 732-row episode-level MCU sheet; has a good write-up on title-matching pitfalls), voodoogumbo/starwars-watch-order (TMDB-keyed, `tv:{id}:S{n}:E{n}`), wylie/starwars (official era names, BBY/ABY dates).

**Disney+ hub structure (what we're imitating)**
- *Marvel:* Featured; Infinity Saga (release order, with a Phase One/Two/Three sub-page); Multiverse Saga; **MCU Movie Timeline** (films only); **MCU Complete Timeline** (films, series, one-shots); themed rows (The Defenders, Origin Stories, …); "Prepare for <next release>".
- *Star Wars:* Movies (I–IX); **Timeline Order** (series appear *once*, as a single tile, in their earliest slot); Series & Specials; Animation; a "Collections" row linking to sub-hubs (Skywalker Saga, Tales, …).
- Disney+ never interleaves episodes. Neither will we: the finest granularity is a season.

---


## 2. Requirements (confirmed 2026-10-07)

| Topic | Decision |
|---|---|
| Clients | Jellyfin web and the Android mobile app. Both render jellyfin-web served by the server, so a web hub reaches both. No Android TV or other native clients. |
| Granularity | Movies, series and **seasons** placed next to each other. Season-level splitting is preferred; whole-series placement is acceptable. Episodes are never placed individually. |
| MCU default | Marvel.com "Complete MCU Timeline" (2026), **plus every MCU-connected show from every network** (ABC, Netflix, Hulu, Freeform, Disney+, Marvel Animation). All of it included by default. |
| Scope | Any connected universe or collection, not just the MCU and Star Wars. Data is loaded dynamically wherever possible. |

---

## 3. Decisions

1. **A universe is members plus orders, assembled from pluggable *sources*.** No single source can do everything, so each contributes what it's good at. The plugin combines them, and new universes need no plugin release.
2. **Dynamic first, curated where dynamic can't reach.**
   - Membership, release order and sub-series grouping come from dynamic sources (Wikidata, TMDB collections, user list URLs).
   - In-universe chronology needs a ranked list. That comes from the curated *registry* (fetched at runtime, like a plugin repository) or from a list the user points at.
   - Without one, the timeline falls back to release order, which is correct for many franchises (Harry Potter, John Wick, …).
3. **Release order is automatically season-level.** Jellyfin already knows each season's premiere date, so any universe gets a season-split release order with no curation.
4. **The web hub is the primary UI.** Public playlists provide continuous "play in order". Native collections are optional and off by default, since they split by type and the hub covers both clients.
5. **Stack:** C# / .NET 10 plugin for Jellyfin 12.2. The hub is SolidJS + TypeScript + Vite, embedded in the plugin. State lives in plugin config plus JSON caches in the plugin data directory; there is no external database.

---

## 4. Sources

| Source | Members | Orders it provides | Finest granularity | Network | User setup |
|---|---|---|---|---|---|
| **Registry** (curated JSON, remote) | yes | Named chronological orders, groups (phases, sagas, eras), labels | season | HTTP GET from registry URL(s); cached, with a bundled snapshot | None; the default registry is built in. Extra registry URLs can be added. |
| **Wikidata** | yes | Release; groups from sub-series (e.g. "original trilogy", MCU phases) | series (plus automatic season split for release order) | SPARQL, cached about weekly | Pick a franchise from a search box (`wbsearchentities`) |
| **TMDB collection** | yes (movies) | Release | movie | None: Jellyfin stores `TmdbCollection` IDs on movies | Auto-suggested |
| **MDBList list** | yes | Custom rank | series | Keyless `…/json` endpoint | Paste a list URL |
| **Trakt list** | yes | Custom rank | season | Trakt API; needs the user's own VIP client ID | URL plus key |
| **Local file** | yes | Anything | season | None | Drop a file in the plugin's data directory |
| **Derived** | — | Release sort, group/tag filters, type filters ("Movie Timeline"), `from` another order | — | — | Built in |

**How sources combine**
- **Members:** the union of all sources, deduplicated by TMDB ID, with IMDb/TVDB as fallbacks.
- **Orders:** each order comes from one ranked source, or is derived.
- **Members missing from a ranked order:**
  - Left out of that row.
  - Still appear in Release Order.
  - Listed in the admin report as "not placed", so a user can see when a new release isn't in the timeline yet.
- **Unowned items:**
  - Curated universes show them greyed out (toggleable).
  - Dynamic universes show owned items only, which also hides Wikidata noise.
- **Discovery:**
  - The admin page suggests universes from the library, e.g. "You have 9 titles from Wizarding World" (via Wikidata) or "7 titles in the John Wick Collection" (via TMDB collections).
  - Each suggestion can be enabled with one click.

**Where curated data lives**
- The registry is just one source, fetched at runtime from a URL, and the plugin supports several registry URLs.
- The default registry starts in this repo under `registry/`: an `index.json` plus one file per universe, served from a stable raw/Pages URL. It can move to its own repo later without a plugin change.
- A scheduled GitHub Action checks each curated universe's Wikidata anchor and TMDB, and opens a PR listing new titles that still need a chronological slot. New releases also show up automatically in release order via Wikidata before anyone curates them.

---

## 5. Universe definition format (registry and local files)

```json
{
  "$schema": "../schema/universe.schema.json",
  "id": "mcu",
  "name": "Marvel Cinematic Universe",
  "revision": "2026.10.1",
  "discover": { "wikidata": ["Q642878"] },
  "attribution": ["marvel.com Complete MCU Timeline (2026-06-02)", "Wikidata (CC0)"],
  "entries": {
    "iron-man": { "type": "movie",  "tmdb": 1726,  "imdb": "tt0371746", "title": "Iron Man", "released": "2008-05-02", "groups": ["phase-1", "infinity-saga"] },
    "loki-s1":  { "type": "season", "tmdb": 84958, "season": 1, "title": "Loki — Season 1", "released": "2021-06-09", "groups": ["phase-4", "multiverse-saga"] },
    "agents-of-shield-s1": { "type": "season", "tmdb": "…", "season": 1, "flags": ["network-tv"], "basis": "fandom" }
  },
  "groups": [
    { "id": "phase-1", "title": "Phase One", "parent": "infinity-saga" }
  ],
  "orders": [
    { "id": "timeline",       "title": "Complete Timeline", "basis": "Marvel.com 2026 + network shows", "items": ["…", "iron-man", "…", "loki-s1", "…"] },
    { "id": "movie-timeline", "title": "Movie Timeline",    "derive": { "from": "timeline", "types": ["movie"] } },
    { "id": "release",        "title": "Release Order",     "derive": { "sortBy": "released" } },
    { "id": "phase-1",        "title": "Phase One",         "derive": { "group": "phase-1", "sortBy": "released" } }
  ],
  "hub": {
    "rows": ["timeline", "movie-timeline", "infinity-saga", "multiverse-saga", "release"],
    "pages": [{ "id": "phases", "title": "Phases", "rows": ["phase-1", "phase-2", "phase-3", "phase-4", "phase-5", "phase-6"] }]
  }
}
```

**Entry types**
- `movie`, `series`, and `season` (series TMDB ID plus season number).
- A `series` entry in an order expands to all its seasons, sorted by date, in that slot.

**Fields**
- `flags` (e.g. `network-tv`, `alternate-universe`, `one-shot`, `animated`) drive user filters such as "Marvel.com list only".
- `basis` records where a placement came from.

**Matching**
- Movies: TMDB, then IMDb.
- Series: TMDB, then TVDB, then IMDb.
- Seasons: the series item plus `IndexNumber`. Series that use TMDB episode-group orders can renumber seasons; the admin report flags mismatches, and per-entry overrides fix them.

**User-defined universes** use the same schema with a `sources` block instead of (or as well as) `entries` and `orders`. The config UI generates this, e.g.:

```json
{ "id": "middle-earth", "name": "Middle-earth",
  "sources": [
    { "type": "wikidata", "anchor": "<franchise QID>" },
    { "type": "mdblist", "url": "https://mdblist.com/lists/<user>/<slug>", "order": { "id": "timeline", "title": "Timeline" } }
  ] }
```

**Licensing**
- Plugin code: GPL-3.0.
- Registry data: CC BY-SA 4.0, because chronological placements of the network shows are referenced from Fandom. We store IDs and orderings, never prose.

---

## 6. Default curated content

**MCU — "Complete Timeline"**
- Marvel.com's 2026 Disney+ order, season-level. It covers the films, Disney+ series, Netflix Defenders shows, One-Shots, Eyes of Wakanda and Marvel Zombies.
- Plus shows missing from Marvel.com's list, placed by in-universe dates referenced from the MCU Fandom timeline, each flagged `network-tv` or similar:
  - Agents of S.H.I.E.L.D. S1–S7, Agent Carter S1–S2, Inhumans, Runaways S1–S3, Cloak & Dagger S1–S2, Helstrom.
  - Your Friendly Neighborhood Spider-Man.
  - Releases after June 2026, e.g. Spider-Man: Brand New Day.
- Other orders: Movie Timeline, Release Order (season-level), Infinity/Multiverse Saga, Phases 1–6.
- A one-toggle filter gives the pure Marvel.com list.

**Star Wars**
- Timeline per StarWars.com (series-level, split by season where a season clearly sits apart).
- Other rows: Skywalker Saga, Anthology films, Live-action series, Animation, Release Order.

---

## 7. Architecture

```
 Registry URLs ─┐
 Wikidata ──────┤                          ┌─▶ UniversesController ─▶ Web hub (web + Android app)
 TMDB coll. ────┼─▶ Sources ─▶ Composer ─▶ Resolver ─▶ ResolvedUniverse ─┼─▶ PlaylistReconciler ─▶ public playlists
 MDBList/Trakt ─┤   (cached)   (members,   (ILibraryManager)             └─▶ CollectionReconciler ─▶ BoxSets (optional)
 Local files ───┘              orders)
```

**Plugin components (C#)**
- **Sources** behind an `IUniverseSource` interface: `RegistrySource`, `WikidataSource`, `TmdbCollectionSource`, `MdbListSource`, `TraktSource`, `LocalFileSource`.
  - Each returns members and ranked orders.
  - Each caches responses in the data directory with a TTL and ETag.
  - Each falls back to its last good result.
- **`UniverseComposer`**: merges sources, expands derived orders and series-to-season splits, and applies user filters.
- **`LibraryResolver`**: batched `HasAnyProviderIds` queries per item type, plus a season lookup. Produces owned/missing status per entry.
- **`SyncService`**: a debounced reaction to library events plus a post-scan task, refreshing resolved universes and reconciling outputs. Also a manual/scheduled "Refresh universes" task, which is idempotent.
- **`PlaylistReconciler`**: public playlists (owner = configured admin, `MediaType = Video`) for each chosen order. Seasons expand to episodes for playback only.
- **`CollectionReconciler`** (optional): parent and row BoxSets with `DisplayOrder = "Default"`. Locked names, stripped TMDB IDs, and its own provider-ID marker.
- **`UniversesController`**:
  - `GET /Chrono/Universes`
  - `GET /Chrono/Universes/{id}`: rows of `BaseItemDto`s plus missing-entry placeholders and the user's "next up in timeline".
  - Discovery and preview endpoints for the admin UI.
  - Static hub assets.
- **Admin config page**:
  - Registries and enabled universes, plus discovery suggestions.
  - "Add universe": a Wikidata search, a list URL, or a TMDB collection.
  - Per-universe options: flags, missing items, playlists, collections.
  - A resolution report: unmatched, not placed, season mismatches.

**Web hub (SolidJS)**
- A universe gallery, then a universe page:
  - A hero banner and Disney+-style horizontal rows.
  - Season cards that link to the season page; series and movie cards that link to their item pages.
  - Watched badges and progress.
  - "Continue timeline", plus "Play in order" via the playlist.
  - Sub-pages for phases and sagas.
  - Missing titles greyed out.
- A mobile-first layout for the Android app.
- **Delivery (spike first, in M2):**
  - (a) Plugin Pages registration, giving an in-app menu entry.
  - (b) JavaScript Injector or File Transformation adding a menu entry and route.
  - (c) A standalone same-origin page that reuses jellyfin-web's stored credentials.
  - Pick whichever works best inside the Android app's WebView; keep the others as optional, soft, reflection-based integrations.
- **Later:** Home Screen Sections rows, e.g. "Continue the MCU".

---

## 8. Milestones

**M0 — Scaffold**
- Solution: `Jellyfin.Plugin.Chrono` (net10.0, Jellyfin 12.2), xUnit tests, `web/` (Vite + SolidJS), `registry/`, `schema/`.
- `build.yaml` and a GitHub Actions build producing the plugin zip.
- docker-compose with Jellyfin 12.2.
- A script that generates a fake library of tiny ffmpeg clips with `[tmdbid-…]` folder names (movies plus series with seasons).
- Review KassFlute/jellyfin-plugin-mcu-timeline for overlap.

**M1 — Core model + MCU**
- JSON Schema; `RegistrySource` from the bundled snapshot; `UniverseComposer`; `LibraryResolver`; read-only `UniversesController`.
- `mcu.json` with the full default content from §6.
- A validator (CI): schema, referential integrity, and TMDB ID/date checks using a CI secret.
- Unit tests for composition, derivation and season splitting.

**M2 — Web hub**
- Delivery spike on web and the Android app, then the hub.
- Universe page with rows, season cards, watched state, missing titles, phase/saga pages.
- **Exit criterion:** browsing the MCU timeline on the phone feels like Disney+.

**M3 — Watch in order**
- Public playlists per order; "Continue timeline" and "Play from here".
- Optional collections.
- Admin config page with the resolution report.

**M4 — Dynamic sources + Star Wars**
- `WikidataSource` (membership, release order, sub-series groups, noise filters) and `TmdbCollectionSource`.
- Discovery suggestions; remote registry refresh with multiple registries.
- `star-wars.json`.

**M5 — List sources + universe editor**
- MDBList, Trakt (bring your own key) and local files.
- "Add universe" UI.
- Registry freshness GitHub Action.

**M6 — Polish**
- Home Screen Sections rows; more curated universes (Star Trek, Middle-earth, Wizarding World, X-Men, DCU, Arrowverse); optional Seerr "request missing" links; a published plugin repository manifest.

---

## 9. Risks

| Risk | Mitigation |
|---|---|
| Hub delivery doesn't work in the Android app's WebView | Spike in M2 before building UI; three delivery options; playlists still work natively |
| UI-injection plugins break on jellyfin-web updates | Prefer the least invasive option; the hub is self-contained; integrations are optional |
| Wikidata noise, gaps (TV) or outages | Owned-only display for dynamic universes; `instance of` filters; caching with last-good fallback; curated registry for flagship universes |
| Chronology can't be derived dynamically | Registry plus user list URLs; release-order fallback; "not placed" report |
| Season numbering mismatches | Admin report; per-entry overrides |
| Jellyfin renames or merges our collections | Collections are optional; distinctive names, locked fields, stripped TMDB IDs |
| Playlists or collections lost on upgrade (#18101) | Everything is derived; the refresh task is idempotent |
| Jellyfin major-version API churn | Thin adapter around Jellyfin APIs; per-major builds |
| Registry data rot | Freshness Action; Wikidata surfaces new releases automatically |
| Contested chronology | Named orders with `basis`; flags and filters; user overrides |

---

## 10. Open questions

1. **Show unowned titles greyed out in curated hubs by default?** Proposed: yes for curated universes, no for dynamic ones.
2. **License:** GPL-3.0 for the code and CC BY-SA 4.0 for the registry data. OK?
