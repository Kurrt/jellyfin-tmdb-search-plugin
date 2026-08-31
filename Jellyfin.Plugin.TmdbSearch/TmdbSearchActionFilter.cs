using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TmdbSearch.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Intercepts Jellyfin Items search and serves TMDB results instead of SQL/Stremio search.
/// </summary>
public sealed class TmdbSearchActionFilter : IAsyncActionFilter, IOrderedFilter
{
    private const string TmdbImageBase = "https://image.tmdb.org/t/p/w500";

    private readonly TmdbClient _tmdbClient;
    private readonly TmdbLibraryIndex _libraryIndex;
    private readonly GelatoMetaBridge _gelatoBridge;
    private readonly IDtoService _dtoService;
    private readonly ILibraryManager _libraryManager;
    private readonly IServerConfigurationManager _serverConfigurationManager;
    private readonly ILogger<TmdbSearchActionFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbSearchActionFilter"/> class.
    /// </summary>
    public TmdbSearchActionFilter(
        TmdbClient tmdbClient,
        TmdbLibraryIndex libraryIndex,
        GelatoMetaBridge gelatoBridge,
        IDtoService dtoService,
        ILibraryManager libraryManager,
        IServerConfigurationManager serverConfigurationManager,
        ILogger<TmdbSearchActionFilter> logger)
    {
        _tmdbClient = tmdbClient;
        _libraryIndex = libraryIndex;
        _gelatoBridge = gelatoBridge;
        _dtoService = dtoService;
        _libraryManager = libraryManager;
        _serverConfigurationManager = serverConfigurationManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        if (!ctx.IsApiSearchAction())
        {
            await next().ConfigureAwait(false);
            return;
        }

        if (!ctx.TryGetActionArgument("searchTerm", out string? searchTerm)
            || string.IsNullOrWhiteSpace(searchTerm))
        {
            await next().ConfigureAwait(false);
            return;
        }

        if (searchTerm.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
        {
            ctx.ActionArguments["searchTerm"] = searchTerm[6..].Trim();
            await next().ConfigureAwait(false);
            return;
        }

        var requestedTypes = GetRequestedItemTypes(ctx);
        if (requestedTypes.Count == 0)
        {
            _logger.LogDebug(
                "TMDB search passthrough for \"{Query}\": no movie/series types requested",
                searchTerm);
            await next().ConfigureAwait(false);
            return;
        }

        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            _logger.LogWarning("TMDB search passthrough for \"{Query}\": plugin instance unavailable", searchTerm);
            await next().ConfigureAwait(false);
            return;
        }

        var config = plugin.Configuration;
        if (string.IsNullOrWhiteSpace(config.TmdbApiKey))
        {
            _logger.LogWarning("TMDB search passthrough for \"{Query}\": no API key configured", searchTerm);
            await next().ConfigureAwait(false);
            return;
        }

        ctx.TryGetActionArgument("startIndex", out var startIndex, 0);
        ctx.TryGetActionArgument("limit", out var limit, 25);
        if (limit <= 0)
        {
            limit = 25;
        }

        var language = ResolveLanguage(config);
        var hits = await _tmdbClient
            .SearchAsync(searchTerm, requestedTypes, config, language, ctx.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        if (hits is null)
        {
            _logger.LogWarning(
                "TMDB search passthrough for \"{Query}\": TMDB request failed or timed out",
                searchTerm);
            await next().ConfigureAwait(false);
            return;
        }

        var pagedHits = hits.Skip(startIndex).Take(limit).ToArray();
        var dtos = BuildResultDtos(pagedHits);

        _logger.LogInformation(
            "TMDB search \"{Query}\" types=[{Types}] start={Start} limit={Limit} page={Page} total={Total}",
            searchTerm,
            string.Join(',', requestedTypes),
            startIndex,
            limit,
            dtos.Count,
            hits.Count);

        ctx.Result = new OkObjectResult(new QueryResult<BaseItemDto>
        {
            Items = dtos,
            TotalRecordCount = hits.Count,
        });
    }

    private List<BaseItemDto> BuildResultDtos(IReadOnlyList<TmdbSearchHit> hits)
    {
        var options = new DtoOptions
        {
            EnableImages = true,
            EnableUserData = false,
        };

        var dtos = new List<BaseItemDto>(hits.Count);

        foreach (var hit in hits)
        {
            if (_libraryIndex.TryGetItemId(hit.Kind, hit.TmdbId, out var ownedId))
            {
                var ownedItem = _libraryManager.GetItemById(ownedId);
                if (ownedItem is not null)
                {
                    dtos.Add(_dtoService.GetBaseItemDto(ownedItem, options));
                    continue;
                }
            }

            var externalId = StremioGuidHelper.BuildExternalId(imdbId: null, hit.TmdbId);
            var stremioKind = StremioGuidHelper.ToStremioKind(hit.Kind);
            if (stremioKind is null)
            {
                continue;
            }

            var stubItem = CreateStubItem(hit);
            var dto = _dtoService.GetBaseItemDto(stubItem, options);
            dto.Id = StremioGuidHelper.ToGuid(stremioKind.Value, externalId);
            dtos.Add(dto);

            _gelatoBridge.SaveSearchMeta(dto.Id, stremioKind.Value, externalId, imdbId: null);
        }

        return dtos;
    }

    private static BaseItem CreateStubItem(TmdbSearchHit hit)
    {
        BaseItem item = hit.Kind == BaseItemKind.Movie
            ? new Movie()
            : new Series();

        item.Name = hit.Title;
        item.Overview = hit.Overview;
        if (hit.Year.HasValue)
        {
            item.ProductionYear = hit.Year.Value;
            item.PremiereDate = new DateTime(hit.Year.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        item.SetProviderId(MetadataProvider.Tmdb, hit.TmdbId.ToString());

        if (!string.IsNullOrWhiteSpace(hit.PosterPath))
        {
            item.ImageInfos =
            [
                new ItemImageInfo
                {
                    Type = ImageType.Primary,
                    Path = $"{TmdbImageBase}{hit.PosterPath}",
                },
            ];
        }

        return item;
    }

    private string ResolveLanguage(PluginConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.Language))
        {
            return config.Language;
        }

        var lang = _serverConfigurationManager.Configuration.PreferredMetadataLanguage;
        return string.IsNullOrWhiteSpace(lang) ? "en-US" : lang;
    }

    private static HashSet<BaseItemKind> GetRequestedItemTypes(ActionExecutingContext ctx)
    {
        var requested = new HashSet<BaseItemKind>([BaseItemKind.Movie, BaseItemKind.Series]);

        if (ctx.TryGetActionArgument<BaseItemKind[]>("includeItemTypes", out var includeTypes)
            && includeTypes is { Length: > 0 })
        {
            requested = new HashSet<BaseItemKind>(includeTypes);
            requested.IntersectWith([BaseItemKind.Movie, BaseItemKind.Series]);
        }

        if (ctx.TryGetActionArgument<BaseItemKind[]>("excludeItemTypes", out var excludeTypes)
            && excludeTypes is { Length: > 0 })
        {
            requested.ExceptWith(excludeTypes);
        }

        if (ctx.TryGetActionArgument<MediaType[]>("mediaTypes", out var mediaTypes)
            && mediaTypes.Contains(MediaType.Video))
        {
            requested.Remove(BaseItemKind.Series);
        }

        return requested;
    }
}
