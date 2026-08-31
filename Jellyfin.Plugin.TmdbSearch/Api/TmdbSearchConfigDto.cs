using Jellyfin.Plugin.TmdbSearch.Configuration;

namespace Jellyfin.Plugin.TmdbSearch.Api;

/// <summary>
/// JSON transport model for the TMDB Search settings API.
/// </summary>
public sealed class TmdbSearchConfigDto
{
    /// <summary>
    /// Gets or sets the TMDB API key.
    /// </summary>
    public string TmdbApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TMDB language code.
    /// </summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether adult content is included.
    /// </summary>
    public bool IncludeAdult { get; set; }

    /// <summary>
    /// Gets or sets the search cache TTL in seconds.
    /// </summary>
    public int CacheTtlSeconds { get; set; } = 600;

    /// <summary>
    /// Gets or sets a value indicating whether a TMDB API key is configured.
    /// </summary>
    public bool HasApiKey { get; set; }

    /// <summary>
    /// Maps plugin configuration to a DTO for the settings API.
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <returns>The settings DTO.</returns>
    public static TmdbSearchConfigDto From(PluginConfiguration config)
    {
        return new TmdbSearchConfigDto
        {
            TmdbApiKey = config.TmdbApiKey ?? string.Empty,
            Language = config.Language ?? string.Empty,
            IncludeAdult = config.IncludeAdult,
            CacheTtlSeconds = config.CacheTtlSeconds > 0 ? config.CacheTtlSeconds : 600,
            HasApiKey = !string.IsNullOrWhiteSpace(config.TmdbApiKey),
        };
    }

    /// <summary>
    /// Applies DTO values onto an existing plugin configuration instance.
    /// </summary>
    /// <param name="target">The configuration to update.</param>
    public void ApplyTo(PluginConfiguration target)
    {
        target.TmdbApiKey = TmdbApiKey.Trim();
        target.Language = Language.Trim();
        target.IncludeAdult = IncludeAdult;
        target.CacheTtlSeconds = CacheTtlSeconds > 0 ? CacheTtlSeconds : 600;
    }
}
