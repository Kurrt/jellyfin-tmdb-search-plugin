using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for recognizing Jellyfin item image MVC actions.
/// </summary>
public sealed class TmdbImageResourceFilterTests
{
    /// <summary>
    /// Verifies the image endpoints Gelato and the web client use are intercepted.
    /// </summary>
    [Theory]
    [InlineData("GetItemImage")]
    [InlineData("GetItemImageByIndex")]
    [InlineData("GetItemImage2")]
    public void IsItemImageAction_AcceptsJellyfinImageActions(string actionName)
    {
        Assert.True(TmdbImageResourceFilter.IsItemImageAction(actionName));
    }

    /// <summary>
    /// Verifies unrelated endpoints are left to Jellyfin.
    /// </summary>
    [Theory]
    [InlineData("GetItems")]
    [InlineData("GetItem")]
    [InlineData(null)]
    [InlineData("")]
    public void IsItemImageAction_IgnoresNonImageActions(string? actionName)
    {
        Assert.False(TmdbImageResourceFilter.IsItemImageAction(actionName));
    }
}
