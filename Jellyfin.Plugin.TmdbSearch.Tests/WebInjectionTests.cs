using Jellyfin.Plugin.TmdbSearch.Configuration;
using Jellyfin.Plugin.TmdbSearch.Web;
using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for injecting the async stream loader into jellyfin-web HTML.
/// </summary>
public sealed class WebInjectionTests
{
    /// <summary>
    /// Verifies the loader is inserted before the closing body tag.
    /// </summary>
    [Fact]
    public void InjectScript_InsertsBeforeBodyClose()
    {
        const string html = "<html><body><div id=\"app\"></div></body></html>";
        var injected = WebInjection.InjectScript(html, "window.TMDBSEARCH=1;");

        Assert.Contains($"id=\"{WebInjection.ScriptElementId}\"", injected, StringComparison.Ordinal);
        Assert.Contains("window.TMDBSEARCH=1;", injected, StringComparison.Ordinal);
        Assert.Contains($"<script id=\"{WebInjection.ScriptElementId}\">window.TMDBSEARCH=1;</script></body>", injected, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a second inject is a no-op so File Transformation stays idempotent.
    /// </summary>
    [Fact]
    public void InjectScript_IsIdempotent()
    {
        const string html = "<html><body></body></html>";
        var first = WebInjection.InjectScript(html, "a();");
        var second = WebInjection.InjectScript(first, "b();");

        Assert.Equal(first, second);
        Assert.Contains("a();", second, StringComparison.Ordinal);
        Assert.DoesNotContain("b();", second, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies strip removes the injected tag when the feature is disabled.
    /// </summary>
    [Fact]
    public void StripInjectedScript_RemovesLoaderTag()
    {
        const string html = "<html><body><p>ok</p></body></html>";
        var injected = WebInjection.InjectScript(html, "a();");
        var stripped = WebInjection.StripInjectedScript(injected);

        Assert.DoesNotContain(WebInjection.ScriptElementId, stripped, StringComparison.Ordinal);
        Assert.Contains("<p>ok</p>", stripped, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies the File Transformation callback payload is read from Contents.
    /// </summary>
    [Fact]
    public void ApplyTransformation_InjectsFromTypedPayload()
    {
        var payload = new WebInjection.FileTransformationPayload
        {
            Contents = "<html><body></body></html>",
        };

        var transformed = WebInjection.ApplyTransformation(payload, enabled: true, new PluginConfiguration());

        Assert.Contains(WebInjection.ScriptElementId, transformed, StringComparison.Ordinal);
        Assert.Contains("window.__tmdbsearchFeatures=", transformed, StringComparison.Ordinal);
        Assert.Contains("proto.getItem = function", transformed, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies disabled client flags are serialized into the injected preamble.
    /// </summary>
    [Fact]
    public void ApplyTransformation_EmbedsDisabledClientFlags()
    {
        var payload = new WebInjection.FileTransformationPayload
        {
            Contents = "<html><body></body></html>",
        };
        var config = new PluginConfiguration
        {
            EnableHidePageSpinner = false,
            EnableShowPlayBeforeStreams = false,
        };

        var transformed = WebInjection.ApplyTransformation(payload, enabled: true, config);

        Assert.Contains("\"hidePageSpinner\":false", transformed, StringComparison.Ordinal);
        Assert.Contains("\"showPlayBeforeStreams\":false", transformed, StringComparison.Ordinal);
        Assert.Contains("\"immediateTmdbMetadata\":true", transformed, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a disabled transform strips a previously injected loader.
    /// </summary>
    [Fact]
    public void ApplyTransformation_StripsWhenDisabled()
    {
        var payload = new WebInjection.FileTransformationPayload
        {
            Contents = WebInjection.InjectScript("<html><body></body></html>", "a();"),
        };

        var transformed = WebInjection.ApplyTransformation(payload, enabled: false);

        Assert.DoesNotContain(WebInjection.ScriptElementId, transformed, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies configuration default keeps async stream UI on.
    /// </summary>
    [Fact]
    public void PluginConfiguration_EnablesAsyncStreamUiByDefault()
    {
        var config = new PluginConfiguration();
        Assert.True(config.EnableAsyncStreamUi);
        Assert.True(config.EnableTmdbLibrarySearch);
        Assert.True(config.EnableImmediateTmdbMetadata);
    }
}
