using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for building a GetItem-ready metadata DTO without waiting on Gelato streams.
/// </summary>
public sealed class TmdbItemMetadataBuilderTests
{
    /// <summary>
    /// Verifies the details-page DTO keeps plot/poster metadata and drops Path=/stub sources.
    /// </summary>
    [Fact]
    public void FromStub_ClearsPlaceholderMediaSources()
    {
        var stub = SearchResultDtoBuilder.CreateStub(
            new TmdbSearchHit(550, BaseItemKind.Movie, "Fight Club", 1999, "/p.jpg", "Office worker", 91.2),
            "jellyfin-server-id").Dto;

        var dto = TmdbItemMetadataBuilder.FromStub(stub, details: null);

        Assert.Equal(stub.Id, dto.Id);
        Assert.Equal("Fight Club", dto.Name);
        Assert.Equal("Office worker", dto.Overview);
        Assert.Equal("jellyfin-server-id", dto.ServerId);
        Assert.Equal("550", dto.ProviderIds[MetadataProvider.Tmdb.ToString()]);
        Assert.NotNull(dto.MediaSources);
        Assert.Empty(dto.MediaSources);
        Assert.NotSame(stub, dto);
        Assert.NotEmpty(stub.MediaSources);
    }

    /// <summary>
    /// Verifies TMDB details fill genres, people, rating, and runtime used by jellyfin-web.
    /// </summary>
    [Fact]
    public void FromStub_AppliesTmdbDetails()
    {
        var stub = SearchResultDtoBuilder.CreateStub(
            new TmdbSearchHit(550, BaseItemKind.Movie, "Fight Club", 1999, "/p.jpg", "Office worker", 91.2),
            "jellyfin-server-id").Dto;
        var details = new TmdbTitleDetails(
            550,
            BaseItemKind.Movie,
            "Fight Club",
            "An insomniac office worker and a devil-may-care soap maker...",
            1999,
            new DateTime(1999, 10, 15, 0, 0, 0, DateTimeKind.Utc),
            "/p.jpg",
            139,
            8.4f,
            "Mischief. Mayhem. Soap.",
            ["Drama"],
            [
                new TmdbPersonCredit("Brad Pitt", "Tyler Durden", PersonKind.Actor),
                new TmdbPersonCredit("David Fincher", null, PersonKind.Director),
            ],
            ["Fox 2000 Pictures"],
            []);

        var dto = TmdbItemMetadataBuilder.FromStub(stub, details);

        Assert.Equal("An insomniac office worker and a devil-may-care soap maker...", dto.Overview);
        Assert.Equal(139L * TimeSpan.TicksPerMinute, dto.RunTimeTicks);
        Assert.Equal(8.4f, dto.CommunityRating);
        Assert.Equal(["Drama"], dto.Genres);
        Assert.Equal("Mischief. Mayhem. Soap.", Assert.Single(dto.Taglines));
        Assert.Equal("Fox 2000 Pictures", Assert.Single(dto.Studios).Name);
        Assert.Equal(2, dto.People.Length);
        Assert.Equal("Brad Pitt", dto.People[0].Name);
        Assert.Equal("Tyler Durden", dto.People[0].Role);
        Assert.Equal(PersonKind.Actor, dto.People[0].Type);
        Assert.Equal(PersonKind.Director, dto.People[1].Type);
        Assert.Empty(dto.MediaSources);
        Assert.Equal(LocationType.FileSystem, dto.LocationType);
    }

    /// <summary>
    /// Verifies series metadata reports TMDB season counts so jellyfin-web shows children.
    /// </summary>
    [Fact]
    public void FromStub_SetsSeriesChildCountFromTmdbSeasons()
    {
        var stub = SearchResultDtoBuilder.CreateStub(
            new TmdbSearchHit(1399, BaseItemKind.Series, "Game of Thrones", 2011, "/g.jpg", "Westeros", 400),
            "jellyfin-server-id").Dto;
        var details = new TmdbTitleDetails(
            1399,
            BaseItemKind.Series,
            "Game of Thrones",
            "Westeros",
            2011,
            new DateTime(2011, 4, 17, 0, 0, 0, DateTimeKind.Utc),
            "/g.jpg",
            60,
            8.9f,
            null,
            ["Sci-Fi & Fantasy"],
            [],
            [],
            [
                new TmdbSeasonInfo(1, "Season 1", "Ned Stark", 10, new DateTime(2011, 4, 17, 0, 0, 0, DateTimeKind.Utc), "/s1.jpg"),
                new TmdbSeasonInfo(2, "Season 2", "Clash", 10, new DateTime(2012, 4, 1, 0, 0, 0, DateTimeKind.Utc), "/s2.jpg"),
            ]);

        var dto = TmdbItemMetadataBuilder.FromStub(stub, details);

        Assert.Equal(2, dto.ChildCount);
        Assert.True(dto.IsFolder);
        Assert.Equal(BaseItemKind.Series, dto.Type);
        Assert.Empty(dto.MediaSources);
    }

    /// <summary>
    /// Verifies season/episode identity fields survive the metadata overlay.
    /// </summary>
    [Fact]
    public void FromStub_CopiesSeasonAndEpisodeIdentity()
    {
        var seriesId = StremioGuidHelper.ToGuid(StremioMediaKind.Series, "tmdb:1399");
        var seasonId = StremioGuidHelper.ForSeason(1399, 1);
        var stub = new BaseItemDto
        {
            Id = StremioGuidHelper.ForEpisode(1399, 1, 2),
            Name = "The Kingsroad",
            Type = BaseItemKind.Episode,
            IndexNumber = 2,
            ParentIndexNumber = 1,
            ParentId = seasonId,
            SeriesId = seriesId,
            SeasonId = seasonId,
            SeriesName = "Game of Thrones",
            ServerId = "jellyfin-server-id",
            ProviderIds = new Dictionary<string, string>
            {
                [MetadataProvider.Tmdb.ToString()] = "1399",
            },
        };

        var dto = TmdbItemMetadataBuilder.FromStub(stub, details: null);

        Assert.Equal(2, dto.IndexNumber);
        Assert.Equal(1, dto.ParentIndexNumber);
        Assert.Equal(seasonId, dto.ParentId);
        Assert.Equal(seriesId, dto.SeriesId);
        Assert.Equal(seasonId, dto.SeasonId);
        Assert.Equal("Game of Thrones", dto.SeriesName);
        Assert.Empty(dto.MediaSources);
    }
}
