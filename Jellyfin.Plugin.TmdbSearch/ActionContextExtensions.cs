using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TmdbSearch.Configuration;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Extension methods for ASP.NET action filter context used by search interception.
/// </summary>
public static class ActionContextExtensions
{
    private static readonly HashSet<string> SearchActionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "GetItems",
        "GetItemsByUserIdLegacy",
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
    /// <returns>True for search actions.</returns>
    public static bool IsApiSearchAction(this ActionExecutingContext ctx) =>
        ctx.GetActionName() is { } actionName && SearchActionNames.Contains(actionName);

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
