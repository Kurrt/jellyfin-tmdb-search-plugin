using System.Globalization;
using Jellyfin.Plugin.TmdbSearch.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

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
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

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
                Name = Name,
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.config.html",
                    GetType().Namespace)
            }
        ];
    }
}
