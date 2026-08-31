# Search pipeline: layering, the stub-id lifecycle, and Meilisearch

Written while diagnosing two live faults against Jellyfin 10.11 + Gelato 0.26.16 +
TMDB Search 1.0.6. Documents where this plugin sits relative to Gelato and Meilisearch,
why stub ids go stale, and a proposal for making TMDB discovery and Meilisearch's local
search complement each other instead of one bypassing the other.

## Where the plugins sit

All three hook a `GET /Items` search, but at **different layers**, which is why they
coexist without a DI conflict:

```
GET /Items
  |
  v
MVC action filters        TmdbSearchActionFilter  (this plugin, order 0)
                          Gelato.SearchActionFilter
  |
  v
ItemsController.GetItems
  |
  v
service layer             Gelato.Decorators.DtoServiceDecorator
  |
  v
repository layer          MeilisearchRepositoryDecorator   (services.Replace on IItemRepository)
  |
  v
SQLite
```

Meilisearch swaps out `IItemRepository`. This plugin is an MVC filter sitting far above
it. Nothing overlaps, nothing fights for the same registration.

## The stub-id lifecycle, and why lookups used to 404

Search results for a title you do **not** own carry a deterministic stub GUID
(`StremioGuidHelper.ToGuid`), not a real Jellyfin item id. That stub is a placeholder for
a library row that does not exist yet.

The failure sequence, as captured live:

```
20:56:35  SearchActionFilter: search "spy kids" -> 8 results   (client receives stub id)
20:56:42  GET /Users/{user}/Items/{stub}      -> 200           (details page renders)
20:56:44  InsertActionFilter: inserted new media "Spy Kids: Armageddon"
                                                                (Gelato materializes it --
                                                                 under a NEW, different id)
20:56:46  GET /Users/{user}/Items/{stub}      -> 404
20:57:41 .. 11:11:00  same request, 404 every time, never recovers
```

Opening the details page is itself what triggers materialization. The already-open page
keeps polling the stub id it was given, which now resolves to nothing. It never
self-heals — 15+ minutes of the client's own retries were observed, all 404. Searching
again "fixes" it only because a fresh search re-registers the mapping in Gelato's own
short-lived cache.

**Fix in this PR:** `TmdbStubRegistry` records stub GUID -> TMDB id at mint time, and
`TmdbItemLookupActionFilter` rewrites a `GetItemById` for a known stub onto the real item
via `TmdbLibraryIndex`. That index is maintained by Jellyfin's own `ItemAdded` /
`ItemUpdated` / `ItemRemoved` events, so it is correct regardless of search traffic and
does not depend on Gelato internals.

### Deliberately still open

`GetItemById` is the endpoint that was proven to 404. The details page also calls
`/Items/{id}/Similar`, `/ThemeMedia`, and `PlaybackInfo` with the same id. Those may need
the same treatment, but the filter's action list was not widened speculatively — that
should follow a live reproduction showing they actually fail.

The registry is in-memory, so a server restart re-breaks any page still open on a stub.
The next search repairs it; this seemed an acceptable trade against persisting state.

## Meilisearch: today it is silently bypassed

`TmdbSearchActionFilter` short-circuits by assigning `ctx.Result` before the controller
runs. The request therefore never reaches the repository layer, so
`MeilisearchRepositoryDecorator` is never consulted for movie/series searches.

Consequences on a stack running both:

- Meilisearch's typo tolerance and fuzzy matching over the titles you actually own are
  lost for exactly the queries users run most.
- Meilisearch still serves everything that takes the passthrough branch: `local:`-prefixed
  queries, music, people, and every non-movie/series item type.
- No crash, no error — the capability just quietly stops applying.

## Proposal: merge local + TMDB rather than replace

Let the native pipeline run, then combine:

1. `await next()` — the request flows through the controller into whatever local backend
   is installed, producing local results.
2. Read the resulting `QueryResult<BaseItemDto>` back off `ctx.Result`.
3. Query TMDB concurrently for discovery results.
4. Merge, deduping by TMDB provider id: local/owned items first, then TMDB stubs for
   titles not already present.
5. Replace `ctx.Result` with the merged set.

Why this shape:

- **Each engine does what it is good at.** Meilisearch: fast, typo-tolerant, local. TMDB:
  discovering titles you do not own. Neither emulates the other.
- **Degrades safely.** TMDB timeouts are already observable in the wild; today a timeout
  yields an empty result set, whereas merging still returns local results.
- **No backend detection needed.** Step 1 is "whatever the native pipeline returns" —
  Meilisearch when installed, SQL when not. No coupling, no per-backend config branch.
- **Reduces reliance on `TmdbLibraryIndex` for owned-item matching** in the common path.
  The index remains useful for dedupe and for the stub redirect above.

Costs and open questions:

- One extra local query per search. Cheap — it is the query that would have run anyway.
- **Paging needs design.** `startIndex` / `limit` currently page a single TMDB list;
  combining two result sets makes the mapping non-obvious. Probably: page the local set
  normally and append TMDB stubs only once local results are exhausted.
- Behaviour change to result composition, so it should sit behind a config flag
  (`MergeLocalResults`, default on) with the current short-circuit still reachable.

This is a product decision as much as a technical one, which is why it is written up here
rather than implemented in this PR.

## If short-circuiting is kept instead

The filters still do not conflict, but both this plugin's `TmdbSearchActionFilter` and
Gelato's `SearchActionFilter` target `GetItems`. The README's existing instruction to
disable Gelato search is what keeps that unambiguous, and it should stay.
