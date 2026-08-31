using System.Globalization;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Gelato seed payload for an unowned TMDB search stub.
/// </summary>
/// <param name="Guid">Deterministic Gelato search GUID.</param>
/// <param name="Kind">Movie or series.</param>
/// <param name="ExternalId">Stremio external id, typically tmdb:{id}.</param>
/// <param name="Name">Display title.</param>
/// <param name="PosterUrl">Absolute TMDB poster URL when known.</param>
/// <param name="Description">Plot overview when known.</param>
public sealed record GelatoMetaSeed(
    Guid Guid,
    StremioMediaKind Kind,
    string ExternalId,
    string? Name,
    string? PosterUrl,
    string? Description);

/// <summary>
/// A lightweight search DTO plus the Gelato meta that should be cached for insert.
/// </summary>
/// <param name="Dto">Jellyfin search result DTO.</param>
/// <param name="Gelato">Gelato seed for click-to-insert.</param>
public sealed record SearchStubResult(BaseItemDto Dto, GelatoMetaSeed Gelato);

/// <summary>
/// Builds Remux-style search stubs without Jellyfin's IDtoService pipeline.
/// </summary>
public static class SearchResultDtoBuilder
{
    /// <summary>
    /// Creates a Gelato-compatible search stub for an unowned TMDB hit.
    /// </summary>
    /// <param name="hit">Normalized TMDB search hit.</param>
    /// <returns>The DTO and Gelato seed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the hit is not a movie or series.</exception>
    public static SearchStubResult CreateStub(TmdbSearchHit hit)
    {
        if (StremioGuidHelper.ToStremioKind(hit.Kind) is not { } stremioKind)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hit),
                hit.Kind,
                "Only movie and series hits can become search stubs.");
        }

        var externalId = StremioGuidHelper.BuildExternalId(imdbId: null, hit.TmdbId);
        var guid = StremioGuidHelper.ToGuid(stremioKind, externalId);
        var posterUrl = TmdbSearchMapper.ToPosterUrl(hit.PosterPath);

        var dto = new BaseItemDto
        {
            Id = guid,
            Name = hit.Title,
            Overview = hit.Overview,
            ProductionYear = hit.Year,
            PremiereDate = hit.Year is { } year
                ? new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                : null,
            Type = hit.Kind,
            MediaType = MediaType.Video,
            ProviderIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [MetadataProvider.Tmdb.ToString()] = hit.TmdbId.ToString(CultureInfo.InvariantCulture),
            },
            MediaSources =
            [
                new MediaSourceInfo
                {
                    Id = guid.ToString("N", CultureInfo.InvariantCulture),
                    Path = "/stub",
                    Protocol = MediaProtocol.File,
                    IsRemote = false,
                    SupportsDirectPlay = false,
                    SupportsDirectStream = true,
                },
            ],
        };

        if (posterUrl is not null)
        {
            dto.ImageTags = new Dictionary<ImageType, string>
            {
                [ImageType.Primary] = "tmdb",
            };
        }

        return new SearchStubResult(
            dto,
            new GelatoMetaSeed(
                guid,
                stremioKind,
                externalId,
                hit.Title,
                posterUrl,
                hit.Overview));
    }
}
