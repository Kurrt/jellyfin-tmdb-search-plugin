using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Serves TMDB seasons/episodes and empty Next Up before Jellyfin 404s or user-wide continue-watching.
/// </summary>
public sealed class TmdbShowChildrenActionFilter : IAsyncActionFilter, IAsyncAlwaysRunResultFilter, IOrderedFilter
{
    private readonly TmdbShowChildrenService _children;
    private readonly ILogger<TmdbShowChildrenActionFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbShowChildrenActionFilter"/> class.
    /// </summary>
    /// <param name="children">Show children service.</param>
    /// <param name="logger">Logger instance.</param>
    public TmdbShowChildrenActionFilter(
        TmdbShowChildrenService children,
        ILogger<TmdbShowChildrenActionFilter> logger)
    {
        _children = children;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Runs at the same time as search, before Gelato's insert filter (order 1),
    /// so GetSeasons is not blocked by a hanging AIOStreams GetItem-style insert.
    /// </remarks>
    public int Order => 0;

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        if (HasSearchTerm(ctx)
            || !TmdbShowChildrenRoute.TryMatch(
                ctx.HttpContext.Request.Path.Value,
                ctx.GetActionName(),
                ReadGuid(ctx, "seriesId", "SeriesId", "id", "Id"),
                ReadGuid(ctx, "seasonId", "SeasonId"),
                ReadGuid(ctx, "parentId", "ParentId"),
                out var match))
        {
            await next().ConfigureAwait(false);
            return;
        }

        var result = await _children
            .TryCreateAsync(match, ctx.HttpContext.RequestAborted)
            .ConfigureAwait(false);
        if (result is null)
        {
            await next().ConfigureAwait(false);
            return;
        }

        _logger.LogDebug(
            "Serving TMDB {Kind} payload for {SeriesId} parent {ParentId}",
            match.Kind,
            match.SeriesId,
            match.ParentId);
        ctx.Result = result;
    }

    /// <inheritdoc />
    public async Task OnResultExecutionAsync(ResultExecutingContext ctx, ResultExecutionDelegate next)
    {
        if (TmdbItemActionFilter.IsNotFoundResult(ctx.Result)
            && TmdbShowChildrenRoute.TryMatch(
                ctx.HttpContext.Request.Path.Value,
                ctx.ActionDescriptor is ControllerActionDescriptor descriptor
                    ? descriptor.ActionName
                    : null,
                ReadGuid(ctx, "seriesId", "SeriesId", "id", "Id"),
                ReadGuid(ctx, "seasonId", "SeasonId"),
                ReadGuid(ctx, "parentId", "ParentId"),
                out var match))
        {
            var result = await _children
                .TryCreateAsync(match, ctx.HttpContext.RequestAborted)
                .ConfigureAwait(false);
            if (result is not null)
            {
                ctx.Result = result;
            }
        }

        await next().ConfigureAwait(false);
    }

    private static bool HasSearchTerm(ActionExecutingContext ctx) =>
        ctx.IsApiSearchAction()
        && ctx.TryGetActionArgument("searchTerm", out string? searchTerm)
        && !string.IsNullOrWhiteSpace(searchTerm);

    private static Guid? ReadGuid(ActionContext ctx, params string[] keys)
    {
        var executing = ctx as ActionExecutingContext;
        foreach (var key in keys)
        {
            if (executing is not null
                && executing.TryGetActionArgument(key, out Guid guid)
                && guid != Guid.Empty)
            {
                return guid;
            }

            if (executing is not null
                && executing.TryGetActionArgument(key, out Guid? nullable)
                && nullable is { } value
                && value != Guid.Empty)
            {
                return value;
            }

            if (ctx.RouteData.Values.TryGetValue(key, out var routeRaw)
                && routeRaw is not null
                && Guid.TryParse(routeRaw.ToString(), out var routeGuid)
                && routeGuid != Guid.Empty)
            {
                return routeGuid;
            }

            var query = ctx.HttpContext.Request.Query[key].FirstOrDefault();
            if (Guid.TryParse(query, out var queryGuid) && queryGuid != Guid.Empty)
            {
                return queryGuid;
            }
        }

        return null;
    }
}
