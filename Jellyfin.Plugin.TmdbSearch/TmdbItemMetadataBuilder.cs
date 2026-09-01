using System.Globalization;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Library;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Builds a details-page DTO from a search stub plus optional TMDB details.
/// Never copies Path=/stub media sources, so Play cannot target the placeholder.
/// </summary>
public static class TmdbItemMetadataBuilder
{
    /// <summary>
    /// Copies stub identity and artwork, overlays TMDB details, and clears media sources.
    /// </summary>
    /// <param name="stub">Cached search-result DTO.</param>
    /// <param name="details">TMDB title details when the extra request succeeded.</param>
    /// <returns>A new DTO safe to return before Gelato streams exist.</returns>
    public static BaseItemDto FromStub(BaseItemDto stub, TmdbTitleDetails? details)
    {
        ArgumentNullException.ThrowIfNull(stub);

        var dto = new BaseItemDto
        {
            Id = stub.Id,
            ServerId = stub.ServerId,
            Name = FirstNonEmpty(details?.Title, stub.Name) ?? stub.Name,
            Overview = FirstNonEmpty(details?.Overview, stub.Overview) ?? stub.Overview,
            ProductionYear = details?.Year ?? stub.ProductionYear,
            PremiereDate = details?.PremiereDate ?? stub.PremiereDate,
            Type = stub.Type,
            MediaType = stub.MediaType,
            IsFolder = stub.IsFolder,
            ImageTags = stub.ImageTags is null
                ? null
                : new Dictionary<ImageType, string>(stub.ImageTags),
            ImageBlurHashes = stub.ImageBlurHashes is null
                ? new Dictionary<ImageType, Dictionary<string, string>>()
                : new Dictionary<ImageType, Dictionary<string, string>>(stub.ImageBlurHashes),
            ProviderIds = stub.ProviderIds is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(stub.ProviderIds, StringComparer.OrdinalIgnoreCase),
            MediaSources = [],
            LocationType = LocationType.FileSystem,
            IsPlaceHolder = false,
            CanDownload = false,
            PlayAccess = PlayAccess.Full,
            IndexNumber = stub.IndexNumber,
            IndexNumberEnd = stub.IndexNumberEnd,
            ParentIndexNumber = stub.ParentIndexNumber,
            ParentId = stub.ParentId,
            SeriesId = stub.SeriesId,
            SeasonId = stub.SeasonId,
            SeriesName = stub.SeriesName,
            SeasonName = stub.SeasonName,
            ChildCount = stub.ChildCount,
            RecursiveItemCount = stub.RecursiveItemCount,
        };

        if (details is null)
        {
            return dto;
        }

        if (details.RuntimeMinutes is > 0)
        {
            dto.RunTimeTicks = details.RuntimeMinutes.Value * TimeSpan.TicksPerMinute;
        }

        if (details.VoteAverage is > 0)
        {
            dto.CommunityRating = details.VoteAverage;
        }

        if (details.Genres.Count > 0)
        {
            dto.Genres = details.Genres.ToArray();
        }

        if (!string.IsNullOrWhiteSpace(details.Tagline))
        {
            dto.Taglines = [details.Tagline];
        }

        if (details.Studios.Count > 0)
        {
            dto.Studios = details.Studios
                .Select(static name => new NameGuidPair { Name = name })
                .ToArray();
        }

        if (details.People.Count > 0)
        {
            dto.People = details.People
                .Select(static person => new BaseItemPerson
                {
                    Name = person.Name,
                    Role = person.Role,
                    Type = person.Type,
                })
                .ToArray();
        }

        if (details.Seasons.Count > 0)
        {
            dto.ChildCount = details.Seasons.Count;
            dto.RecursiveItemCount = details.Seasons.Sum(static season => Math.Max(season.EpisodeCount, 0));
        }

        return dto;
    }

    /// <summary>
    /// Reads the numeric TMDB id from a stub DTO.
    /// </summary>
    /// <param name="dto">Item DTO.</param>
    /// <param name="tmdbId">Parsed TMDB id when successful.</param>
    /// <returns>True when a TMDB provider id is present.</returns>
    public static bool TryGetTmdbId(BaseItemDto dto, out int tmdbId)
    {
        tmdbId = 0;
        if (dto.ProviderIds is null)
        {
            return false;
        }

        return dto.ProviderIds.TryGetValue(MetadataProvider.Tmdb.ToString(), out var raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out tmdbId)
            && tmdbId > 0;
    }

    private static string? FirstNonEmpty(string? preferred, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }
}
