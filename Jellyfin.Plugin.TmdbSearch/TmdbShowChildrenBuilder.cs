using System.Globalization;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Library;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Builds season and episode DTOs for unowned TMDB series stubs.
/// </summary>
public static class TmdbShowChildrenBuilder
{
    /// <summary>
    /// Creates a season DTO parented to a TMDB series stub.
    /// </summary>
    /// <param name="series">Cached series search stub.</param>
    /// <param name="season">TMDB season summary.</param>
    /// <returns>A season DTO with a Gelato-compatible GUID.</returns>
    public static BaseItemDto CreateSeason(BaseItemDto series, TmdbSeasonInfo season)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(season);

        if (!TmdbItemMetadataBuilder.TryGetTmdbId(series, out var tmdbId))
        {
            throw new ArgumentException("Series stub must include a TMDB provider id.", nameof(series));
        }

        var id = StremioGuidHelper.ForSeason(tmdbId, season.SeasonNumber);
        var posterUrl = TmdbSearchMapper.ToPosterUrl(season.PosterPath);
        var dto = new BaseItemDto
        {
            Id = id,
            ServerId = series.ServerId,
            Name = season.Name,
            Overview = season.Overview,
            PremiereDate = season.AirDate,
            ProductionYear = season.AirDate?.Year,
            Type = BaseItemKind.Season,
            MediaType = MediaType.Video,
            IsFolder = true,
            IndexNumber = season.SeasonNumber,
            ParentId = series.Id,
            SeriesId = series.Id,
            SeriesName = series.Name,
            ChildCount = season.EpisodeCount,
            RecursiveItemCount = season.EpisodeCount,
            ProviderIds = CopyProviderIds(series),
            ImageBlurHashes = new Dictionary<ImageType, Dictionary<string, string>>(),
            MediaSources = [],
            LocationType = LocationType.FileSystem,
            IsPlaceHolder = false,
            CanDownload = false,
            PlayAccess = PlayAccess.Full,
        };

        ApplyPrimaryImage(dto, posterUrl, series.ServerId);
        return dto;
    }

    /// <summary>
    /// Creates an episode DTO parented to a TMDB season stub.
    /// </summary>
    /// <param name="series">Cached series search stub.</param>
    /// <param name="season">Season DTO for this episode.</param>
    /// <param name="episode">TMDB episode row.</param>
    /// <returns>An episode DTO with a Gelato-compatible GUID and /stub media source.</returns>
    public static BaseItemDto CreateEpisode(BaseItemDto series, BaseItemDto season, TmdbEpisodeInfo episode)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(season);
        ArgumentNullException.ThrowIfNull(episode);

        if (!TmdbItemMetadataBuilder.TryGetTmdbId(series, out var tmdbId))
        {
            throw new ArgumentException("Series stub must include a TMDB provider id.", nameof(series));
        }

        var seasonNumber = season.IndexNumber ?? 0;
        var id = StremioGuidHelper.ForEpisode(tmdbId, seasonNumber, episode.EpisodeNumber);
        var posterUrl = TmdbSearchMapper.ToPosterUrl(episode.StillPath);
        var dto = new BaseItemDto
        {
            Id = id,
            ServerId = series.ServerId,
            Name = episode.Name,
            Overview = episode.Overview,
            PremiereDate = episode.AirDate,
            ProductionYear = episode.AirDate?.Year,
            Type = BaseItemKind.Episode,
            MediaType = MediaType.Video,
            IsFolder = false,
            IndexNumber = episode.EpisodeNumber,
            ParentIndexNumber = seasonNumber,
            ParentId = season.Id,
            SeasonId = season.Id,
            SeriesId = series.Id,
            SeriesName = series.Name,
            SeasonName = season.Name,
            ProviderIds = CopyProviderIds(series),
            ImageBlurHashes = new Dictionary<ImageType, Dictionary<string, string>>(),
            LocationType = LocationType.FileSystem,
            IsPlaceHolder = false,
            CanDownload = false,
            PlayAccess = PlayAccess.Full,
            MediaSources =
            [
                new MediaSourceInfo
                {
                    Id = id.ToString("N", CultureInfo.InvariantCulture),
                    Path = "/stub",
                    Protocol = MediaProtocol.File,
                    IsRemote = false,
                    SupportsDirectPlay = false,
                    SupportsDirectStream = true,
                },
            ],
        };

        if (episode.RuntimeMinutes is > 0)
        {
            dto.RunTimeTicks = episode.RuntimeMinutes.Value * TimeSpan.TicksPerMinute;
        }

        if (episode.VoteAverage is > 0)
        {
            dto.CommunityRating = episode.VoteAverage;
        }

        ApplyPrimaryImage(dto, posterUrl, series.ServerId);
        return dto;
    }

    /// <summary>
    /// Wraps item DTOs in a Jellyfin query result.
    /// </summary>
    /// <param name="items">Season or episode DTOs.</param>
    /// <returns>A query result with TotalRecordCount matching the item count.</returns>
    public static QueryResult<BaseItemDto> CreateQuery(IReadOnlyList<BaseItemDto> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new QueryResult<BaseItemDto>
        {
            Items = items.ToArray(),
            TotalRecordCount = items.Count,
        };
    }

    /// <summary>
    /// An empty query result used for Next Up on unowned TMDB series.
    /// </summary>
    /// <returns>A query with no items.</returns>
    public static QueryResult<BaseItemDto> CreateEmptyQuery() => CreateQuery([]);

    /// <summary>
    /// An HTTP 200 wrapping an empty query result.
    /// </summary>
    /// <returns>OkObjectResult with an empty QueryResult.</returns>
    public static IActionResult CreateEmptyResult() => new OkObjectResult(CreateEmptyQuery());

    /// <summary>
    /// An HTTP 200 wrapping a children query result.
    /// </summary>
    /// <param name="items">Season or episode DTOs.</param>
    /// <returns>OkObjectResult with the query result.</returns>
    public static IActionResult CreateResult(IReadOnlyList<BaseItemDto> items) =>
        new OkObjectResult(CreateQuery(items));

    private static Dictionary<string, string> CopyProviderIds(BaseItemDto series) =>
        series.ProviderIds is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(series.ProviderIds, StringComparer.OrdinalIgnoreCase);

    private static void ApplyPrimaryImage(BaseItemDto dto, string? posterUrl, string? serverId)
    {
        if (posterUrl is null || string.IsNullOrWhiteSpace(serverId))
        {
            return;
        }

        dto.ImageTags = new Dictionary<ImageType, string>
        {
            [ImageType.Primary] = "tmdb",
        };
    }
}
