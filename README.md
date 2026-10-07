# Chrono for Jellyfin

Disney+-style universe hubs for Jellyfin. Browse the Marvel Cinematic Universe, Star Wars or any other connected franchise as rows of **Complete Timeline**, **Official Timeline**, **Movie Timeline**, **Sagas**, **Phases** and **Release Order**, with series placed season by season next to the films.

* **Hub in the web client and mobile apps.** A *Universes* entry in the menu and on the home screen opens a universe gallery and per-universe hubs: hero, *Continue* button, filter chips (e.g. hide Netflix/ABC shows), phase/era pages, watched and progress badges.
* **Season-level timelines.** Loki S1 sits after Endgame, Agents of S.H.I.E.L.D. seasons sit where they happen. Episodes are never shown individually.
* **Play in order.** *Continue* and *Play from here* queue the rest of the timeline on the device you are using (seasons expand to episodes, partly watched seasons resume at the first unwatched episode).
* **Missing titles.** Titles you don't own appear greyed out with their poster. With Seerr/Jellyseerr connected, users can request a movie or a single season straight from the hub.
* **Any universe.** Curated MCU and Star Wars data ships with the plugin. Build more universes in the dashboard from a Wikidata franchise, a TMDB collection in your library, or an MDBList/Trakt list (for a custom chronological order). The plugin also suggests universes it finds in your library.
* **Works on every client, too.** Optional public playlists (play in order anywhere) and collections (one per row, in exact order) for clients that can't show the hub, such as Android TV.

## Requirements

* Jellyfin **12.0 or newer** (built against 12.2, .NET 10).
* The hub runs in jellyfin-web, so it works in the browser, Jellyfin Media Player/Desktop and the Android and iOS mobile apps. Native clients (Android TV, Swiftfin, Findroid, Kodi, Roku) only see the optional playlists and collections.

## Install

1. Dashboard → Plugins → Repositories → add
   `https://raw.githubusercontent.com/JiiimmyyN/jellyfin-chrono/main/manifest.json`
2. Install **Chrono** from the catalog and restart Jellyfin.
3. Reload the web client. *Universes* appears in the navigation and on the home screen.

Manual install: unzip the release into `<config>/plugins/Chrono_<version>/` and restart.

## Configure

Dashboard → Plugins → Chrono:

* **Universes:** enable/disable universes, show or hide titles you don't own, include owned titles Wikidata links to a universe that aren't curated yet, create playlists (pick the rows) and collections, hide flagged groups (e.g. Marvel Television) for everyone, and see which titles are missing.
* **Add a universe:** pick a suggestion, or combine sources:
  * *Wikidata franchise* (`Q`-id, use the search box): every film and series Wikidata links to the franchise, release order, plus sub-series rows (e.g. "Star Wars original trilogy").
  * *TMDB collection* id: the movies of that collection in your library.
  * *MDBList list URL*: a ranked list, kept in its order (movies and whole series).
  * *Trakt list URL*: a ranked list including seasons (requires your own Trakt client id; Trakt only issues API apps to VIP accounts).

  The first ranked list becomes the main timeline. Owned series with several seasons are split into seasons automatically.
* **Requests:** Seerr/Jellyseerr URL and API key. Requests are made as the Seerr user linked to the Jellyfin account (falls back to the API key owner).
* **Advanced:** registry URLs, refresh intervals, Trakt client id, web client injection and menu link.

The *Refresh universes* scheduled task (daily) re-downloads definitions and rebuilds playlists and collections; Chrono also refreshes after library scans and configuration changes.

## Universe data

Curated universes live in [`registry/`](registry/): one JSON file per universe, validated by [`registry/schema/universe.schema.json`](registry/schema/universe.schema.json). The plugin downloads the registry at runtime (falling back to the copy bundled in the plugin), so data updates don't need a plugin release. Add extra registries in the settings, or drop files into `<config>/plugins/Jellyfin.Plugin.Chrono/universes/*.json` to add or override universes locally.

A universe has **entries** (movies, series or seasons keyed by TMDB id), **groups** (phases, sagas, eras), **flags** (filterable categories) and **orders**. Orders are either an explicit list (timelines) or derived (`release` = sorted by date, `phase-1` = group filter, `movie-timeline` = the timeline filtered to movies). `discover` lists Wikidata anchors used to pick up new releases automatically, and `exclude` the titles curators rejected.

Sources and licences:

* MCU: [marvel.com Complete MCU Timeline](https://www.marvel.com/articles/movies/mcu-timeline-order-disney-plus) (Disney+ order), network shows placed with the [MCU Wiki](https://marvelcinematicuniverse.fandom.com/) viewing timeline.
* Star Wars: [StarWars.com viewing guide](https://www.starwars.com/news/star-wars-movies-and-series-guide) and eras, split per season with [Wookieepedia](https://starwars.fandom.com/).
* IDs and phases: [Wikidata](https://www.wikidata.org/) (CC0), season dates: [TVmaze](https://www.tvmaze.com/), posters: [TMDB](https://www.themoviedb.org/).

The registry data is licensed [CC BY-SA 4.0](registry/LICENSE). Posters of titles you don't own are loaded from TMDB's image CDN; Chrono is not endorsed or certified by TMDB.

### Maintaining the registry

```bash
dotnet run --project tools/Chrono.RegistryTool -- validate registry
```

```bash
dotnet run --project tools/Chrono.RegistryTool -- discover registry
```

`discover` lists titles Wikidata links to a universe that are neither curated nor excluded. A weekly GitHub Action keeps an issue with that list up to date.

## Development

```
src/Jellyfin.Plugin.Chrono   server plugin (C#, .NET 10)
web/                         hub client (SolidJS + TypeScript + Vite), embedded into the plugin
registry/                    curated universe data + JSON schema
tools/Chrono.RegistryTool    registry validation and discovery
tests/                       xUnit tests
dev/                         local Jellyfin 12.2 (+ optional Seerr) with a generated fake library
docs/                        plan and API contract
```

Run the tests:

```bash
dotnet test tests/Jellyfin.Plugin.Chrono.Tests
```

```bash
cd web && pnpm install && pnpm test
```

Hub UI with mock data (no server needed):

```bash
cd web && pnpm dev
```

Local Jellyfin with a fake library of tiny clips named after the registry titles:

```bash
dotnet run dev/make-library.cs -- dev/media registry/universes 70
```

```bash
docker compose -f dev/docker-compose.yml up -d && dev/setup.sh
```

```bash
dev/deploy.sh
```

`setup.sh` completes the startup wizard with the test account from `dev/dev.env.example` and adds the libraries; `deploy.sh` builds the web client and plugin, copies it into the dev server and restarts it. Add `--profile seerr` to the compose command to start Seerr on port 5055 for request testing.

Build a release zip:

```bash
scripts/package.sh 0.1.0.0
```

Releases are built by GitHub Actions when a `v*` tag is pushed; the workflow attaches the zip to the release and updates `manifest.json`.

## License

Plugin code: [GPL-3.0](LICENSE). Registry data: [CC BY-SA 4.0](registry/LICENSE).
