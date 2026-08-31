# TMDB Search for Jellyfin

Replace Jellyfin **Items search** with direct [TMDB](https://www.themoviedb.org/) lookup. Works with every client that uses the standard `/Items?SearchTerm=` API (web, Android, iOS, Infuse, Swiftfin, etc.).

## What it does

- Intercepts movie/series search and queries TMDB directly (fast, Remux-style discovery).
- If you already have the title in your library (matched by TMDB id), returns the real Jellyfin item.
- If you do not, returns a TMDB result stub and seeds [Gelato](https://github.com/lostb1t/Gelato) so click-to-insert/playback still works.
- Prefix a query with `local:` to use native Jellyfin search instead (music, people, or local-only lookup).

## Requirements

- Jellyfin **10.11.x**
- A [TMDB API key](https://www.themoviedb.org/settings/api) (v3)
- [Gelato](https://github.com/lostb1t/Gelato) for streaming titles you do not already own

## Install

### From the Jellyfin plugin catalog (recommended)

Add this repository once, then install from the catalog like any official plugin.

**Repository URL:**

```
https://raw.githubusercontent.com/Kurrt/jellyfin-tmdb-search-plugin/main/manifest.json
```

1. Open **Dashboard → Plugins → Repositories**.
2. Click **+** (Add).
3. Enter any name (e.g. `TMDB Search`).
4. Paste the repository URL above into **Repository URL**.
5. Click **Save**.
6. Open **Dashboard → Plugins → Catalog**.
7. Find **TMDB Search** and click **Install**.
8. Restart Jellyfin.

Future updates appear in the catalog automatically after you refresh repositories.

### Manual install (release zip)

1. Download the latest `jellyfin-plugin-tmdbsearch_*.zip` from [Releases](https://github.com/Kurrt/jellyfin-tmdb-search-plugin/releases).
2. Extract into your Jellyfin plugins folder:
   - Docker/Linux: `/config/plugins/TMDB Search/`
   - macOS: `~/.local/share/jellyfin/plugins/TMDB Search/`
   - Windows: `%AppData%\Local\jellyfin\plugins\TMDB Search\`
3. Restart Jellyfin.

### From source

```bash
dotnet build -c Release
cp Jellyfin.Plugin.TmdbSearch/bin/Release/net9.0/Jellyfin.Plugin.TmdbSearch.dll \
   /path/to/jellyfin/plugins/TMDB\ Search/
```

Restart Jellyfin after copying the DLL.

## Configure

1. Open **Dashboard → Plugins → TMDB Search**.
2. Paste your TMDB API key and click **Save**.
3. Restart Jellyfin if search does not pick up the key immediately.

Optional settings:

| Setting | Default | Description |
|---------|---------|-------------|
| Language | server default | TMDB language code (e.g. `en-US`) |
| Include adult | off | Include adult TMDB results |
| Cache TTL | 600 | Seconds to cache identical search queries in memory |

## Gelato setup

This plugin replaces search only. Gelato still handles insert and playback for titles you do not own.

1. Install and configure Gelato as usual (AIOStreams manifest, library paths, etc.).
2. In Gelato settings, **disable Gelato search** so TMDB handles discovery.
3. Search for a movie or show, click a result you do not own, and Gelato will materialize it on demand.

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Plugin not in catalog | Add the [repository URL](#from-the-jellyfin-plugin-catalog-recommended) under **Plugins → Repositories**, then check **Catalog** again |
| Install fails checksum error | The manifest may be out of date with the release zip — use [manual install](#manual-install-release-zip) instead |
| No TMDB results | Check API key on the plugin config page; confirm Jellyfin was restarted after install; check server logs for `TMDB search passthrough` |
| Empty results for valid titles | TMDB may be unreachable; plugin falls back to native Jellyfin search |
| Click on unowned title 404s | Gelato must be installed and configured; check Gelato logs |
| Want local/library-only search | Prefix query with `local:` (e.g. `local: matrix`) |

## Build and test

```bash
dotnet build
dotnet test
```

### Releasing

1. Bump `Version` in `Jellyfin.Plugin.TmdbSearch.csproj`.
2. Build and package:

   ```bash
   dotnet build -c Release
   zip -j dist/jellyfin-plugin-tmdbsearch_<version>.zip \
     Jellyfin.Plugin.TmdbSearch/bin/Release/net9.0/Jellyfin.Plugin.TmdbSearch.dll
   md5 dist/jellyfin-plugin-tmdbsearch_<version>.zip
   ```

3. Create a GitHub release and upload the zip.
4. Add a new entry to `manifest.json` (`version`, `sourceUrl`, `checksum`, `timestamp`, `changelog`).
5. Push `manifest.json` to `main` so catalog installs pick up the update.

## License

MIT
