# Field report: raw evidence behind the two fixes

Diagnostic notes gathered read-only against a live server, kept as the evidence trail for
the two bugs fixed in this branch. Hostnames, user ids and absolute config paths have been
genericised; log excerpts are otherwise verbatim.

**Environment**

| | |
|---|---|
| Jellyfin | 10.11.11 |
| Gelato | 0.26.16.0 |
| TMDB Search | 1.0.5.0, then 1.0.6.0 |
| Meilisearch | not installed on this instance |
| Library size | 3319 movies/series carrying TMDB provider ids |
| Client | Jellyfin Web, Chrome 151, macOS |

Jellyfin logs local time (+10:00 here); the reverse proxy logs UTC. Timestamps below are
labelled where both appear, because mixing them makes unrelated events look causal.

---

## Bug 1 — captive dependency: `GET /Items` returns 500

### Full exception

```
[ERR] Jellyfin.Api.Middleware.ExceptionMiddleware: Error processing request. URL "GET" "/Items".
System.ObjectDisposedException: Cannot access a disposed object.
Object name: 'IServiceProvider'.
   at Microsoft.Extensions.DependencyInjection.ServiceLookup.ThrowHelper.ThrowObjectDisposedException()
   at Microsoft.Extensions.DependencyInjection.ServiceLookup.ServiceProviderEngineScope.GetService(Type serviceType)
   at lambda_method12924(Closure, IServiceProvider, Object[])
   at Microsoft.EntityFrameworkCore.Internal.DbContextPool`1.<>c__DisplayClass6_0.<CreateActivator>b__2()
   at Microsoft.EntityFrameworkCore.Internal.DbContextPool`1.Rent()
   at Microsoft.EntityFrameworkCore.Infrastructure.PooledDbContextFactory`1.CreateDbContext()
   at Jellyfin.Server.Implementations.Item.ChapterRepository.GetChapters(Guid baseItemId)
   at Emby.Server.Implementations.Chapters.ChapterManager.GetChapters(Guid baseItemId)
   at Emby.Server.Implementations.Dto.DtoService.AttachBasicFields(BaseItemDto dto, BaseItem item, BaseItem owner, DtoOptions options)
   at Emby.Server.Implementations.Dto.DtoService.GetBaseItemDtoInternal(BaseItem item, DtoOptions options, User user, BaseItem owner)
   at Emby.Server.Implementations.Dto.DtoService.GetBaseItemDto(BaseItem item, DtoOptions options, User user, BaseItem owner)
   at Gelato.Decorators.DtoServiceDecorator.GetBaseItemDto(BaseItem item, DtoOptions options, User user, BaseItem owner)
   at Jellyfin.Plugin.TmdbSearch.TmdbSearchActionFilter.BuildResultDtos(IReadOnlyList`1 hits)
   at Jellyfin.Plugin.TmdbSearch.TmdbSearchActionFilter.OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
   at Microsoft.AspNetCore.Mvc.Infrastructure.ControllerActionInvoker.<InvokeNextActionFilterAsync>g__Awaited|10_0(...)
```

### Frequency and impact

- **148** `ObjectDisposedException` occurrences in a single day's log.
- Surfaces to clients as HTTP 500 on `GET /Items` — i.e. search returns nothing.
- The plugin's own success line, `TMDB search "<query>" types=[...] start=... limit=...`,
  appears **zero times** across the whole log. Grepping for it returns nothing at all.

Every search attempt on this host ended in one of three states, never success:

```
[WRN] TmdbSearchActionFilter: TMDB search passthrough for ""hulk"": no API key configured
[INF] Plugin: TMDB Search configuration updated (api key configured: True)
[WRN] TmdbClient: TMDB search failed for query "hulk"
[WRN] TmdbSearchActionFilter: TMDB search passthrough for ""hulk"": TMDB request failed or timed out
[ERR] ExceptionMiddleware: Error processing request. URL "GET" "/Items".   <- ObjectDisposedException
```

Note the library index itself warms fine, which is what makes the failure look confusing
at first glance — the plugin appears healthy at startup:

```
[INF] TmdbLibraryIndexHostedService: TMDB Search indexed 3319 library movies/series with TMDB provider ids
```

### Diagnosis

`TmdbSearchActionFilter` is registered `AddSingleton` in `PluginServiceRegistrator`, but its
constructor takes `IDtoService` and `ILibraryManager`, which are request-scoped. The
singleton captures the first request's scope; once that scope is disposed, every later call
into `DtoService` → `ChapterRepository` → `PooledDbContextFactory` throws. Classic captive
dependency.

`Gelato.Decorators.DtoServiceDecorator` appears in the trace only because Gelato decorates
`IDtoService` on this host — it is not implicated in the fault. The same bug should
reproduce without Gelato installed.

### Reproducing

1. Install the plugin, configure a valid TMDB API key.
2. Search for anything that returns movie/series results — twice.
3. First search may succeed; subsequent ones 500 with the trace above.

### Verifying the fix

`GET /Items?searchTerm=matrix` repeatedly (5+ times) should return 200 every time, and the
log should show one `TMDB search "matrix" types=[...]` line per request.

---

## Bug 2 — stale stub ids 404 forever on the details page

### Symptom

Open the details page for a catalog title you do not own. The page renders, and the
Recommended / Similar rails populate — but the title's own poster, description and metadata
never appear. It never recovers on its own.

Those rails populate because they come from a different code path entirely; only the
item's own detail fetch is broken.

### Client-side evidence

Console, repeating:

```
Failed to load resource: the server responded with a status of 404 ()
  @ /Users/<user-id>/Items/2857010cfd52cef4084e15ec299af5fa
failed to get item or current user: Response
  @ /web/itemDetails.<hash>.chunk.js
```

The stub id above is the deterministic MD5 of `stremio://movie/tmdb:<id>` — reproducible,
not sensitive.

### Proxy access log (UTC) — the moment it flips

```
10:56:36.221  200  GET /Items/2857010c.../Images/Primary?...    <- poster works throughout
10:56:42.749  200  GET /Users/<user-id>/Items/2857010c...       <- succeeds once
10:56:42.887  200  GET /Items/2857010c...?userId=<user-id>
10:56:46.220  404  GET /Users/<user-id>/Items/2857010c...       <- flips, 4s later
10:57:41.853  404  GET /Users/<user-id>/Items/2857010c...
10:58:00.073  404  GET /Users/<user-id>/Items/2857010c...
   ... same request every 20-40s ...
11:11:00.074  404  GET /Users/<user-id>/Items/2857010c...       <- still failing 15 min later
```

The client's own retry loop ran for over fifteen minutes without once recovering. Note
`/Images/Primary` for the *same* id keeps returning 200 — image requests are served through
a path that does not require the real library item to exist, which is why the poster can
appear while the metadata never does.

### Server log (local +10:00) — the cause

```
20:56:35.494  Gelato.Filters.SearchActionFilter: Intercepted /Items search ""spy kids"" types=["Movie,Series"] start=0 limit=800 results=8
                 ^ client receives the stub id here
20:56:44.746  Gelato.Filters.InsertActionFilter: inserted new media: "Spy Kids: Armageddon"
                 ^ materialized into the real library, under a DIFFERENT id,
                   triggered by the details-page visit itself
20:56:46.072  Gelato.GelatoManager: SyncStreams finished GelatoId=tt13978520 userId=<user-id> duration=1.3s streams=3
```

Grepping the entire day's log for the stub id `2857010c` returns **only those two lines** —
the search that minted it and the insert. Nothing ever references it again, including no
attempted redirect.

### The contrast that pins it

Gelato *does* have a redirect path, and it works — six minutes earlier, same log, a
different title:

```
20:55:11.204  SearchActionFilter: Intercepted /Items search ""back to the future"" ... results=26
20:55:17.342  InsertActionFilter: Media already exists; redirecting to canonical id 177e5ef8-...
20:55:17.355  InsertActionFilter: Media already exists; redirecting to canonical id 177e5ef8-...
20:55:18.933  InsertActionFilter: Media already exists; redirecting to canonical id 177e5ef8-...
```

Three stale-id requests, all correctly redirected. Across the day, the same redirect fires
for many ids (`d930bb82`, `6e18f337`, `e4008669`, `0265c220`, `682b19c0`, …) — and in every
observed case a `SearchActionFilter` line precedes it by a few seconds.

That is the pattern: **the redirect only fires for requests that follow a fresh search hit
for that title.** The stub → canonical mapping is populated by the search pass, not held
durably. A title materialized by a *details-page visit* — with no subsequent search — never
gets its mapping registered, so every later `GetItemById` on the stub falls through to core
Jellyfin, which has legitimately never heard of that id.

This is also why "search again and click it" is the folk remedy: re-searching is the only
thing that repopulates the mapping. A plain reload of the stale URL does nothing.

### Why the fix lives here rather than in Gelato

This plugin already maintains `TmdbLibraryIndex` — TMDB id → real library item id, kept
current by Jellyfin's own `ItemAdded`/`ItemUpdated`/`ItemRemoved` events. That index is
correct regardless of search traffic and needs nothing from Gelato's internals. It was
simply never consulted on the failing endpoint. The fix records stub → TMDB id at mint time
and consults the index on `GetItemById`.

### Reproducing

1. Search for a title in the Gelato catalog that is **not** yet in the library.
2. Click straight into it from the search results — do not reload, do not search again.
3. Watch for `InsertActionFilter: inserted new media: "..."` in the server log.
4. The details page's poster/title/description stay blank; `/Users/{userId}/Items/{stubId}`
   404s on a loop.

### Verifying the fix

Same steps — the details page should populate within a second or two of the insert landing,
with a log line:

```
TMDB Search: stub <stub-id> is now owned as <real-id> — rewriting item lookup
```

---

## Not investigated

- Whether `/Items/{id}/Similar`, `/ThemeMedia` and `PlaybackInfo` also 404 on a stale stub.
  They are called with the same id by the details page, so they plausibly do; the fix was
  deliberately not widened to them without a reproduction proving it.
- Whether playback (`PlaybackInfo` → stream selection) has an equivalent stale-id window.
  A separate investigation on this host found unrelated playback failures with distinct
  root causes (bad release metadata, and genuinely missing content at the Usenet provider),
  so playback issues on a Gelato stack should not be assumed to share this cause.
