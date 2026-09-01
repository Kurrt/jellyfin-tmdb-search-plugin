using System.Collections.Concurrent;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// In-memory map of search-stub item ids to DTOs and TMDB poster URLs.
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
    /// <param name="ttl">How long stubs remain fetchable after search.</param>
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
    public void Set(Guid itemId, string? posterUrl) => Set(itemId, dto: null, posterUrl);

    /// <summary>
    /// Stores a search stub so later GetItem and image requests can be served without Gelato.
    /// </summary>
    /// <param name="itemId">Synthetic search item id.</param>
    /// <param name="dto">Search result DTO returned to clients.</param>
    /// <param name="posterUrl">Absolute TMDB poster URL when known.</param>
    public void Set(Guid itemId, BaseItemDto? dto, string? posterUrl)
    {
        var allowedPoster = IsAllowedTmdbImageUrl(posterUrl) ? posterUrl : null;
        if (dto is null && allowedPoster is null)
        {
            return;
        }

        _entries[itemId] = new CacheEntry(allowedPoster, dto, _timeProvider.GetUtcNow().Add(_ttl));
    }

    /// <summary>
    /// Looks up a still-valid poster URL for a search stub.
    /// </summary>
    /// <param name="itemId">Synthetic search item id.</param>
    /// <param name="posterUrl">The cached TMDB URL when found.</param>
    /// <returns>True when a non-expired TMDB poster URL is cached.</returns>
    public bool TryGet(Guid itemId, out string posterUrl)
    {
        if (TryGetEntry(itemId, out var entry) && entry.Url is not null)
        {
            posterUrl = entry.Url;
            return true;
        }

        posterUrl = string.Empty;
        return false;
    }

    /// <summary>
    /// Looks up a still-valid search stub DTO for item detail requests.
    /// </summary>
    /// <param name="itemId">Synthetic search item id.</param>
    /// <param name="dto">The cached DTO when found.</param>
    /// <returns>True when a non-expired stub DTO is cached.</returns>
    public bool TryGetDto(Guid itemId, out BaseItemDto dto)
    {
        if (TryGetEntry(itemId, out var entry) && entry.Dto is not null)
        {
            dto = entry.Dto;
            return true;
        }

        dto = new BaseItemDto();
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

    private bool TryGetEntry(Guid itemId, out CacheEntry entry)
    {
        if (_entries.TryGetValue(itemId, out var found) && found.ExpiresAt > _timeProvider.GetUtcNow())
        {
            entry = found;
            return true;
        }

        _entries.TryRemove(itemId, out _);
        entry = new CacheEntry(null, null, DateTimeOffset.MinValue);
        return false;
    }

    private sealed record CacheEntry(string? Url, BaseItemDto? Dto, DateTimeOffset ExpiresAt);
}
