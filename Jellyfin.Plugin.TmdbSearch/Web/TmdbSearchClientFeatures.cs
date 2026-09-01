using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.TmdbSearch.Configuration;

namespace Jellyfin.Plugin.TmdbSearch.Web;

/// <summary>
/// Client-side feature flags consumed by the injected details-page script.
/// Names are camelCased in JSON to match <c>featureEnabled('immediateTmdbMetadata')</c>.
/// </summary>
public sealed class TmdbSearchClientFeatureFlags
{
    /// <summary>
    /// Gets a value indicating whether getItem is patched to load TMDB metadata first.
    /// </summary>
    public bool ImmediateTmdbMetadata { get; init; }

    /// <summary>
    /// Gets a value indicating whether the original GetItem still runs for Gelato streams.
    /// </summary>
    public bool BackgroundStreamLoading { get; init; }

    /// <summary>
    /// Gets a value indicating whether the page spinner is hidden after metadata appears.
    /// </summary>
    public bool HidePageSpinner { get; init; }

    /// <summary>
    /// Gets a value indicating whether Play is un-hidden before streams exist.
    /// </summary>
    public bool ShowPlayBeforeStreams { get; init; }

    /// <summary>
    /// Gets a value indicating whether GetItem failures show "No streams available".
    /// </summary>
    public bool NoStreamsOnError { get; init; }

    /// <summary>
    /// Gets a value indicating whether series/season pages skip waiting on GetItem.
    /// </summary>
    public bool ImmediateSeriesMetadata { get; init; }
}

/// <summary>
/// Serializes per-feature flags into the injected jellyfin-web script.
/// </summary>
public static class TmdbSearchClientFeatures
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Builds a JavaScript assignment for <c>window.__tmdbsearchFeatures</c>.
    /// </summary>
    /// <param name="config">Plugin configuration to expose to the client.</param>
    /// <returns>A statement the web client can evaluate before the loader script.</returns>
    public static string BuildPreamble(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var flags = new TmdbSearchClientFeatureFlags
        {
            ImmediateTmdbMetadata = config.EnableImmediateTmdbMetadata,
            BackgroundStreamLoading = config.EnableBackgroundStreamLoading,
            HidePageSpinner = config.EnableHidePageSpinner,
            ShowPlayBeforeStreams = config.EnableShowPlayBeforeStreams,
            NoStreamsOnError = config.EnableNoStreamsOnError,
            ImmediateSeriesMetadata = config.EnableImmediateSeriesMetadata,
        };
        var json = JsonSerializer.Serialize(flags, JsonOptions);
        return $"window.__tmdbsearchFeatures={json};";
    }
}
