using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TmdbSearch.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// HTTP client for TMDB search and external id lookups with in-memory caching.
/// </summary>
public sealed class TmdbClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;
    private readonly ILogger<TmdbClient> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<TmdbSearchHit>>> _searchCache = new();
    private readonly ConcurrentDictionary<int, CacheEntry<string?>> _imdbCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbClient"/> class.
    /// </summary>
    /// <param name="httpClient">Configured HTTP client.</param>
    /// <param name="logger">Logger instance.</param>
    public TmdbClient(HttpClient httpClient, ILogger<TmdbClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Searches TMDB for movies and/or series matching the query.
    /// </summary>
    /// <param name="query">User search text.</param>
    /// <param name="requestedKinds">Item kinds to include.</param>
    /// <param name="config">Plugin configuration.</param>
    /// <param name="language">Resolved TMDB language code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked search hits, or null when TMDB is unavailable.</returns>
    public async Task<IReadOnlyList<TmdbSearchHit>?> SearchAsync(
        string query,
        IReadOnlySet<BaseItemKind> requestedKinds,
        PluginConfiguration config,
        string language,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.TmdbApiKey))
        {
            _logger.LogWarning("TMDB search skipped because no API key is configured");
            return null;
        }

        var cacheKey = BuildSearchCacheKey(query, requestedKinds, config, language);
        if (_searchCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            return cached.Value;
        }

        try
        {
            var tasks = new List<Task<IReadOnlyList<TmdbSearchHit>>>();

            if (requestedKinds.Contains(BaseItemKind.Movie))
            {
                tasks.Add(SearchMoviesAsync(query, config, language, cancellationToken));
            }

            if (requestedKinds.Contains(BaseItemKind.Series))
            {
                tasks.Add(SearchSeriesAsync(query, config, language, cancellationToken));
            }

            var parts = await Task.WhenAll(tasks).ConfigureAwait(false);
            var merged = parts
                .SelectMany(static hits => hits)
                .OrderByDescending(static hit => hit.Popularity)
                .ToList();

            var ttl = TimeSpan.FromSeconds(Math.Max(config.CacheTtlSeconds, 0));
            _searchCache[cacheKey] = new CacheEntry<IReadOnlyList<TmdbSearchHit>>(merged, ttl);
            return merged;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "TMDB search failed for query {Query}", query);
            return null;
        }
    }

    /// <summary>
    /// Fetches the IMDb id for a TMDB movie or series id.
    /// </summary>
    /// <param name="tmdbId">TMDB numeric id.</param>
    /// <param name="kind">Movie or series.</param>
    /// <param name="apiKey">TMDB API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The IMDb id when found.</returns>
    public async Task<string?> GetImdbIdAsync(
        int tmdbId,
        BaseItemKind kind,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        if (_imdbCache.TryGetValue(tmdbId, out var cached) && !cached.IsExpired)
        {
            return cached.Value;
        }

        try
        {
            var segment = kind == BaseItemKind.Movie ? "movie" : "tv";
            var url =
                $"3/{segment}/{tmdbId}/external_ids?api_key={Uri.EscapeDataString(apiKey)}";

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(RequestTimeout);

            var response = await _httpClient
                .GetFromJsonAsync<TmdbExternalIdsResponse>(url, cts.Token)
                .ConfigureAwait(false);

            var imdbId = response?.ImdbId;
            _imdbCache[tmdbId] = new CacheEntry<string?>(imdbId, TimeSpan.FromDays(7));
            return imdbId;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogDebug(ex, "TMDB external_ids lookup failed for {TmdbId}", tmdbId);
            return null;
        }
    }

    private async Task<IReadOnlyList<TmdbSearchHit>> SearchMoviesAsync(
        string query,
        PluginConfiguration config,
        string language,
        CancellationToken cancellationToken)
    {
        var url = BuildSearchUrl("search/movie", query, config, language);
        var response = await GetSearchResponseAsync(url, cancellationToken).ConfigureAwait(false);

        return response.Results
            .Select(row => MapRow(row, BaseItemKind.Movie, row.Title, row.ReleaseDate))
            .Where(static hit => hit is not null)
            .Select(static hit => hit!)
            .ToList();
    }

    private async Task<IReadOnlyList<TmdbSearchHit>> SearchSeriesAsync(
        string query,
        PluginConfiguration config,
        string language,
        CancellationToken cancellationToken)
    {
        var url = BuildSearchUrl("search/tv", query, config, language);
        var response = await GetSearchResponseAsync(url, cancellationToken).ConfigureAwait(false);

        return response.Results
            .Select(row => MapRow(row, BaseItemKind.Series, row.Name, row.FirstAirDate))
            .Where(static hit => hit is not null)
            .Select(static hit => hit!)
            .ToList();
    }

    private async Task<TmdbSearchResponse> GetSearchResponseAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(RequestTimeout);

        var response = await _httpClient
            .GetFromJsonAsync<TmdbSearchResponse>(url, cts.Token)
            .ConfigureAwait(false);

        return response ?? new TmdbSearchResponse();
    }

    private static string BuildSearchUrl(
        string endpoint,
        string query,
        PluginConfiguration config,
        string language)
    {
        var includeAdult = config.IncludeAdult ? "true" : "false";
        return
            $"3/{endpoint}?api_key={Uri.EscapeDataString(config.TmdbApiKey)}"
            + $"&query={Uri.EscapeDataString(query)}"
            + $"&include_adult={includeAdult}"
            + $"&language={Uri.EscapeDataString(language)}";
    }

    private static string BuildSearchCacheKey(
        string query,
        IReadOnlySet<BaseItemKind> requestedKinds,
        PluginConfiguration config,
        string language)
    {
        var kinds = string.Join(',', requestedKinds.OrderBy(static k => k));
        return $"{language}|{config.IncludeAdult}|{kinds}|{query}";
    }

    private static TmdbSearchHit? MapRow(
        TmdbSearchResultRow row,
        BaseItemKind kind,
        string? title,
        string? date)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new TmdbSearchHit(
            row.Id,
            kind,
            title,
            ParseYear(date),
            row.PosterPath,
            row.Overview,
            row.Popularity);
    }

    private static int? ParseYear(string? date)
    {
        if (string.IsNullOrWhiteSpace(date) || date.Length < 4)
        {
            return null;
        }

        return int.TryParse(date.AsSpan(0, 4), out var year) ? year : null;
    }

    private sealed class CacheEntry<T>
    {
        public CacheEntry(T value, TimeSpan ttl)
        {
            Value = value;
            ExpiresAt = DateTimeOffset.UtcNow.Add(ttl);
        }

        public T Value { get; }

        public DateTimeOffset ExpiresAt { get; }

        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
