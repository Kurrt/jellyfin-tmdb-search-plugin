using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for recognizing Jellyfin single-item detail MVC actions.
/// </summary>
public sealed class TmdbItemActionFilterTests
{
    /// <summary>
    /// Verifies the GetItem endpoints used when opening a search result are intercepted.
    /// </summary>
    [Theory]
    [InlineData("GetItem")]
    [InlineData("GetItemLegacy")]
    public void IsItemDetailAction_AcceptsGetItemActions(string actionName)
    {
        Assert.True(TmdbItemActionFilter.IsItemDetailAction(actionName));
    }

    /// <summary>
    /// Verifies search and image actions are left to their own filters.
    /// </summary>
    [Theory]
    [InlineData("GetItems")]
    [InlineData("GetItemImage")]
    [InlineData(null)]
    public void IsItemDetailAction_IgnoresOtherActions(string? actionName)
    {
        Assert.False(TmdbItemActionFilter.IsItemDetailAction(actionName));
    }

    /// <summary>
    /// Verifies the web client's /Users/{userId}/Items/{itemId} path yields the stub GUID.
    /// </summary>
    [Fact]
    public void TryGetItemIdFromPath_ParsesLegacyUserItemRoute()
    {
        var path = "/Users/6af0429e93bd49dfa56162910227d22d/Items/3ed52899c7ffa850617dda69c07207bf";

        Assert.True(TmdbItemActionFilter.TryGetItemIdFromPath(path, out var itemId));
        Assert.Equal(Guid.Parse("3ed52899-c7ff-a850-617d-da69c07207bf"), itemId);
    }

    /// <summary>
    /// Verifies the current /Items/{itemId} route is also recognized.
    /// </summary>
    [Fact]
    public void TryGetItemIdFromPath_ParsesCurrentItemRoute()
    {
        var path = "/Items/3ed52899-c7ff-a850-617d-da69c07207bf";

        Assert.True(TmdbItemActionFilter.TryGetItemIdFromPath(path, out var itemId));
        Assert.Equal(Guid.Parse("3ed52899-c7ff-a850-617d-da69c07207bf"), itemId);
    }

    /// <summary>
    /// Verifies Jellyfin's NotFound() and Problem Details 404 results are treated as missing items.
    /// </summary>
    [Fact]
    public void IsNotFoundResult_AcceptsNotFoundAndProblemDetails()
    {
        Assert.True(TmdbItemActionFilter.IsNotFoundResult(new NotFoundResult()));
        Assert.True(TmdbItemActionFilter.IsNotFoundResult(new NotFoundObjectResult(new { title = "Not Found" })));
        Assert.True(TmdbItemActionFilter.IsNotFoundResult(new ObjectResult(new { title = "Not Found" }) { StatusCode = 404 }));
        Assert.False(TmdbItemActionFilter.IsNotFoundResult(new OkObjectResult(new { })));
        Assert.False(TmdbItemActionFilter.IsNotFoundResult(null));
    }
}
