using System.Collections.Concurrent;
using System.Globalization;
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
    private readonly ConcurrentDictionary<string, CacheEntry<TmdbTitleDetails>> _detailsCache = new();
    private readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<TmdbEpisodeInfo>>> _seasonCache = new();

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

    /// <summary>
    /// Loads TMDB movie or series details with credits. Failures return null so search-stub
    /// metadata can still paint the details page.
    /// </summary>
    /// <param name="kind">Movie or series.</param>
    /// <param name="tmdbId">TMDB numeric id.</param>
    /// <param name="config">Plugin configuration.</param>
    /// <param name="language">Resolved TMDB language code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Mapped details, or null when TMDB is unavailable or the kind is unsupported.</returns>
    public async Task<TmdbTitleDetails?> GetTitleDetailsAsync(
        BaseItemKind kind,
        int tmdbId,
        PluginConfiguration config,
        string language,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.TmdbApiKey) || tmdbId <= 0)
        {
            return null;
        }

        var endpointKind = kind switch
        {
            BaseItemKind.Movie => "movie",
            BaseItemKind.Series => "tv",
            _ => null,
        };
        if (endpointKind is null)
        {
            return null;
        }

        var cacheKey = $"{language}|{endpointKind}|{tmdbId}";
        if (_detailsCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            return cached.Value;
        }

        try
        {
            var url =
                $"3/{endpointKind}/{tmdbId}?api_key={Uri.EscapeDataString(config.TmdbApiKey)}&language={Uri.EscapeDataString(language)}&append_to_response=credits";
            var parsed = await GetJsonAsync<TmdbTitleDetailsResponse>(url, cancellationToken).ConfigureAwait(false);
            var details = parsed is null ? null : MapTitleDetails(parsed, kind);
            if (details is not null)
            {
                var ttl = TimeSpan.FromSeconds(Math.Max(config.CacheTtlSeconds, 0));
                _detailsCache[cacheKey] = new CacheEntry<TmdbTitleDetails>(details, ttl);
            }

            return details;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "TMDB {Kind} details failed for id {TmdbId}", endpointKind, tmdbId);
            return null;
        }
    }

    /// <summary>
    /// Loads TMDB episodes for one season. Failures return an empty list.
    /// </summary>
    /// <param name="tmdbId">TMDB series id.</param>
    /// <param name="seasonNumber">Season number.</param>
    /// <param name="config">Plugin configuration.</param>
    /// <param name="language">Resolved TMDB language code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Mapped episodes, or empty when TMDB is unavailable.</returns>
    public async Task<IReadOnlyList<TmdbEpisodeInfo>> GetSeasonEpisodesAsync(
        int tmdbId,
        int seasonNumber,
        PluginConfiguration config,
        string language,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.TmdbApiKey) || tmdbId <= 0 || seasonNumber < 0)
        {
            return [];
        }

        var cacheKey = $"{language}|tv|{tmdbId}|season|{seasonNumber}";
        if (_seasonCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            return cached.Value;
        }

        try
        {
            var url =
                $"3/tv/{tmdbId}/season/{seasonNumber}?api_key={Uri.EscapeDataString(config.TmdbApiKey)}&language={Uri.EscapeDataString(language)}";
            var parsed = await GetJsonAsync<TmdbSeasonEpisodesResponse>(url, cancellationToken).ConfigureAwait(false);
            var episodes = parsed is null ? [] : MapEpisodes(parsed);
            var ttl = TimeSpan.FromSeconds(Math.Max(config.CacheTtlSeconds, 0));
            _seasonCache[cacheKey] = new CacheEntry<IReadOnlyList<TmdbEpisodeInfo>>(episodes, ttl);
            return episodes;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "TMDB season {Season} failed for series {TmdbId}", seasonNumber, tmdbId);
            return [];
        }
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

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer
            .DeserializeAsync<T>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static TmdbTitleDetails? MapTitleDetails(TmdbTitleDetailsResponse row, BaseItemKind kind)
    {
        var title = kind == BaseItemKind.Series
            ? FirstNonEmpty(row.Name, row.Title)
            : FirstNonEmpty(row.Title, row.Name);
        if (title is null)
        {
            return null;
        }

        var date = kind == BaseItemKind.Series ? row.FirstAirDate : row.ReleaseDate;
        var premiere = ParseDate(date);

        var runtime = kind == BaseItemKind.Series
            ? row.EpisodeRunTime.FirstOrDefault(static minutes => minutes > 0)
            : row.Runtime;
        if (runtime is <= 0)
        {
            runtime = null;
        }

        var people = new List<TmdbPersonCredit>();
        if (row.Credits is not null)
        {
            foreach (var cast in row.Credits.Cast.OrderBy(static member => member.Order).Take(15))
            {
                if (string.IsNullOrWhiteSpace(cast.Name))
                {
                    continue;
                }

                people.Add(new TmdbPersonCredit(cast.Name.Trim(), cast.Character, PersonKind.Actor));
            }

            foreach (var crew in row.Credits.Crew)
            {
                if (string.IsNullOrWhiteSpace(crew.Name) || MapCrewJob(crew.Job) is not { } personKind)
                {
                    continue;
                }

                people.Add(new TmdbPersonCredit(crew.Name.Trim(), crew.Job, personKind));
            }
        }

        return new TmdbTitleDetails(
            row.Id,
            kind,
            title,
            row.Overview,
            TmdbSearchMapper.ParseYear(date),
            premiere,
            row.PosterPath,
            runtime,
            row.VoteAverage > 0 ? (float)row.VoteAverage : null,
            row.Tagline,
            NamesOf(row.Genres),
            people,
            NamesOf(row.ProductionCompanies),
            MapSeasons(row.Seasons));
    }

    private static IReadOnlyList<TmdbSeasonInfo> MapSeasons(IEnumerable<TmdbSeasonRow> rows)
    {
        var seasons = new List<TmdbSeasonInfo>();
        foreach (var row in rows.OrderBy(static season => season.SeasonNumber))
        {
            if (row.SeasonNumber < 0 || (row.SeasonNumber == 0 && row.EpisodeCount <= 0))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(row.Name)
                ? $"Season {row.SeasonNumber}"
                : row.Name.Trim();
            seasons.Add(new TmdbSeasonInfo(
                row.SeasonNumber,
                name,
                string.IsNullOrWhiteSpace(row.Overview) ? null : row.Overview.Trim(),
                row.EpisodeCount,
                ParseDate(row.AirDate),
                row.PosterPath));
        }

        return seasons;
    }

    private static IReadOnlyList<TmdbEpisodeInfo> MapEpisodes(TmdbSeasonEpisodesResponse response)
    {
        var episodes = new List<TmdbEpisodeInfo>(response.Episodes.Count);
        foreach (var row in response.Episodes.OrderBy(static episode => episode.EpisodeNumber))
        {
            if (row.EpisodeNumber <= 0)
            {
                continue;
            }

            var name = FirstNonEmpty(row.Name, $"Episode {row.EpisodeNumber}");
            if (name is null)
            {
                continue;
            }

            var runtime = row.Runtime is > 0 ? row.Runtime : null;
            episodes.Add(new TmdbEpisodeInfo(
                row.EpisodeNumber,
                name,
                string.IsNullOrWhiteSpace(row.Overview) ? null : row.Overview.Trim(),
                ParseDate(row.AirDate),
                row.StillPath,
                runtime,
                row.VoteAverage > 0 ? (float)row.VoteAverage : null,
                row.Id > 0 ? row.Id : null));
        }

        return episodes;
    }

    private static DateTime? ParseDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            return null;
        }

        if (DateTime.TryParse(
            date,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsedDate))
        {
            return parsedDate;
        }

        return null;
    }

    private static IReadOnlyList<string> NamesOf(IEnumerable<TmdbNamedRow> rows)
    {
        var names = new List<string>();
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                continue;
            }

            names.Add(row.Name.Trim());
        }

        return names;
    }

    private static PersonKind? MapCrewJob(string? job) =>
        job switch
        {
            "Director" => PersonKind.Director,
            "Writer" or "Screenplay" or "Story" => PersonKind.Writer,
            "Creator" => PersonKind.Creator,
            _ => null,
        };

    private static string? FirstNonEmpty(string? preferred, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred.Trim();
        }

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
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
