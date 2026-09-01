using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for TMDB-backed season/episode DTOs and empty Next Up payloads.
/// </summary>
public sealed class TmdbShowChildrenBuilderTests
{
    /// <summary>
    /// Verifies seasons use Gelato series-namespace GUIDs and report episode ChildCount.
    /// </summary>
    [Fact]
    public void CreateSeason_UsesSeriesNamespaceGuidAndChildCount()
    {
        var series = SearchResultDtoBuilder.CreateStub(
            new TmdbSearchHit(1399, BaseItemKind.Series, "Game of Thrones", 2011, "/g.jpg", "Westeros", 1),
            "jellyfin-server-id").Dto;
        var seasonInfo = new TmdbSeasonInfo(
            1,
            "Season 1",
            "Ned Stark",
            10,
            new DateTime(2011, 4, 17, 0, 0, 0, DateTimeKind.Utc),
            "/s1.jpg");

        var season = TmdbShowChildrenBuilder.CreateSeason(series, seasonInfo);

        Assert.Equal(StremioGuidHelper.ForSeason(1399, 1), season.Id);
        Assert.Equal(BaseItemKind.Season, season.Type);
        Assert.True(season.IsFolder);
        Assert.Equal(1, season.IndexNumber);
        Assert.Equal(series.Id, season.ParentId);
        Assert.Equal(series.Id, season.SeriesId);
        Assert.Equal("Game of Thrones", season.SeriesName);
        Assert.Equal(10, season.ChildCount);
        Assert.Equal("jellyfin-server-id", season.ServerId);
        Assert.Equal("1399", season.ProviderIds[MetadataProvider.Tmdb.ToString()]);
        Assert.Contains(ImageType.Primary, season.ImageTags.Keys);
        Assert.Empty(season.MediaSources ?? []);
    }

    /// <summary>
    /// Verifies episodes use Gelato's series:season:episode URI and keep a /stub source for insert.
    /// </summary>
    [Fact]
    public void CreateEpisode_UsesStremioEpisodeGuid()
    {
        var series = SearchResultDtoBuilder.CreateStub(
            new TmdbSearchHit(1399, BaseItemKind.Series, "Game of Thrones", 2011, "/g.jpg", "Westeros", 1),
            "jellyfin-server-id").Dto;
        var season = TmdbShowChildrenBuilder.CreateSeason(
            series,
            new TmdbSeasonInfo(1, "Season 1", null, 10, null, null));
        var episodeInfo = new TmdbEpisodeInfo(
            1,
            "Winter Is Coming",
            "Ned",
            new DateTime(2011, 4, 17, 0, 0, 0, DateTimeKind.Utc),
            "/e1.jpg",
            62,
            8.3f,
            63056);

        var episode = TmdbShowChildrenBuilder.CreateEpisode(series, season, episodeInfo);

        Assert.Equal(StremioGuidHelper.ForEpisode(1399, 1, 1), episode.Id);
        Assert.Equal(BaseItemKind.Episode, episode.Type);
        Assert.False(episode.IsFolder);
        Assert.Equal(1, episode.IndexNumber);
        Assert.Equal(1, episode.ParentIndexNumber);
        Assert.Equal(season.Id, episode.ParentId);
        Assert.Equal(season.Id, episode.SeasonId);
        Assert.Equal(series.Id, episode.SeriesId);
        Assert.Equal("Game of Thrones", episode.SeriesName);
        Assert.Equal(MediaType.Video, episode.MediaType);
        Assert.Equal("/stub", Assert.Single(episode.MediaSources).Path);
        Assert.Equal("tmdb:1399:1:1", StremioGuidHelper.BuildEpisodeExternalId(1399, 1, 1));
    }

    /// <summary>
    /// Verifies Next Up for an unowned TMDB series is an empty query, not a 404.
    /// </summary>
    [Fact]
    public void CreateEmptyQuery_IsEmptyResult()
    {
        var payload = TmdbShowChildrenBuilder.CreateEmptyQuery();

        Assert.Empty(payload.Items);
        Assert.Equal(0, payload.TotalRecordCount);
    }

    /// <summary>
    /// Verifies a QueryResult wraps season DTOs for GetSeasons.
    /// </summary>
    [Fact]
    public void CreateQuery_WrapsItems()
    {
        var series = SearchResultDtoBuilder.CreateStub(
            new TmdbSearchHit(1399, BaseItemKind.Series, "Game of Thrones", 2011, null, null, 1),
            "sid").Dto;
        var seasons = new[]
        {
            TmdbShowChildrenBuilder.CreateSeason(
                series,
                new TmdbSeasonInfo(1, "Season 1", null, 10, null, null)),
        };

        var result = TmdbShowChildrenBuilder.CreateQuery(seasons);

        Assert.Equal(seasons[0].Id, Assert.Single(result.Items).Id);
        Assert.Equal(1, result.TotalRecordCount);
    }
}

/// <summary>
/// Tests for recognizing Shows/Seasons, Shows/Episodes, NextUp, and ParentId listings.
/// </summary>
public sealed class TmdbShowChildrenRouteTests
{
    private static readonly Guid SeriesId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid SeasonId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>
    /// Verifies GetSeasons and /Shows/{id}/Seasons map to the series id.
    /// </summary>
    [Theory]
    [InlineData("/Shows/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/Seasons", "GetSeasons")]
    [InlineData("/Shows/aaaaaaaabbbbccccddddeeeeeeeeeeee/Seasons", null)]
    public void TryMatch_RecognizesGetSeasons(string path, string? actionName)
    {
        Assert.True(TmdbShowChildrenRoute.TryMatch(
            path,
            actionName,
            seriesId: SeriesId,
            seasonId: null,
            parentId: null,
            out var match));
        Assert.Equal(TmdbShowChildrenKind.Seasons, match.Kind);
        Assert.Equal(SeriesId, match.SeriesId);
    }

    /// <summary>
    /// Verifies GetEpisodes carries an optional season id.
    /// </summary>
    [Fact]
    public void TryMatch_RecognizesGetEpisodesWithSeasonId()
    {
        Assert.True(TmdbShowChildrenRoute.TryMatch(
            "/Shows/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/Episodes",
            "GetEpisodes",
            seriesId: SeriesId,
            seasonId: SeasonId,
            parentId: null,
            out var match));
        Assert.Equal(TmdbShowChildrenKind.Episodes, match.Kind);
        Assert.Equal(SeriesId, match.SeriesId);
        Assert.Equal(SeasonId, match.SeasonId);
    }

    /// <summary>
    /// Verifies GetNextUp uses SeriesId from the query, not a path GUID.
    /// </summary>
    [Fact]
    public void TryMatch_RecognizesGetNextUpBySeriesId()
    {
        Assert.True(TmdbShowChildrenRoute.TryMatch(
            "/Shows/NextUp",
            "GetNextUp",
            seriesId: SeriesId,
            seasonId: null,
            parentId: null,
            out var match));
        Assert.Equal(TmdbShowChildrenKind.NextUp, match.Kind);
        Assert.Equal(SeriesId, match.SeriesId);
    }

    /// <summary>
    /// Verifies GetItems?ParentId= is a children listing, not a search.
    /// </summary>
    [Fact]
    public void TryMatch_RecognizesGetItemsParentId()
    {
        Assert.True(TmdbShowChildrenRoute.TryMatch(
            "/Items",
            "GetItems",
            seriesId: null,
            seasonId: null,
            parentId: SeriesId,
            out var match));
        Assert.Equal(TmdbShowChildrenKind.ParentItems, match.Kind);
        Assert.Equal(SeriesId, match.ParentId);
    }

    /// <summary>
    /// Verifies playback and GetItem stay with their dedicated handlers.
    /// </summary>
    [Theory]
    [InlineData("/Items/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/PlaybackInfo", "GetPostedPlaybackInfo")]
    [InlineData("/Items/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "GetItem")]
    [InlineData("/Items", "GetItems")]
    public void TryMatch_IgnoresPlaybackSearchAndGetItem(string path, string actionName)
    {
        Assert.False(TmdbShowChildrenRoute.TryMatch(
            path,
            actionName,
            seriesId: null,
            seasonId: null,
            parentId: null,
            out _));
    }

    /// <summary>
    /// Verifies accessory empty Next Up is not handled by the ThemeMedia fallback.
    /// Next Up must be an inbound empty 200 so jellyfin-web does not load global continue-watching.
    /// </summary>
    [Fact]
    public void AccessoryFallback_DoesNotHandleShowsRoutes()
    {
        Assert.False(TmdbStubAccessoryFallback.TryCreate(
            "/Shows/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee/Seasons",
            "GetSeasons",
            SeriesId,
            out _));
        Assert.False(TmdbStubAccessoryFallback.TryCreate(
            "/Shows/NextUp",
            "GetNextUp",
            SeriesId,
            out _));
    }

    /// <summary>
    /// Verifies show-children handling runs before Gelato insert and also on 404 fallback.
    /// </summary>
    [Fact]
    public void ShowChildrenFilter_RunsBeforeGelatoAndOnNotFound()
    {
        Assert.True(typeof(IAsyncAlwaysRunResultFilter).IsAssignableFrom(typeof(TmdbShowChildrenActionFilter)));
        Assert.True(typeof(IAsyncActionFilter).IsAssignableFrom(typeof(TmdbShowChildrenActionFilter)));
        Assert.Equal(0, new TmdbShowChildrenActionFilter(
            children: null!,
            logger: NullLogger<TmdbShowChildrenActionFilter>.Instance).Order);
    }

    /// <summary>
    /// Verifies an OkObjectResult wrapping an empty query is the Next Up payload.
    /// </summary>
    [Fact]
    public void CreateEmptyResult_IsOkQuery()
    {
        var result = TmdbShowChildrenBuilder.CreateEmptyResult();
        var payload = Assert.IsType<QueryResult<BaseItemDto>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(payload.Items);
        Assert.Equal(0, payload.TotalRecordCount);
    }
}
