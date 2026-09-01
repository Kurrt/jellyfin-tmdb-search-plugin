using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// In-memory map of search-stub item ids to DTOs and TMDB poster URLs.
/// Optionally persisted so GetItem still works after a Jellyfin restart.
/// </summary>
public sealed class TmdbPosterCache
{
    /// <summary>
    /// Default lifetime matching Gelato's search-meta cache.
    /// </summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(6);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    private readonly ConcurrentDictionary<Guid, CacheEntry> _entries = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;
    private readonly string? _persistPath;
    private readonly ILogger<TmdbPosterCache>? _logger;
    private readonly Lock _persistLock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbPosterCache"/> class.
    /// </summary>
    /// <param name="timeProvider">Clock used for TTL expiry. Defaults to the system clock.</param>
    /// <param name="ttl">How long stubs remain fetchable after search.</param>
    /// <param name="persistPath">Optional JSON file used to survive process restarts.</param>
    /// <param name="logger">Optional logger for persist failures.</param>
    public TmdbPosterCache(
        TimeProvider? timeProvider = null,
        TimeSpan? ttl = null,
        string? persistPath = null,
        ILogger<TmdbPosterCache>? logger = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ttl = ttl ?? DefaultTtl;
        _persistPath = persistPath;
        _logger = logger;
        LoadFromDisk();
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
        SaveToDisk();
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

    private static PersistedEntry ToPersisted(Guid itemId, CacheEntry entry) =>
        new()
        {
            Id = itemId,
            PosterUrl = entry.Url,
            Name = entry.Dto?.Name,
            Overview = entry.Dto?.Overview,
            ProductionYear = entry.Dto?.ProductionYear,
            Type = entry.Dto?.Type.ToString(),
            TmdbId = TryGetTmdbId(entry.Dto),
            ServerId = entry.Dto?.ServerId,
            ExpiresAt = entry.ExpiresAt,
        };

    private static string? TryGetTmdbId(BaseItemDto? dto)
    {
        if (dto?.ProviderIds is null)
        {
            return null;
        }

        return dto.ProviderIds.TryGetValue(MetadataProvider.Tmdb.ToString(), out var tmdbId)
            ? tmdbId
            : null;
    }

    private static BaseItemDto? ToDto(PersistedEntry row)
    {
        if (int.TryParse(row.TmdbId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tmdbId)
            && Enum.TryParse(row.Type, ignoreCase: true, out BaseItemKind kind)
            && !string.IsNullOrWhiteSpace(row.Name)
            && kind is BaseItemKind.Movie or BaseItemKind.Series)
        {
            var posterPath = TryGetPosterPath(row.PosterUrl);
            var hit = new TmdbSearchHit(
                tmdbId,
                kind,
                row.Name,
                row.ProductionYear,
                posterPath,
                row.Overview,
                0);
            return SearchResultDtoBuilder.CreateStub(hit, row.ServerId).Dto;
        }

        if (string.IsNullOrWhiteSpace(row.Name) || row.Id == Guid.Empty)
        {
            return null;
        }

        return new BaseItemDto
        {
            Id = row.Id,
            Name = row.Name,
            Overview = row.Overview,
            ProductionYear = row.ProductionYear,
            ServerId = row.ServerId,
        };
    }

    private static string? TryGetPosterPath(string? posterUrl)
    {
        if (string.IsNullOrWhiteSpace(posterUrl)
            || !posterUrl.StartsWith(TmdbSearchMapper.PosterImageBase, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return posterUrl[TmdbSearchMapper.PosterImageBase.Length..];
    }

    private void LoadFromDisk()
    {
        if (string.IsNullOrWhiteSpace(_persistPath) || !File.Exists(_persistPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_persistPath);
            var rows = JsonSerializer.Deserialize<List<PersistedEntry>>(json, JsonOptions);
            if (rows is null)
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            foreach (var row in rows)
            {
                if (row.Id == Guid.Empty || row.ExpiresAt <= now)
                {
                    continue;
                }

                var poster = IsAllowedTmdbImageUrl(row.PosterUrl) ? row.PosterUrl : null;
                var dto = ToDto(row);
                if (dto is null && poster is null)
                {
                    continue;
                }

                _entries[row.Id] = new CacheEntry(poster, dto, row.ExpiresAt);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to load TMDB search stub cache from {Path}", _persistPath);
        }
    }

    private void SaveToDisk()
    {
        if (string.IsNullOrWhiteSpace(_persistPath))
        {
            return;
        }

        lock (_persistLock)
        {
            try
            {
                var directory = Path.GetDirectoryName(_persistPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var now = _timeProvider.GetUtcNow();
                var rows = _entries
                    .Where(pair => pair.Value.ExpiresAt > now)
                    .Select(pair => ToPersisted(pair.Key, pair.Value))
                    .ToList();

                var json = JsonSerializer.Serialize(rows, JsonOptions);
                var tempPath = $"{_persistPath}.tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _persistPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Failed to persist TMDB search stub cache to {Path}", _persistPath);
            }
        }
    }

    private sealed record CacheEntry(string? Url, BaseItemDto? Dto, DateTimeOffset ExpiresAt);

    private sealed class PersistedEntry
    {
        public Guid Id { get; set; }

        public string? PosterUrl { get; set; }

        public string? Name { get; set; }

        public string? Overview { get; set; }

        public int? ProductionYear { get; set; }

        public string? Type { get; set; }

        public string? TmdbId { get; set; }

        public string? ServerId { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }
    }
}
