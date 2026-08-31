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

### From a release zip

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
| No TMDB results | Check API key on the plugin config page; confirm Jellyfin was restarted after install |
| Empty results for valid titles | TMDB may be unreachable; plugin falls back to native Jellyfin search |
| Click on unowned title 404s | Gelato must be installed and configured; check Gelato logs |
| Want local/library-only search | Prefix query with `local:` (e.g. `local: matrix`) |

## Build and test

```bash
dotnet build
dotnet test
```

## License

MIT
