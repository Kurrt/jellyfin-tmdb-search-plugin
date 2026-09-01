using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Serves cached TMDB search stubs for GetItem when Gelato has not materialized the item.
/// </summary>
public sealed class TmdbItemActionFilter : IAsyncActionFilter, IOrderedFilter
{
    private static readonly HashSet<string> ItemDetailActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "GetItem",
        "GetItemLegacy",
    };

    private static readonly string[] RouteItemIdKeys =
    [
        "itemId",
        "ItemId",
        "id",
        "Id",
    ];

    private readonly TmdbPosterCache _stubCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbItemActionFilter"/> class.
    /// </summary>
    /// <param name="stubCache">Search-stub DTO cache.</param>
    public TmdbItemActionFilter(TmdbPosterCache stubCache)
    {
        _stubCache = stubCache;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Runs after Gelato's insert filter (order 1) so a successful materialization still wins.
    /// </remarks>
    public int Order => 2;

    /// <summary>
    /// Returns true when the MVC action is a single-item detail endpoint.
    /// </summary>
    /// <param name="actionName">The current action name.</param>
    /// <returns>True for GetItem and GetItemLegacy.</returns>
    public static bool IsItemDetailAction(string? actionName) =>
        actionName is not null && ItemDetailActions.Contains(actionName);

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        if (ctx.ActionDescriptor is not ControllerActionDescriptor descriptor
            || !IsItemDetailAction(descriptor.ActionName)
            || !TryGetItemId(ctx, out var itemId)
            || !_stubCache.TryGetDto(itemId, out var dto))
        {
            await next().ConfigureAwait(false);
            return;
        }

        ctx.Result = new OkObjectResult(dto);
    }

    private static bool TryGetItemId(ActionExecutingContext ctx, out Guid itemId)
    {
        itemId = Guid.Empty;

        foreach (var key in RouteItemIdKeys)
        {
            if (ctx.RouteData.Values.TryGetValue(key, out var raw)
                && raw is not null
                && Guid.TryParse(raw.ToString(), out itemId)
                && itemId != Guid.Empty)
            {
                return true;
            }
        }

        if (ctx.TryGetActionArgument("itemId", out Guid argumentId) && argumentId != Guid.Empty)
        {
            itemId = argumentId;
            return true;
        }

        return false;
    }
}
