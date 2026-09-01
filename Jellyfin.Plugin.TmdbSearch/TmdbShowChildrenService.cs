using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TmdbSearch.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Serves TMDB seasons/episodes and empty Next Up for unowned search stubs.
/// </summary>
public sealed class TmdbShowChildrenService
{
    private readonly TmdbPosterCache _stubCache;
    private readonly TmdbClient _tmdbClient;
    private readonly GelatoMetaBridge _gelatoBridge;
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly ILogger<TmdbShowChildrenService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbShowChildrenService"/> class.
    /// </summary>
    /// <param name="stubCache">Search-stub DTO cache.</param>
    /// <param name="tmdbClient">TMDB HTTP client.</param>
    /// <param name="gelatoBridge">Gelato meta seed bridge.</param>
    /// <param name="serverConfigurationManager">Server configuration for language fallback.</param>
    /// <param name="logger">Logger instance.</param>
    public TmdbShowChildrenService(
        TmdbPosterCache stubCache,
        TmdbClient tmdbClient,
        GelatoMetaBridge gelatoBridge,
        IServerConfigurationManager serverConfigurationManager,
        ILogger<TmdbShowChildrenService> logger)
    {
        _stubCache = stubCache;
        _tmdbClient = tmdbClient;
        _gelatoBridge = gelatoBridge;
        _serverConfigurationManager = serverConfigurationManager;
        _logger = logger;
    }

    /// <summary>
    /// Builds an action result for a parsed children request when the owner is a TMDB stub.
    /// </summary>
    /// <param name="match">Parsed children route.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A 200 payload, or null when this request should fall through to Jellyfin.</returns>
    public async Task<IActionResult?> TryCreateAsync(TmdbShowChildrenMatch match, CancellationToken cancellationToken)
    {
        return match.Kind switch
        {
            TmdbShowChildrenKind.NextUp => TryCreateNextUp(match.SeriesId),
            TmdbShowChildrenKind.Seasons => await TryCreateSeasonsAsync(match.SeriesId, cancellationToken)
                .ConfigureAwait(false),
            TmdbShowChildrenKind.Episodes => await TryCreateEpisodesAsync(
                    match.SeriesId,
                    match.SeasonId,
                    cancellationToken)
                .ConfigureAwait(false),
            TmdbShowChildrenKind.ParentItems => await TryCreateParentItemsAsync(match.ParentId, cancellationToken)
                .ConfigureAwait(false),
            _ => Unhandled(match.Kind),
        };
    }

    private IActionResult? TryCreateNextUp(Guid seriesId)
    {
        if (!PluginSettings.Current.EnableEmptyNextUpForStubs
            || !_stubCache.TryGetDto(seriesId, out var series)
            || series.Type != BaseItemKind.Series)
        {
            return null;
        }

        return TmdbShowChildrenBuilder.CreateEmptyResult();
    }

    private async Task<IActionResult?> TryCreateSeasonsAsync(Guid seriesId, CancellationToken cancellationToken)
    {
        if (!PluginSettings.Current.EnableTmdbSeasons
            || !_stubCache.TryGetDto(seriesId, out var series)
            || series.Type != BaseItemKind.Series)
        {
            return null;
        }

        var seasons = await BuildSeasonsAsync(series, cancellationToken).ConfigureAwait(false);
        return TmdbShowChildrenBuilder.CreateResult(seasons);
    }

    private async Task<IActionResult?> TryCreateEpisodesAsync(
        Guid seriesId,
        Guid? seasonId,
        CancellationToken cancellationToken)
    {
        if (!PluginSettings.Current.EnableTmdbEpisodes
            || !_stubCache.TryGetDto(seriesId, out var series)
            || series.Type != BaseItemKind.Series)
        {
            return null;
        }

        var episodes = await BuildEpisodesAsync(series, seasonId, cancellationToken).ConfigureAwait(false);
        return TmdbShowChildrenBuilder.CreateResult(episodes);
    }

    private async Task<IActionResult?> TryCreateParentItemsAsync(Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is not { } id || !_stubCache.TryGetDto(id, out var parent))
        {
            return null;
        }

        if (parent.Type == BaseItemKind.Series)
        {
            return await TryCreateSeasonsAsync(id, cancellationToken).ConfigureAwait(false);
        }

        if (parent.Type == BaseItemKind.Season)
        {
            var seriesId = parent.SeriesId ?? Guid.Empty;
            if (seriesId == Guid.Empty || !_stubCache.TryGetDto(seriesId, out _))
            {
                return null;
            }

            return await TryCreateEpisodesAsync(seriesId, id, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<IReadOnlyList<BaseItemDto>> BuildSeasonsAsync(
        BaseItemDto series,
        CancellationToken cancellationToken)
    {
        var details = await TryGetSeriesDetailsAsync(series, cancellationToken).ConfigureAwait(false);
        if (details is null || details.Seasons.Count == 0)
        {
            return [];
        }

        var seasons = new List<BaseItemDto>(details.Seasons.Count);
        foreach (var seasonInfo in details.Seasons)
        {
            var season = TmdbShowChildrenBuilder.CreateSeason(series, seasonInfo);
            CacheChild(season, TmdbSearchMapper.ToPosterUrl(seasonInfo.PosterPath), seedGelato: false);
            seasons.Add(season);
        }

        return seasons;
    }

    private async Task<IReadOnlyList<BaseItemDto>> BuildEpisodesAsync(
        BaseItemDto series,
        Guid? seasonId,
        CancellationToken cancellationToken)
    {
        if (!TmdbItemMetadataBuilder.TryGetTmdbId(series, out var tmdbId))
        {
            return [];
        }

        var seasons = await BuildSeasonsAsync(series, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<BaseItemDto> targetSeasons = seasonId is { } id && id != Guid.Empty
            ? seasons.Where(season => season.Id == id).ToArray()
            : seasons;

        if (targetSeasons.Count == 0)
        {
            return [];
        }

        var (config, language) = ResolveConfig();
        var episodes = new List<BaseItemDto>();
        foreach (var season in targetSeasons)
        {
            var seasonNumber = season.IndexNumber ?? 0;
            var rows = await _tmdbClient
                .GetSeasonEpisodesAsync(tmdbId, seasonNumber, config, language, cancellationToken)
                .ConfigureAwait(false);
            foreach (var row in rows)
            {
                var episode = TmdbShowChildrenBuilder.CreateEpisode(series, season, row);
                CacheChild(
                    episode,
                    TmdbSearchMapper.ToPosterUrl(row.StillPath),
                    seedGelato: true,
                    StremioGuidHelper.BuildEpisodeExternalId(tmdbId, seasonNumber, row.EpisodeNumber));
                episodes.Add(episode);
            }
        }

        return episodes;
    }

    private async Task<TmdbTitleDetails?> TryGetSeriesDetailsAsync(
        BaseItemDto series,
        CancellationToken cancellationToken)
    {
        if (!TmdbItemMetadataBuilder.TryGetTmdbId(series, out var tmdbId))
        {
            return null;
        }

        var (config, language) = ResolveConfig();
        if (string.IsNullOrWhiteSpace(config.TmdbApiKey))
        {
            return null;
        }

        return await _tmdbClient
            .GetTitleDetailsAsync(BaseItemKind.Series, tmdbId, config, language, cancellationToken)
            .ConfigureAwait(false);
    }

    private void CacheChild(BaseItemDto dto, string? posterUrl, bool seedGelato, string? externalId = null)
    {
        _stubCache.Set(dto.Id, dto, posterUrl);
        if (!seedGelato || string.IsNullOrWhiteSpace(externalId))
        {
            return;
        }

        _gelatoBridge.SaveSearchMeta(
            dto.Id,
            StremioMediaKind.Series,
            externalId,
            dto.Name,
            posterUrl,
            dto.Overview);
    }

    private (PluginConfiguration Config, string Language) ResolveConfig()
    {
        var plugin = Plugin.Instance;
        var config = plugin?.Configuration ?? new PluginConfiguration();
        var language = plugin is null
            ? "en-US"
            : TmdbLanguage.Resolve(config, _serverConfigurationManager);
        return (config, language);
    }

    private IActionResult? Unhandled(TmdbShowChildrenKind kind)
    {
        _logger.LogWarning("Unhandled TMDB show children kind {Kind}", kind);
        return null;
    }
}
