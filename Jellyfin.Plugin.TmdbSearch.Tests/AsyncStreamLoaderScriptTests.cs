using Jellyfin.Plugin.TmdbSearch.Web;
using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Contract tests for the jellyfin-web metadata-first stream loader.
/// </summary>
public sealed class AsyncStreamLoaderScriptTests
{
    /// <summary>
    /// Loads the embedded client script used for injection.
    /// </summary>
    /// <returns>JavaScript source.</returns>
    private static string LoadScript() => WebInjection.ReadAsyncStreamLoaderScript();

    /// <summary>
    /// Verifies details pages fetch TMDB/stub metadata instead of rewriting GetItem Fields.
    /// Restricting Fields to ChildCount strips Overview and other ItemFields.
    /// </summary>
    [Fact]
    public void Script_LoadsMetadataWithoutRewritingGetItemFields()
    {
        var script = LoadScript();

        Assert.Contains("proto.getItem = function", script, StringComparison.Ordinal);
        Assert.Contains("original.call", script, StringComparison.Ordinal);
        Assert.Contains("TmdbSearch/Items/", script, StringComparison.Ordinal);
        Assert.Contains("/Metadata", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ChildCount,ProviderIds,Path", script, StringComparison.Ordinal);
        Assert.DoesNotContain("withFields", script, StringComparison.Ordinal);
        Assert.DoesNotContain("XMLHttpRequest.prototype.open = function", script, StringComparison.Ordinal);
        Assert.DoesNotContain("proto.fetch = function", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies placeholder /stub sources never enable Play, and real sources are merged onto the same item object.
    /// </summary>
    [Fact]
    public void Script_MergesPlayableStreamsAndGatesPlayUntilReady()
    {
        var script = LoadScript();

        Assert.Contains("function isPlaceholderSource", script, StringComparison.Ordinal);
        Assert.Contains("function hasPlayableSources", script, StringComparison.Ordinal);
        Assert.Contains("/stub", script, StringComparison.Ordinal);
        Assert.Contains("item.MediaSources", script, StringComparison.Ordinal);
        Assert.Contains("tmdbsearch-streams-pending", script, StringComparison.Ordinal);
        Assert.Contains("tmdbsearch-streams-ready", script, StringComparison.Ordinal);
        Assert.Contains("tmdbsearch-sources-loading", script, StringComparison.Ordinal);
        Assert.Contains("No streams available", script, StringComparison.Ordinal);
        Assert.Contains("isCurrentPage", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "streamsReady = !!(sources && sources.length && full.LocationType !== 'Virtual')",
            script,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the page-level jellyfin-web spinner is hidden once TMDB metadata is shown.
    /// Stream timeouts belong to AIOStreams/Gelato; this patch only hides the overlay.
    /// </summary>
    [Fact]
    public void Script_HidesPageSpinnerWhenMetadataIsShown()
    {
        var script = LoadScript();

        Assert.Contains("function hidePageSpinner", script, StringComparison.Ordinal);
        Assert.Contains("tmdbsearch-hide-docspinner", script, StringComparison.Ordinal);
        Assert.Contains(".docspinner", script, StringComparison.Ordinal);
        Assert.Contains(".mdl-spinner", script, StringComparison.Ordinal);
        Assert.Contains("mdlSpinnerActive", script, StringComparison.Ordinal);
        Assert.Contains("window.Loading", script, StringComparison.Ordinal);
        Assert.Contains("function restorePageSpinnerIfLeftItem", script, StringComparison.Ordinal);
        Assert.Contains("function revealPlayButton", script, StringComparison.Ordinal);
        Assert.Contains("btnPlay", script, StringComparison.Ordinal);
        Assert.DoesNotContain("window.loading;", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies GetItem errors and empty/stub sources stop the stream spinner.
    /// Does not add a homemade AIOStreams timeout.
    /// </summary>
    [Fact]
    public void Script_TreatsGetItemErrorsAndEmptySourcesAsNoStreams()
    {
        var script = LoadScript();

        Assert.Contains("Promise.resolve(original.call", script, StringComparison.Ordinal);
        Assert.Contains(".catch(function () {", script, StringComparison.Ordinal);
        Assert.Contains("return null;", script, StringComparison.Ordinal);
        Assert.Contains("showNoStreams", script, StringComparison.Ordinal);
        Assert.Contains("hasPlayableSources(full)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("25000", script, StringComparison.Ordinal);
        Assert.DoesNotContain("30000", script, StringComparison.Ordinal);
        Assert.DoesNotContain("AbortController", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies series/season pages return TMDB metadata immediately so ChildCount and
    /// overview are not replaced by a hanging or thin GetItem stub.
    /// </summary>
    [Fact]
    public void Script_ReturnsSeriesMetadataWithoutWaitingOnGetItem()
    {
        var script = LoadScript();

        Assert.Contains("type === 'Movie' || type === 'Episode'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("type !== 'Movie' && type !== 'Episode'", script, StringComparison.Ordinal);
        Assert.Contains("return meta;", script, StringComparison.Ordinal);
    }
}
