using System.Globalization;
using Jellyfin.Plugin.TmdbSearch.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Main plugin entry point for TMDB-backed library search.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Stable plugin identifier used by Jellyfin and the config page.
    /// </summary>
    public static readonly Guid PluginId = Guid.Parse("a8f3c2e1-4b5d-6e7f-8a9b-0c1d2e3f4a5b");

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Server application paths.</param>
    /// <param name="xmlSerializer">XML serializer for plugin configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        _logger = logger;
    }

    private readonly ILogger<Plugin> _logger;

    /// <inheritdoc />
    public override string Name => "TMDB Search";

    /// <inheritdoc />
    public override string Description =>
        "Replaces library Items search with direct TMDB lookup for movies and series.";

    /// <inheritdoc />
    public override Guid Id => PluginId;

    /// <summary>
    /// Gets the current plugin singleton instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "config",
                DisplayName = Name,
                EnableInMainMenu = true,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.config.html",
                    GetType().Namespace)
            }
        ];
    }

    /// <inheritdoc />
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        var config = (PluginConfiguration)configuration;
        base.UpdateConfiguration(config);

        _logger.LogInformation(
            "TMDB Search configuration updated (api key configured: {HasKey})",
            !string.IsNullOrWhiteSpace(config.TmdbApiKey));
    }
}
