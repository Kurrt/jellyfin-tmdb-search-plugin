using System.Collections.Concurrent;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Remembers which TMDB title a deterministic search-stub GUID stands in for, so a later
/// direct item lookup on that same GUID can be redirected to the real library item once one
/// exists — instead of 404ing forever on an id that was only ever meant for one search result.
/// </summary>
/// <remarks>
/// <para>
/// Search results returned by <see cref="TmdbSearchActionFilter"/> for a title you do not yet
/// own carry a stub GUID (<see cref="StremioGuidHelper.ToGuid"/>) rather than a real Jellyfin
/// item id. Clicking into that result opens the details page at that stub GUID, and Gelato
/// materializes the title into a real library item — under a <em>different</em> id — as a side
/// effect of that details-page visit. Nothing tells the already-open page to swap ids, so its
/// own item-detail fetch (<c>GetItemById</c>) keeps polling the now-dead stub GUID and 404s
/// indefinitely, even though the title is now perfectly playable under its real id.
/// </para>
/// <para>
/// This registry closes that gap: it records stub GUID → TMDB id at the moment the stub is
/// minted, so <see cref="TmdbItemLookupActionFilter"/> can resolve a stale stub GUID back to
/// its TMDB id, look that up in <see cref="TmdbLibraryIndex"/> (which is kept fresh by
/// Jellyfin's own library events, not by search traffic), and rewrite the request onto the
/// real item transparently.
/// </para>
/// </remarks>
public sealed class TmdbStubRegistry
{
    /// <summary>
    /// Upper bound on retained stub mappings. Every unowned search result mints a stub, so an
    /// unbounded map would grow for the lifetime of the server. Entries are tiny (a GUID plus
    /// 8 bytes), so this cap costs well under a megabyte while comfortably covering the stubs
    /// a user could still have open in a details page.
    /// </summary>
    private const int MaxEntries = 20_000;

    private readonly ConcurrentDictionary<Guid, (BaseItemKind Kind, int TmdbId)> _stubs = new();
    private readonly ConcurrentQueue<Guid> _insertionOrder = new();

    /// <summary>
    /// Records that <paramref name="stubId"/> was minted for the given TMDB title.
    /// </summary>
    /// <param name="stubId">The deterministic stub GUID handed to the client.</param>
    /// <param name="kind">Movie or series, matching <see cref="TmdbLibraryIndex"/>'s keying.</param>
    /// <param name="tmdbId">TMDB numeric id.</param>
    public void Register(Guid stubId, BaseItemKind kind, int tmdbId)
    {
        // Only queue genuinely new keys, so repeat searches for the same title don't inflate
        // the eviction queue with duplicates.
        if (_stubs.TryAdd(stubId, (kind, tmdbId)))
        {
            _insertionOrder.Enqueue(stubId);
        }
        else
        {
            _stubs[stubId] = (kind, tmdbId);
        }

        while (_stubs.Count > MaxEntries && _insertionOrder.TryDequeue(out var oldest))
        {
            _stubs.TryRemove(oldest, out _);
        }
    }

    /// <summary>
    /// Tries to resolve a previously-minted stub GUID back to its TMDB title.
    /// </summary>
    /// <param name="stubId">The stub GUID from an incoming request.</param>
    /// <param name="kind">The resolved media kind when found.</param>
    /// <param name="tmdbId">The resolved TMDB id when found.</param>
    /// <returns>True when <paramref name="stubId"/> is a known stub.</returns>
    public bool TryGetTmdbId(Guid stubId, out BaseItemKind kind, out int tmdbId)
    {
        if (_stubs.TryGetValue(stubId, out var entry))
        {
            kind = entry.Kind;
            tmdbId = entry.TmdbId;
            return true;
        }

        kind = default;
        tmdbId = 0;
        return false;
    }
}
