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
}
