using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Turns 404s on accessory item routes into empty 200s when the id is a TMDB search stub.
/// </summary>
public sealed class TmdbStubAccessoryFilter : IAsyncAlwaysRunResultFilter, IOrderedFilter
{
    private readonly TmdbPosterCache _stubCache;
    private readonly ILogger<TmdbStubAccessoryFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbStubAccessoryFilter"/> class.
    /// </summary>
    /// <param name="stubCache">Search-stub cache.</param>
    /// <param name="logger">Logger instance.</param>
    public TmdbStubAccessoryFilter(TmdbPosterCache stubCache, ILogger<TmdbStubAccessoryFilter> logger)
    {
        _stubCache = stubCache;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Runs after GetItem stub serving so details still win, then fills ThemeMedia and similar 404s.
    /// </remarks>
    public int Order => 3;

    /// <inheritdoc />
    public async Task OnResultExecutionAsync(ResultExecutingContext ctx, ResultExecutionDelegate next)
    {
        if (TmdbItemActionFilter.IsNotFoundResult(ctx.Result)
            && TmdbItemActionFilter.TryGetItemIdFromPath(ctx.HttpContext.Request.Path.Value, out var itemId)
            && HasStub(itemId))
        {
            var actionName = ctx.ActionDescriptor is ControllerActionDescriptor descriptor
                ? descriptor.ActionName
                : null;
            if (TmdbStubAccessoryFallback.TryCreate(
                ctx.HttpContext.Request.Path.Value,
                actionName,
                itemId,
                out var fallback))
            {
                _logger.LogDebug(
                    "Serving empty accessory payload for TMDB stub {ItemId} {Path}",
                    itemId,
                    ctx.HttpContext.Request.Path);
                ctx.Result = fallback;
            }
        }

        await next().ConfigureAwait(false);
    }

    private bool HasStub(Guid itemId) =>
        _stubCache.TryGetDto(itemId, out _) || _stubCache.TryGet(itemId, out _);
}
