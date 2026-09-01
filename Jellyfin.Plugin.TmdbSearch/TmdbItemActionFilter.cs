using System.Text.RegularExpressions;
using Jellyfin.Plugin.TmdbSearch.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Serves cached TMDB search stubs for GetItem when Gelato has not materialized the item.
/// </summary>
public sealed class TmdbItemActionFilter : IAsyncActionFilter, IAsyncResultFilter, IOrderedFilter
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

    private static readonly Regex ItemIdPathRegex = new(
        @"/Items/([0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12})(?:/|\?|$)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    private readonly TmdbPosterCache _stubCache;
    private readonly ILogger<TmdbItemActionFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbItemActionFilter"/> class.
    /// </summary>
    /// <param name="stubCache">Search-stub DTO cache.</param>
    /// <param name="logger">Logger instance.</param>
    public TmdbItemActionFilter(TmdbPosterCache stubCache, ILogger<TmdbItemActionFilter> logger)
    {
        _stubCache = stubCache;
        _logger = logger;
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

    /// <summary>
    /// Returns true when an MVC result is a 404, including Problem Details payloads.
    /// </summary>
    /// <param name="result">The action result to inspect.</param>
    /// <returns>True when the result reports HTTP 404.</returns>
    public static bool IsNotFoundResult(IActionResult? result) =>
        result is IStatusCodeActionResult { StatusCode: 404 };

    /// <summary>
    /// Parses a stub item id from Jellyfin item detail paths.
    /// </summary>
    /// <param name="path">Request path such as /Users/{userId}/Items/{itemId}.</param>
    /// <param name="itemId">The parsed item id when successful.</param>
    /// <returns>True when the path contains a GUID after /Items/.</returns>
    public static bool TryGetItemIdFromPath(string? path, out Guid itemId)
    {
        itemId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var match = ItemIdPathRegex.Match(path);
            if (!match.Success)
            {
                return false;
            }

            return Guid.TryParse(match.Groups[1].Value, out itemId) && itemId != Guid.Empty;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Always continue into GetItem. Gelato's insert filter (order 1) materializes the
    /// library item; skipping the controller here would return the cached Path=/stub DTO
    /// and prevent SyncStreams from running.
    /// </remarks>
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        await next().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task OnResultExecutionAsync(ResultExecutingContext ctx, ResultExecutionDelegate next)
    {
        if (IsNotFoundResult(ctx.Result)
            && ctx.ActionDescriptor is ControllerActionDescriptor descriptor)
        {
            TryServeStub(ctx, descriptor, result => ctx.Result = result);
        }

        await next().ConfigureAwait(false);
    }

    private bool TryServeStub(
        ActionContext ctx,
        ControllerActionDescriptor descriptor,
        Action<IActionResult> setResult)
    {
        if (!PluginSettings.Current.EnableGetItemStubFallback
            || !IsItemDetailAction(descriptor.ActionName)
            || !TryGetItemId(ctx, out var itemId)
            || !_stubCache.TryGetDto(itemId, out var dto))
        {
            return false;
        }

        _logger.LogInformation("Serving cached TMDB search stub for GetItem {ItemId}", itemId);
        setResult(new OkObjectResult(dto));
        return true;
    }

    private static bool TryGetItemId(ActionContext ctx, out Guid itemId)
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

        if (ctx is ActionExecutingContext executing
            && executing.ActionArguments.TryGetValue("itemId", out var argument)
            && argument is Guid guidArgument
            && guidArgument != Guid.Empty)
        {
            itemId = guidArgument;
            return true;
        }

        return TryGetItemIdFromPath(ctx.HttpContext.Request.Path.Value, out itemId);
    }
}
