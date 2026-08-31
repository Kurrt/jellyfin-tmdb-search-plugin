using System.Collections.Concurrent;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TmdbSearch.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// HTTP client for Remux-style TMDB movie and TV search with in-memory caching.
/// </summary>
public sealed class TmdbClient
{
    /// <summary>
    /// Overall TMDB HTTP timeout. Search must fail fast instead of stalling clients.
    /// </summary>
    public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// TCP connect timeout so hung IPv6 routes to TMDB cannot consume the full HTTP budget.
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    private readonly HttpClient _httpClient;
    private readonly ILogger<TmdbClient> _logger;
    private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<TmdbSearchHit>>> _searchCache = new();

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
    /// Empty TMDB bodies are a successful empty list. Transport failures also return empty
    /// so the caller never falls through to Gelato/Stremio search.
    /// </summary>
    /// <param name="query">User search text.</param>
    /// <param name="requestedKinds">Item kinds to include.</param>
    /// <param name="config">Plugin configuration.</param>
    /// <param name="language">Resolved TMDB language code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked search hits. Empty when TMDB has no matches or is unavailable.</returns>
    public async Task<IReadOnlyList<TmdbSearchHit>> SearchAsync(
        string query,
        IReadOnlySet<BaseItemKind> requestedKinds,
        PluginConfiguration config,
        string language,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.TmdbApiKey))
        {
            _logger.LogWarning("TMDB search skipped because no API key is configured");
            return [];
        }

        var cacheKey = BuildSearchCacheKey(query, requestedKinds, config, language);
        if (_searchCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            return cached.Value;
        }

        var parts = new List<IReadOnlyList<TmdbSearchHit>>();
        var succeeded = false;

        if (requestedKinds.Contains(BaseItemKind.Movie))
        {
            succeeded |= await TryAddSearchPartAsync(
                parts,
                () => SearchMoviesAsync(query, config, language, cancellationToken),
                "movie",
                query,
                cancellationToken).ConfigureAwait(false);
        }

        if (requestedKinds.Contains(BaseItemKind.Series))
        {
            succeeded |= await TryAddSearchPartAsync(
                parts,
                () => SearchSeriesAsync(query, config, language, cancellationToken),
                "series",
                query,
                cancellationToken).ConfigureAwait(false);
        }

        if (!succeeded)
        {
            return [];
        }

        var merged = parts
            .SelectMany(static hits => hits)
            .OrderByDescending(static hit => hit.Popularity)
            .ToList();

        var ttl = TimeSpan.FromSeconds(Math.Max(config.CacheTtlSeconds, 0));
        _searchCache[cacheKey] = new CacheEntry<IReadOnlyList<TmdbSearchHit>>(merged, ttl);
        return merged;
    }

    private async Task<bool> TryAddSearchPartAsync(
        List<IReadOnlyList<TmdbSearchHit>> parts,
        Func<Task<IReadOnlyList<TmdbSearchHit>>> search,
        string kindLabel,
        string query,
        CancellationToken cancellationToken)
    {
        try
        {
            parts.Add(await search().ConfigureAwait(false));
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "TMDB {Kind} search failed for query {Query}", kindLabel, query);
            return false;
        }
    }

    private async Task<IReadOnlyList<TmdbSearchHit>> SearchMoviesAsync(
        string query,
        PluginConfiguration config,
        string language,
        CancellationToken cancellationToken)
    {
        var response = await GetSearchResponseAsync(
            BuildSearchUrl("search/movie", query, config, language),
            cancellationToken).ConfigureAwait(false);

        return MapHits(response, BaseItemKind.Movie);
    }

    private async Task<IReadOnlyList<TmdbSearchHit>> SearchSeriesAsync(
        string query,
        PluginConfiguration config,
        string language,
        CancellationToken cancellationToken)
    {
        var response = await GetSearchResponseAsync(
            BuildSearchUrl("search/tv", query, config, language),
            cancellationToken).ConfigureAwait(false);

        return MapHits(response, BaseItemKind.Series);
    }

    private static List<TmdbSearchHit> MapHits(TmdbSearchResponse response, BaseItemKind kind)
    {
        var hits = new List<TmdbSearchHit>(response.Results.Count);
        foreach (var row in response.Results)
        {
            var hit = TmdbSearchMapper.TryMapRow(row, kind);
            if (hit is not null)
            {
                hits.Add(hit);
            }
        }

        return hits;
    }

    private async Task<TmdbSearchResponse> GetSearchResponseAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var parsed = await JsonSerializer
            .DeserializeAsync<TmdbSearchResponse>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return parsed ?? new TmdbSearchResponse();
    }

    private static string BuildSearchUrl(
        string endpoint,
        string query,
        PluginConfiguration config,
        string language)
    {
        var includeAdult = config.IncludeAdult ? "true" : "false";
        return $"3/{endpoint}?api_key={Uri.EscapeDataString(config.TmdbApiKey)}&query={Uri.EscapeDataString(query)}&include_adult={includeAdult}&language={Uri.EscapeDataString(language)}";
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
