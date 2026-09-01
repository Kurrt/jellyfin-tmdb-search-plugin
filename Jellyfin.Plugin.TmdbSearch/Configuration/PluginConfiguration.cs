using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TmdbSearch.Configuration;

/// <summary>
/// Plugin configuration persisted by Jellyfin.
/// Feature flags default on so upgrades keep current behavior until a setting is saved as off.
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
        EnableTmdbLibrarySearch = true;
        EnableAsyncStreamUi = true;
        EnableImmediateTmdbMetadata = true;
        EnableBackgroundStreamLoading = true;
        EnableHidePageSpinner = true;
        EnableShowPlayBeforeStreams = true;
        EnableNoStreamsOnError = true;
        EnableImmediateSeriesMetadata = true;
        EnableTmdbSeasons = true;
        EnableTmdbEpisodes = true;
        EnableEmptyNextUpForStubs = true;
        EnableGetItemStubFallback = true;
        EnableAccessoryEmptyFallback = true;
        EnableTmdbPosterProxy = true;
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
    /// Gets or sets a value indicating whether movie/series Items search is replaced with TMDB.
    /// Prefix a query with <c>local:</c> to use native Jellyfin search even when this is on.
    /// </summary>
    public bool EnableTmdbLibrarySearch { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the jellyfin-web details-page script is injected.
    /// Requires the File Transformation or JavaScript Injector plugin. Sub-features below only apply when this is on.
    /// </summary>
    public bool EnableAsyncStreamUi { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether item details fetch <c>/TmdbSearch/Items/{id}/Metadata</c>
    /// and paint TMDB metadata before Gelato GetItem finishes.
    /// </summary>
    public bool EnableImmediateTmdbMetadata { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the original GetItem request still runs so Gelato
    /// streams can fill the version panel after metadata is shown.
    /// </summary>
    public bool EnableBackgroundStreamLoading { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the full-page jellyfin-web spinner is suppressed
    /// once TMDB metadata is on screen.
    /// </summary>
    public bool EnableHidePageSpinner { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Play is un-hidden as soon as metadata appears.
    /// The button stays disabled until a real non-<c>/stub</c> stream exists.
    /// </summary>
    public bool EnableShowPlayBeforeStreams { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether GetItem errors, empty sources, or stub-only sources
    /// show "No streams available" in the version panel.
    /// </summary>
    public bool EnableNoStreamsOnError { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether series and season pages return TMDB metadata
    /// immediately instead of waiting on GetItem.
    /// </summary>
    public bool EnableImmediateSeriesMetadata { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether unowned series serve TMDB seasons for
    /// <c>/Shows/{id}/Seasons</c> and series <c>GetItems?ParentId=</c>.
    /// </summary>
    public bool EnableTmdbSeasons { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether unowned seasons serve TMDB episodes for
    /// <c>/Shows/{id}/Episodes</c> and season <c>GetItems?ParentId=</c>.
    /// </summary>
    public bool EnableTmdbEpisodes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Next Up for an unowned TMDB series returns an empty
    /// list so jellyfin-web does not fall through to global continue-watching.
    /// </summary>
    public bool EnableEmptyNextUpForStubs { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a GetItem 404 for a cached TMDB search stub
    /// returns that stub. GetItem is never skipped on the way in.
    /// </summary>
    public bool EnableGetItemStubFallback { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether ThemeMedia, Similar, Ancestors, and SpecialFeatures
    /// 404s for TMDB stubs become empty 200 responses.
    /// </summary>
    public bool EnableAccessoryEmptyFallback { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether item images for unowned TMDB stubs are proxied
    /// from the TMDB CDN.
    /// </summary>
    public bool EnableTmdbPosterProxy { get; set; }
}
