using Jellyfin.Plugin.TmdbSearch.Configuration;
using MediaBrowser.Controller.Configuration;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Resolves the TMDB language code from plugin config or the server default.
/// </summary>
internal static class TmdbLanguage
{
    /// <summary>
    /// Returns the configured language, or the server metadata language, or en-US.
    /// </summary>
    /// <param name="config">Plugin configuration.</param>
    /// <param name="serverConfigurationManager">Jellyfin server configuration.</param>
    /// <returns>A TMDB language code.</returns>
    public static string Resolve(
        PluginConfiguration config,
        IServerConfigurationManager serverConfigurationManager)
    {
        if (!string.IsNullOrWhiteSpace(config.Language))
        {
            return config.Language;
        }

        var lang = serverConfigurationManager.Configuration.PreferredMetadataLanguage;
        return string.IsNullOrWhiteSpace(lang) ? "en-US" : lang;
    }
}
