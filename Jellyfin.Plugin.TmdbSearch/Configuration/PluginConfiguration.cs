using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TmdbSearch.Configuration;

/// <summary>
/// Plugin configuration persisted by Jellyfin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        TmdbApiKey = string.Empty;
        Language = string.Empty;
        IncludeAdult = false;
        CacheTtlSeconds = 600;
        EnableAsyncStreamUi = true;
    }

    /// <summary>
    /// Gets or sets the TMDB API key (v3).
    /// </summary>
    public string TmdbApiKey { get; set; }

    /// <summary>
    /// Gets or sets the TMDB language code (e.g. en-US). Empty uses server metadata language.
    /// </summary>
    public string Language { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether adult content is included in TMDB results.
    /// </summary>
    public bool IncludeAdult { get; set; }

    /// <summary>
    /// Gets or sets the in-memory search query cache TTL in seconds.
    /// </summary>
    public int CacheTtlSeconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether jellyfin-web should load streams asynchronously on item details.
    /// </summary>
    public bool EnableAsyncStreamUi { get; set; }
}
