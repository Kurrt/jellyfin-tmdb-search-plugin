using System.Reflection;
using Jellyfin.Plugin.TmdbSearch.Configuration;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for recognizing Jellyfin single-item detail MVC actions.
/// </summary>
public sealed class TmdbItemActionFilterTests
{
    /// <summary>
    /// Verifies the GetItem endpoints used when opening a search result are intercepted.
    /// </summary>
    [Theory]
    [InlineData("GetItem")]
    [InlineData("GetItemLegacy")]
    public void IsItemDetailAction_AcceptsGetItemActions(string actionName)
    {
        Assert.True(TmdbItemActionFilter.IsItemDetailAction(actionName));
    }

    /// <summary>
    /// Verifies search and image actions are left to their own filters.
    /// </summary>
    [Theory]
    [InlineData("GetItems")]
    [InlineData("GetItemImage")]
    [InlineData(null)]
    public void IsItemDetailAction_IgnoresOtherActions(string? actionName)
    {
        Assert.False(TmdbItemActionFilter.IsItemDetailAction(actionName));
    }

    /// <summary>
    /// Verifies the web client's /Users/{userId}/Items/{itemId} path yields the stub GUID.
    /// </summary>
    [Fact]
    public void TryGetItemIdFromPath_ParsesLegacyUserItemRoute()
    {
        var path = "/Users/6af0429e93bd49dfa56162910227d22d/Items/3ed52899c7ffa850617dda69c07207bf";

        Assert.True(TmdbItemActionFilter.TryGetItemIdFromPath(path, out var itemId));
        Assert.Equal(Guid.Parse("3ed52899-c7ff-a850-617d-da69c07207bf"), itemId);
    }

    /// <summary>
    /// Verifies the current /Items/{itemId} route is also recognized.
    /// </summary>
    [Fact]
    public void TryGetItemIdFromPath_ParsesCurrentItemRoute()
    {
        var path = "/Items/3ed52899-c7ff-a850-617d-da69c07207bf";

        Assert.True(TmdbItemActionFilter.TryGetItemIdFromPath(path, out var itemId));
        Assert.Equal(Guid.Parse("3ed52899-c7ff-a850-617d-da69c07207bf"), itemId);
    }

    /// <summary>
    /// Verifies Jellyfin's NotFound() and Problem Details 404 results are treated as missing items.
    /// </summary>
    [Fact]
    public void IsNotFoundResult_AcceptsNotFoundAndProblemDetails()
    {
        Assert.True(TmdbItemActionFilter.IsNotFoundResult(new NotFoundResult()));
        Assert.True(TmdbItemActionFilter.IsNotFoundResult(new NotFoundObjectResult(new { title = "Not Found" })));
        Assert.True(TmdbItemActionFilter.IsNotFoundResult(new ObjectResult(new { title = "Not Found" }) { StatusCode = 404 }));
        Assert.False(TmdbItemActionFilter.IsNotFoundResult(new OkObjectResult(new { })));
        Assert.False(TmdbItemActionFilter.IsNotFoundResult(null));
    }

    /// <summary>
    /// Cached search stubs must not skip GetItem. Gelato insert already ran at
    /// filter order 1; short-circuiting here returns Path=/stub and blocks SyncStreams.
    /// </summary>
    [Fact]
    public async Task OnActionExecutionAsync_DoesNotShortCircuitGetItemWhenStubIsCached()
    {
        var itemId = Guid.Parse("3ed52899-c7ff-a850-617d-da69c07207bf");
        var filter = CreateFilterWithCachedStub(itemId, "Fight Club");
        var (executing, actionContext) = CreateGetItemExecutingContext(itemId);
        var nextCalled = false;
        ActionExecutionDelegate next = () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(
                actionContext,
                new List<IFilterMetadata>(),
                controller: new object()));
        };

        await filter.OnActionExecutionAsync(executing, next);

        Assert.True(nextCalled);
        Assert.Null(executing.Result);
    }

    /// <summary>
    /// When GetItem still 404s after Gelato insert, the cached stub keeps the
    /// details page from going blank.
    /// </summary>
    [Fact]
    public async Task OnResultExecutionAsync_ServesCachedStubOnGetItem404()
    {
        var itemId = Guid.Parse("3ed52899-c7ff-a850-617d-da69c07207bf");
        var filter = CreateFilterWithCachedStub(itemId, "Fight Club");
        var executing = CreateGetItemExecutingContext(itemId).Executing;
        var resultContext = new ResultExecutingContext(
            executing,
            new List<IFilterMetadata>(),
            new NotFoundResult(),
            controller: new object());
        var nextCalled = false;
        ResultExecutionDelegate next = () =>
        {
            nextCalled = true;
            return Task.FromResult(new ResultExecutedContext(
                executing,
                new List<IFilterMetadata>(),
                resultContext.Result,
                controller: new object()));
        };

        await filter.OnResultExecutionAsync(resultContext, next);

        Assert.True(nextCalled);
        var ok = Assert.IsType<OkObjectResult>(resultContext.Result);
        var dto = Assert.IsType<BaseItemDto>(ok.Value);
        Assert.Equal("Fight Club", dto.Name);
    }

    /// <summary>
    /// When the GetItem stub fallback is disabled, a 404 stays a 404.
    /// </summary>
    [Fact]
    public async Task OnResultExecutionAsync_SkipsStubWhenFallbackDisabled()
    {
        var itemId = Guid.Parse("3ed52899-c7ff-a850-617d-da69c07207bf");
        var filter = CreateFilterWithCachedStub(itemId, "Fight Club");
        var executing = CreateGetItemExecutingContext(itemId).Executing;
        var resultContext = new ResultExecutingContext(
            executing,
            new List<IFilterMetadata>(),
            new NotFoundResult(),
            controller: new object());
        var config = new PluginConfiguration
        {
            EnableGetItemStubFallback = false,
        };

        using (PluginSettings.OverrideCurrent(config))
        {
            await filter.OnResultExecutionAsync(
                resultContext,
                () => Task.FromResult(new ResultExecutedContext(
                    executing,
                    new List<IFilterMetadata>(),
                    resultContext.Result,
                    controller: new object())));
        }

        Assert.IsType<NotFoundResult>(resultContext.Result);
    }

    /// <summary>
    /// Builds a filter whose stub cache already holds a search DTO for <paramref name="itemId"/>.
    /// </summary>
    private static TmdbItemActionFilter CreateFilterWithCachedStub(Guid itemId, string name)
    {
        var cache = new TmdbPosterCache();
        cache.Set(
            itemId,
            new BaseItemDto
            {
                Id = itemId,
                Name = name,
            },
            posterUrl: null);

        return new TmdbItemActionFilter(cache, NullLogger<TmdbItemActionFilter>.Instance);
    }

    /// <summary>
    /// Builds an executing context for GET /Items/{itemId} (GetItem).
    /// </summary>
    private static (ActionExecutingContext Executing, ActionContext ActionContext) CreateGetItemExecutingContext(
        Guid itemId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = $"/Items/{itemId:D}";
        httpContext.Request.Method = HttpMethods.Get;

        var routeData = new RouteData();
        routeData.Values["itemId"] = itemId.ToString("D");

        var descriptor = new ControllerActionDescriptor
        {
            ActionName = "GetItem",
            ControllerName = "UserLibrary",
            DisplayName = "GetItem",
            ControllerTypeInfo = typeof(object).GetTypeInfo(),
            MethodInfo = typeof(object).GetMethod(nameof(ToString), Type.EmptyTypes)
                ?? typeof(object).GetMethods(BindingFlags.Public | BindingFlags.Instance)[0],
        };

        var actionContext = new ActionContext(httpContext, routeData, descriptor);
        var executing = new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?> { ["itemId"] = itemId },
            controller: new object());

        return (executing, actionContext);
    }
}
