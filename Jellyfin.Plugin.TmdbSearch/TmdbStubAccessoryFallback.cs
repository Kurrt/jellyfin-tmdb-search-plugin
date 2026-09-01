using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Builds empty 200 responses for accessory item endpoints when the id is a TMDB search stub.
/// Playback, images, and GetItem are left to their dedicated handlers.
/// </summary>
public static class TmdbStubAccessoryFallback
{
    /// <summary>
    /// Tries to create an empty success result for a known accessory item route.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <param name="actionName">The MVC action name when known.</param>
    /// <param name="ownerId">The stub item id, used as theme-media owner.</param>
    /// <param name="result">The empty 200 result when this route is handled.</param>
    /// <returns>True when the route should return an empty success payload instead of 404.</returns>
    public static bool TryCreate(string? path, string? actionName, Guid ownerId, out IActionResult result)
    {
        result = new EmptyResult();
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (IsThemeMedia(path, actionName))
        {
            result = new OkObjectResult(CreateEmptyThemeMedia(ownerId));
            return true;
        }

        if (IsThemeSongs(path, actionName) || IsThemeVideos(path, actionName))
        {
            result = new OkObjectResult(CreateEmptyThemeResult(ownerId));
            return true;
        }

        if (IsSimilar(path, actionName))
        {
            result = new OkObjectResult(CreateEmptyQuery());
            return true;
        }

        if (IsAncestors(path, actionName) || IsSpecialFeatures(path, actionName))
        {
            result = new OkObjectResult(Array.Empty<BaseItemDto>());
            return true;
        }

        return false;
    }

    private static bool IsThemeMedia(string path, string? actionName) =>
        PathEndsWith(path, "/ThemeMedia") || ActionEquals(actionName, "GetThemeMedia");

    private static bool IsThemeSongs(string path, string? actionName) =>
        PathEndsWith(path, "/ThemeSongs") || ActionEquals(actionName, "GetThemeSongs");

    private static bool IsThemeVideos(string path, string? actionName) =>
        PathEndsWith(path, "/ThemeVideos") || ActionEquals(actionName, "GetThemeVideos");

    private static bool IsSimilar(string path, string? actionName) =>
        PathEndsWith(path, "/Similar") || ActionEquals(actionName, "GetSimilarItems");

    private static bool IsAncestors(string path, string? actionName) =>
        PathEndsWith(path, "/Ancestors") || ActionEquals(actionName, "GetAncestors");

    private static bool IsSpecialFeatures(string path, string? actionName) =>
        PathEndsWith(path, "/SpecialFeatures") || ActionEquals(actionName, "GetSpecialFeatures");

    private static bool PathEndsWith(string path, string suffix) =>
        path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    private static bool ActionEquals(string? actionName, string expected) =>
        actionName is not null && string.Equals(actionName, expected, StringComparison.OrdinalIgnoreCase);

    private static AllThemeMediaResult CreateEmptyThemeMedia(Guid ownerId) =>
        new()
        {
            ThemeSongsResult = CreateEmptyThemeResult(ownerId),
            ThemeVideosResult = CreateEmptyThemeResult(ownerId),
            SoundtrackSongsResult = CreateEmptyThemeResult(ownerId),
        };

    private static ThemeMediaResult CreateEmptyThemeResult(Guid ownerId) =>
        new()
        {
            OwnerId = ownerId,
            Items = [],
            TotalRecordCount = 0,
        };

    private static QueryResult<BaseItemDto> CreateEmptyQuery() =>
        new()
        {
            Items = [],
            TotalRecordCount = 0,
        };
}
