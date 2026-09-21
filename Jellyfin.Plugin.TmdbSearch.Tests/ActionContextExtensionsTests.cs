using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for Jellyfin 12 search MVC action recognition.
/// </summary>
public sealed class ActionContextExtensionsTests
{
    /// <summary>
    /// Verifies Items search and Search/Hints are both intercepted.
    /// </summary>
    [Theory]
    [InlineData("GetItems")]
    [InlineData("GetItemsByUserIdLegacy")]
    [InlineData("GetSearchHints")]
    public void IsApiSearchActionName_AcceptsJellyfin12SearchActions(string actionName)
    {
        Assert.True(ActionContextExtensions.IsApiSearchActionName(actionName));
    }

    /// <summary>
    /// Verifies unrelated library actions are left to Jellyfin.
    /// </summary>
    [Theory]
    [InlineData("GetItem")]
    [InlineData("GetSeasons")]
    [InlineData("GetItemImage")]
    [InlineData(null)]
    [InlineData("")]
    public void IsApiSearchActionName_IgnoresNonSearchActions(string? actionName)
    {
        Assert.False(ActionContextExtensions.IsApiSearchActionName(actionName));
    }
}
