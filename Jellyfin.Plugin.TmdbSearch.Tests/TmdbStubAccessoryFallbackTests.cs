using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for empty 200 responses on accessory item endpoints for TMDB search stubs.
/// </summary>
public sealed class TmdbStubAccessoryFallbackTests
{
    private static readonly Guid StubId = Guid.Parse("3ed52899-c7ff-a850-617d-da69c07207bf");

    /// <summary>
    /// Verifies ThemeMedia 404s become an empty AllThemeMediaResult.
    /// </summary>
    [Theory]
    [InlineData("/Items/3ed52899c7ffa850617dda69c07207bf/ThemeMedia", "GetThemeMedia")]
    [InlineData("/Items/3ed52899-c7ff-a850-617d-da69c07207bf/ThemeMedia", null)]
    public void TryCreate_ReturnsEmptyThemeMedia(string path, string? actionName)
    {
        Assert.True(TmdbStubAccessoryFallback.TryCreate(path, actionName, StubId, out var result));
        var payload = Assert.IsType<AllThemeMediaResult>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.NotNull(payload.ThemeSongsResult);
        Assert.NotNull(payload.ThemeVideosResult);
        Assert.Empty(payload.ThemeSongsResult.Items);
        Assert.Empty(payload.ThemeVideosResult.Items);
        Assert.Equal(StubId, payload.ThemeSongsResult.OwnerId);
        Assert.Equal(StubId, payload.ThemeVideosResult.OwnerId);
    }

    /// <summary>
    /// Verifies similar-item 404s become an empty query result.
    /// </summary>
    [Fact]
    public void TryCreate_ReturnsEmptyQueryForSimilarItems()
    {
        Assert.True(TmdbStubAccessoryFallback.TryCreate(
            "/Items/3ed52899c7ffa850617dda69c07207bf/Similar",
            "GetSimilarItems",
            StubId,
            out var result));
        var payload = Assert.IsType<QueryResult<BaseItemDto>>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Empty(payload.Items);
        Assert.Equal(0, payload.TotalRecordCount);
    }

    /// <summary>
    /// Verifies playback, image, and GetItem routes are left to their dedicated handlers.
    /// </summary>
    [Theory]
    [InlineData("/Items/3ed52899c7ffa850617dda69c07207bf/PlaybackInfo", "GetPostedPlaybackInfo")]
    [InlineData("/Items/3ed52899c7ffa850617dda69c07207bf/Images/Primary", "GetItemImage")]
    [InlineData("/Users/6af0429e93bd49dfa56162910227d22d/Items/3ed52899c7ffa850617dda69c07207bf", "GetItemLegacy")]
    [InlineData("/Items/3ed52899c7ffa850617dda69c07207bf", "GetItem")]
    public void TryCreate_IgnoresPlaybackImagesAndItemDetail(string path, string actionName)
    {
        Assert.False(TmdbStubAccessoryFallback.TryCreate(path, actionName, StubId, out _));
    }
}
