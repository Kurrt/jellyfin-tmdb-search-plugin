using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Extension methods for ASP.NET action filter context used by search interception.
/// </summary>
public static class ActionContextExtensions
{
    private static readonly HashSet<string> ItemSearchActionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "GetItems",
        "GetItemsByUserIdLegacy",
    };

    private static readonly HashSet<string> SearchHintActionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "GetSearchHints",
    };

    /// <summary>
    /// Gets the MVC action name for the current request.
    /// </summary>
    /// <param name="ctx">The action executing context.</param>
    /// <returns>The action name, or null if unavailable.</returns>
    public static string? GetActionName(this ActionExecutingContext ctx) =>
        (ctx.ActionDescriptor as ControllerActionDescriptor)?.ActionName;

    /// <summary>
    /// Returns true when the action is a Jellyfin Items search endpoint.
    /// </summary>
    /// <param name="ctx">The action executing context.</param>
    /// <returns>True for GetItems and GetItemsByUserIdLegacy.</returns>
    public static bool IsItemsSearchAction(this ActionExecutingContext ctx) =>
        ctx.GetActionName() is { } actionName && ItemSearchActionNames.Contains(actionName);

    /// <summary>
    /// Returns true when the action is Jellyfin 12 Search/Hints typeahead.
    /// </summary>
    /// <param name="ctx">The action executing context.</param>
    /// <returns>True for GetSearchHints.</returns>
    public static bool IsSearchHintsAction(this ActionExecutingContext ctx) =>
        ctx.GetActionName() is { } actionName && SearchHintActionNames.Contains(actionName);

    /// <summary>
    /// Returns true when the action is a search endpoint this plugin should intercept.
    /// </summary>
    /// <param name="ctx">The action executing context.</param>
    /// <returns>True for Items search and Search/Hints.</returns>
    public static bool IsApiSearchAction(this ActionExecutingContext ctx) =>
        ctx.IsItemsSearchAction() || ctx.IsSearchHintsAction();

    /// <summary>
    /// Returns true when the MVC action name is a known search endpoint.
    /// </summary>
    /// <param name="actionName">The current action name.</param>
    /// <returns>True for GetItems, GetItemsByUserIdLegacy, and GetSearchHints.</returns>
    public static bool IsApiSearchActionName(string? actionName) =>
        actionName is not null
        && (ItemSearchActionNames.Contains(actionName) || SearchHintActionNames.Contains(actionName));

    /// <summary>
    /// Tries to read a typed action argument from the model binder.
    /// </summary>
    /// <typeparam name="T">The expected argument type.</typeparam>
    /// <param name="ctx">The action executing context.</param>
    /// <param name="key">The argument name.</param>
    /// <param name="value">The parsed value when successful.</param>
    /// <param name="defaultValue">Value to use when the argument is missing.</param>
    /// <returns>True when the argument was present and typed correctly.</returns>
    public static bool TryGetActionArgument<T>(
        this ActionExecutingContext ctx,
        string key,
        out T value,
        T defaultValue = default!)
    {
        if (ctx.ActionArguments.TryGetValue(key, out var objValue) && objValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = defaultValue;
        return false;
    }

    /// <summary>
    /// Resolves the requesting user id from claims or query string.
    /// </summary>
    /// <param name="ctx">The action executing context.</param>
    /// <param name="userId">The resolved user id.</param>
    /// <returns>True when a non-empty user id was found.</returns>
    public static bool TryGetUserId(this ActionExecutingContext ctx, out Guid userId)
    {
        userId = Guid.Empty;

        var userIdStr =
            ctx.HttpContext.User.Claims.FirstOrDefault(c => c.Type is "UserId" or "Jellyfin-UserId")?.Value
            ?? ctx.HttpContext.Request.Query["userId"].FirstOrDefault();

        if (!Guid.TryParse(userIdStr, out userId))
        {
            return false;
        }

        return userId != Guid.Empty;
    }
}
