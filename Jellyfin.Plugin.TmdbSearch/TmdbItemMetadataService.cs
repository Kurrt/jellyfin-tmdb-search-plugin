using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TmdbSearch.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Serves TMDB-backed item metadata for unowned search stubs without waiting on Gelato.
/// </summary>
public sealed class TmdbItemMetadataService
{
    private readonly TmdbPosterCache _stubCache;
    private readonly TmdbClient _tmdbClient;
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly ILogger<TmdbItemMetadataService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbItemMetadataService"/> class.
    /// </summary>
    /// <param name="stubCache">Search-stub DTO cache.</param>
    /// <param name="tmdbClient">TMDB HTTP client.</param>
    /// <param name="serverConfigurationManager">Server configuration for language fallback.</param>
    /// <param name="logger">Logger instance.</param>
    public TmdbItemMetadataService(
        TmdbPosterCache stubCache,
        TmdbClient tmdbClient,
        IServerConfigurationManager serverConfigurationManager,
        ILogger<TmdbItemMetadataService> logger)
    {
        _stubCache = stubCache;
        _tmdbClient = tmdbClient;
        _serverConfigurationManager = serverConfigurationManager;
        _logger = logger;
    }

    /// <summary>
    /// Returns metadata for a cached TMDB search stub, enriched from TMDB when possible.
    /// </summary>
    /// <param name="itemId">Synthetic search item id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The details DTO, or null when the id is not a cached stub.</returns>
    public async Task<BaseItemDto?> TryGetAsync(Guid itemId, CancellationToken cancellationToken)
    {
        if (!_stubCache.TryGetDto(itemId, out var stub))
        {
            return null;
        }

        var plugin = Plugin.Instance;
        var config = plugin?.Configuration ?? new PluginConfiguration();
        var language = plugin is null
            ? "en-US"
            : TmdbLanguage.Resolve(config, _serverConfigurationManager);

        TmdbTitleDetails? details = null;
        if (TmdbItemMetadataBuilder.TryGetTmdbId(stub, out var tmdbId)
            && !string.IsNullOrWhiteSpace(config.TmdbApiKey)
            && stub.Type is BaseItemKind.Movie or BaseItemKind.Series)
        {
            details = await _tmdbClient
                .GetTitleDetailsAsync(stub.Type, tmdbId, config, language, cancellationToken)
                .ConfigureAwait(false);
            if (details is null)
            {
                _logger.LogDebug("TMDB details unavailable for stub {ItemId}; returning search metadata", itemId);
            }
        }

        return TmdbItemMetadataBuilder.FromStub(stub, details);
    }
}
