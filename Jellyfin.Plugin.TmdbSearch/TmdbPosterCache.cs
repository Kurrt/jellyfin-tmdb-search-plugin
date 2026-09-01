using System.Collections.Concurrent;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// In-memory map of search-stub item ids to TMDB poster URLs for image proxying.
/// </summary>
public sealed class TmdbPosterCache
{
    /// <summary>
    /// Default lifetime matching Gelato's search-meta cache.
    /// </summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<Guid, CacheEntry> _entries = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbPosterCache"/> class.
    /// </summary>
    /// <param name="timeProvider">Clock used for TTL expiry. Defaults to the system clock.</param>
    /// <param name="ttl">How long poster URLs remain fetchable after search.</param>
    public TmdbPosterCache(TimeProvider? timeProvider = null, TimeSpan? ttl = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ttl = ttl ?? DefaultTtl;
    }

    /// <summary>
    /// Stores a poster URL for a search stub when it points at the TMDB image CDN.
    /// </summary>
    /// <param name="itemId">Synthetic search item id.</param>
    /// <param name="posterUrl">Absolute TMDB poster URL.</param>
    public void Set(Guid itemId, string? posterUrl)
    {
        if (posterUrl is null || !IsAllowedTmdbImageUrl(posterUrl))
        {
            return;
        }

        _entries[itemId] = new CacheEntry(posterUrl, _timeProvider.GetUtcNow().Add(_ttl));
    }

    /// <summary>
    /// Looks up a still-valid poster URL for a search stub.
    /// </summary>
    /// <param name="itemId">Synthetic search item id.</param>
    /// <param name="posterUrl">The cached TMDB URL when found.</param>
    /// <returns>True when a non-expired TMDB poster URL is cached.</returns>
    public bool TryGet(Guid itemId, out string posterUrl)
    {
        if (_entries.TryGetValue(itemId, out var entry) && entry.ExpiresAt > _timeProvider.GetUtcNow())
        {
            posterUrl = entry.Url;
            return true;
        }

        _entries.TryRemove(itemId, out _);
        posterUrl = string.Empty;
        return false;
    }

    /// <summary>
    /// Returns true when <paramref name="url"/> is an https TMDB CDN image URL.
    /// </summary>
    /// <param name="url">Candidate poster URL.</param>
    /// <returns>True for https://image.tmdb.org/... URLs.</returns>
    public static bool IsAllowedTmdbImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, "image.tmdb.org", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CacheEntry(string Url, DateTimeOffset ExpiresAt);
}
