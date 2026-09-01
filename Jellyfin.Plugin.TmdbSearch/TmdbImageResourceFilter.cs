using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Proxies TMDB posters for unowned search stubs so artwork does not 404 after Gelato cache wipes.
/// </summary>
public sealed class TmdbImageResourceFilter : IAsyncResourceFilter, IOrderedFilter
{
    /// <summary>
    /// Named HttpClient used to fetch TMDB CDN images.
    /// </summary>
    public const string HttpClientName = "TmdbImages";

    private static readonly HashSet<string> ItemImageActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "GetItemImage",
        "GetItemImageByIndex",
        "GetItemImage2",
    };

    private readonly TmdbPosterCache _posterCache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TmdbImageResourceFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbImageResourceFilter"/> class.
    /// </summary>
    /// <param name="posterCache">Search-stub poster URL cache.</param>
    /// <param name="httpClientFactory">Factory for the TMDB image HttpClient.</param>
    /// <param name="logger">Logger instance.</param>
    public TmdbImageResourceFilter(
        TmdbPosterCache posterCache,
        IHttpClientFactory httpClientFactory,
        ILogger<TmdbImageResourceFilter> logger)
    {
        _posterCache = posterCache;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public int Order => -1;

    /// <summary>
    /// Returns true when the MVC action is a Jellyfin item image endpoint.
    /// </summary>
    /// <param name="actionName">The current action name.</param>
    /// <returns>True for GetItemImage variants.</returns>
    public static bool IsItemImageAction(string? actionName) =>
        actionName is not null && ItemImageActions.Contains(actionName);

    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(
        ResourceExecutingContext ctx,
        ResourceExecutionDelegate next)
    {
        if (ctx.ActionDescriptor is not ControllerActionDescriptor descriptor
            || !IsItemImageAction(descriptor.ActionName))
        {
            await next().ConfigureAwait(false);
            return;
        }

        if (!TryGetItemId(ctx, out var itemId) || !_posterCache.TryGet(itemId, out var posterUrl))
        {
            await next().ConfigureAwait(false);
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client
                .GetAsync(posterUrl, HttpCompletionOption.ResponseHeadersRead, ctx.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "TMDB poster proxy returned {Status} for item {ItemId}",
                    (int)response.StatusCode,
                    itemId);
                await next().ConfigureAwait(false);
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(ctx.HttpContext.RequestAborted)
                .ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            ctx.Result = new FileContentResult(bytes, contentType);
        }
        catch (OperationCanceledException) when (ctx.HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TMDB poster proxy failed for item {ItemId}", itemId);
            await next().ConfigureAwait(false);
        }
    }

    private static bool TryGetItemId(ResourceExecutingContext ctx, out Guid itemId)
    {
        itemId = Guid.Empty;
        if (ctx.RouteData.Values.TryGetValue("itemId", out var raw)
            && raw is not null
            && Guid.TryParse(raw.ToString(), out itemId)
            && itemId != Guid.Empty)
        {
            return true;
        }

        return TmdbItemActionFilter.TryGetItemIdFromPath(ctx.HttpContext.Request.Path.Value, out itemId);
    }
}
