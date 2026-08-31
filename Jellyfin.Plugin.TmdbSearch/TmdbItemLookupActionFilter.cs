using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Rewrites a direct item lookup for a stale TMDB search-stub GUID onto the real library item,
/// once Gelato has materialized one, instead of letting it 404 forever on the dead stub id.
/// </summary>
/// <remarks>
/// See <see cref="TmdbStubRegistry"/> for why this is necessary: a title materializes into the
/// library under a new id as a side effect of opening its details page, but the browser is
/// already sitting on the stub id from the search result and nothing else tells it the id
/// changed. This filter is the fix — it runs ahead of Jellyfin's own item controller and swaps
/// the requested id for the real one whenever it can, so the request the client is already
/// making just starts working instead of requiring a fresh search to "shake loose".
/// </remarks>
public sealed class TmdbItemLookupActionFilter : IAsyncActionFilter, IOrderedFilter
{
    private readonly TmdbStubRegistry _stubRegistry;
    private readonly TmdbLibraryIndex _libraryIndex;
    private readonly ILogger<TmdbItemLookupActionFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbItemLookupActionFilter"/> class.
    /// </summary>
    /// <param name="stubRegistry">Stub GUID → TMDB id lookup.</param>
    /// <param name="libraryIndex">TMDB id → real library item id lookup.</param>
    /// <param name="logger">Logger instance.</param>
    public TmdbItemLookupActionFilter(
        TmdbStubRegistry stubRegistry,
        TmdbLibraryIndex libraryIndex,
        ILogger<TmdbItemLookupActionFilter> logger)
    {
        _stubRegistry = stubRegistry;
        _libraryIndex = libraryIndex;
        _logger = logger;
    }

    /// <inheritdoc />
    public int Order => -1;

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        if (!ctx.IsApiItemLookupAction()
            || !ctx.TryGetActionArgument("itemId", out Guid requestedId)
            || requestedId == Guid.Empty)
        {
            await next().ConfigureAwait(false);
            return;
        }

        if (_stubRegistry.TryGetTmdbId(requestedId, out var kind, out var tmdbId)
            && _libraryIndex.TryGetItemId(kind, tmdbId, out var realId)
            && realId != requestedId)
        {
            _logger.LogInformation(
                "TMDB Search: stub {StubId} is now owned as {RealId} — rewriting item lookup",
                requestedId,
                realId);

            ctx.ActionArguments["itemId"] = realId;
            ctx.RouteData.Values["itemId"] = realId.ToString("N");
        }

        await next().ConfigureAwait(false);
    }
}
