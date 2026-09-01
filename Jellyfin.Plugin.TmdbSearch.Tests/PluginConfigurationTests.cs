using System.Xml.Serialization;
using Jellyfin.Plugin.TmdbSearch.Configuration;
using Jellyfin.Plugin.TmdbSearch.Web;
using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for per-feature plugin settings defaults, persistence, and the config page.
/// </summary>
public sealed class PluginConfigurationTests
{
    /// <summary>
    /// Feature ids that must exist as checkboxes and be saved from the config page.
    /// </summary>
    public static readonly string[] FeatureIds =
    [
        "EnableTmdbLibrarySearch",
        "EnableAsyncStreamUi",
        "EnableImmediateTmdbMetadata",
        "EnableBackgroundStreamLoading",
        "EnableHidePageSpinner",
        "EnableShowPlayBeforeStreams",
        "EnableNoStreamsOnError",
        "EnableImmediateSeriesMetadata",
        "EnableTmdbSeasons",
        "EnableTmdbEpisodes",
        "EnableEmptyNextUpForStubs",
        "EnableGetItemStubFallback",
        "EnableAccessoryEmptyFallback",
        "EnableTmdbPosterProxy",
    ];

    /// <summary>
    /// Verifies every feature starts on so upgrades keep current behavior.
    /// </summary>
    [Fact]
    public void Constructor_EnablesEveryFeature()
    {
        var config = new PluginConfiguration();

        Assert.True(config.EnableTmdbLibrarySearch);
        Assert.True(config.EnableAsyncStreamUi);
        Assert.True(config.EnableImmediateTmdbMetadata);
        Assert.True(config.EnableBackgroundStreamLoading);
        Assert.True(config.EnableHidePageSpinner);
        Assert.True(config.EnableShowPlayBeforeStreams);
        Assert.True(config.EnableNoStreamsOnError);
        Assert.True(config.EnableImmediateSeriesMetadata);
        Assert.True(config.EnableTmdbSeasons);
        Assert.True(config.EnableTmdbEpisodes);
        Assert.True(config.EnableEmptyNextUpForStubs);
        Assert.True(config.EnableGetItemStubFallback);
        Assert.True(config.EnableAccessoryEmptyFallback);
        Assert.True(config.EnableTmdbPosterProxy);
    }

    /// <summary>
    /// Verifies XmlSerializer constructor defaults survive configs saved before these flags existed.
    /// </summary>
    [Fact]
    public void XmlDeserialize_MissingFeatureElements_KeepConstructorDefaults()
    {
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        using var reader = new StringReader("<PluginConfiguration><TmdbApiKey>abc</TmdbApiKey></PluginConfiguration>");
        var deserialized = serializer.Deserialize(reader);
        var config = Assert.IsType<PluginConfiguration>(deserialized);

        Assert.Equal("abc", config.TmdbApiKey);
        Assert.True(config.EnableTmdbLibrarySearch);
        Assert.True(config.EnableAsyncStreamUi);
        Assert.True(config.EnableImmediateTmdbMetadata);
        Assert.True(config.EnableTmdbSeasons);
        Assert.True(config.EnableGetItemStubFallback);
        Assert.True(config.EnableTmdbPosterProxy);
    }

    /// <summary>
    /// Verifies saved false values round-trip through XML.
    /// </summary>
    [Fact]
    public void XmlSerialize_PersistsDisabledFlags()
    {
        var serializer = new XmlSerializer(typeof(PluginConfiguration));
        var original = new PluginConfiguration
        {
            EnableTmdbSeasons = false,
            EnableHidePageSpinner = false,
        };
        using var writer = new StringWriter();
        serializer.Serialize(writer, original);
        using var reader = new StringReader(writer.ToString());
        var deserialized = serializer.Deserialize(reader);
        var config = Assert.IsType<PluginConfiguration>(deserialized);

        Assert.False(config.EnableTmdbSeasons);
        Assert.False(config.EnableHidePageSpinner);
        Assert.True(config.EnableTmdbEpisodes);
    }

    /// <summary>
    /// Verifies test overrides apply only inside the scope.
    /// </summary>
    [Fact]
    public void OverrideCurrent_RestoresPreviousConfiguration()
    {
        Assert.True(PluginSettings.Current.EnableTmdbSeasons);
        using (PluginSettings.OverrideCurrent(new PluginConfiguration { EnableTmdbSeasons = false }))
        {
            Assert.False(PluginSettings.Current.EnableTmdbSeasons);
        }

        Assert.True(PluginSettings.Current.EnableTmdbSeasons);
    }

    /// <summary>
    /// Verifies the settings page names every feature toggle with a description.
    /// </summary>
    [Fact]
    public void ConfigPage_DeclaresEveryFeatureToggle()
    {
        var html = ReadEmbedded("Configuration.config.html");

        foreach (var id in FeatureIds)
        {
            Assert.Contains($"id=\"{id}\"", html, StringComparison.Ordinal);
            Assert.Contains($"name=\"{id}\"", html, StringComparison.Ordinal);
            Assert.Contains($"'{id}'", html, StringComparison.Ordinal);
        }

        Assert.Contains("featureIds:", html, StringComparison.Ordinal);
        Assert.Contains("config[id] !== false", html, StringComparison.Ordinal);
        Assert.Contains("config[id] = document.querySelector('#' + id).checked", html, StringComparison.Ordinal);
        Assert.Contains("Replace library search with TMDB", html, StringComparison.Ordinal);
        Assert.Contains("Inject the details-page script", html, StringComparison.Ordinal);
        Assert.Contains("Show TMDB metadata immediately", html, StringComparison.Ordinal);
        Assert.Contains("Load streams in the background", html, StringComparison.Ordinal);
        Assert.Contains("Hide the page spinner after metadata appears", html, StringComparison.Ordinal);
        Assert.Contains("Show Play immediately", html, StringComparison.Ordinal);
        Assert.Contains("No streams available", html, StringComparison.Ordinal);
        Assert.Contains("Show series and season metadata without waiting on GetItem", html, StringComparison.Ordinal);
        Assert.Contains("Serve TMDB seasons for unowned series", html, StringComparison.Ordinal);
        Assert.Contains("Serve TMDB episodes for unowned seasons", html, StringComparison.Ordinal);
        Assert.Contains("Return empty Next Up for unowned series", html, StringComparison.Ordinal);
        Assert.Contains("Return a stub item when GetItem 404s", html, StringComparison.Ordinal);
        Assert.Contains("Return empty lists for theme songs, similar, and ancestors", html, StringComparison.Ordinal);
        Assert.Contains("Proxy TMDB posters for unowned titles", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies client preamble JSON uses camelCase names matching the loader script.
    /// </summary>
    [Fact]
    public void BuildPreamble_UsesCamelCaseFlagNames()
    {
        var preamble = TmdbSearchClientFeatures.BuildPreamble(new PluginConfiguration
        {
            EnableImmediateTmdbMetadata = false,
        });

        Assert.Equal(
            "window.__tmdbsearchFeatures={\"immediateTmdbMetadata\":false,\"backgroundStreamLoading\":true,\"hidePageSpinner\":true,\"showPlayBeforeStreams\":true,\"noStreamsOnError\":true,\"immediateSeriesMetadata\":true};",
            preamble);
    }

    /// <summary>
    /// Reads an embedded plugin resource by suffix.
    /// </summary>
    /// <param name="suffix">Resource name suffix.</param>
    /// <returns>UTF-8 resource text.</returns>
    private static string ReadEmbedded(string suffix)
    {
        var assembly = typeof(WebInjection).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal));
        if (resourceName is null)
        {
            Assert.Fail($"Embedded resource ending with {suffix} was not found.");
            return string.Empty;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            Assert.Fail($"Embedded resource {resourceName} could not be opened.");
            return string.Empty;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
