using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Kind of series-children request jellyfin-web makes after opening a TMDB series stub.
/// </summary>
public enum TmdbShowChildrenKind
{
    /// <summary>GET /Shows/{id}/Seasons.</summary>
    Seasons,

    /// <summary>GET /Shows/{id}/Episodes.</summary>
    Episodes,

    /// <summary>GET /Shows/NextUp?SeriesId=.</summary>
    NextUp,

    /// <summary>GET /Items?ParentId= for a stub series or season.</summary>
    ParentItems,
}

/// <summary>
/// A parsed Shows/Items children request.
/// </summary>
/// <param name="Kind">The children endpoint kind.</param>
/// <param name="SeriesId">Series id for Seasons/Episodes/NextUp.</param>
/// <param name="SeasonId">Season id when GetEpisodes filters by season.</param>
/// <param name="ParentId">Parent id for GetItems listings.</param>
public readonly record struct TmdbShowChildrenMatch(
    TmdbShowChildrenKind Kind,
    Guid SeriesId,
    Guid? SeasonId,
    Guid? ParentId);

/// <summary>
/// Recognizes jellyfin-web season, episode, Next Up, and ParentId listing routes.
/// </summary>
public static class TmdbShowChildrenRoute
{
    /// <summary>
    /// Tries to classify a Shows/Items children request.
    /// </summary>
    /// <param name="path">Request path.</param>
    /// <param name="actionName">MVC action name when known.</param>
    /// <param name="seriesId">Bound series id when present.</param>
    /// <param name="seasonId">Bound season id when present.</param>
    /// <param name="parentId">Bound ParentId when present.</param>
    /// <param name="match">The classified request when successful.</param>
    /// <returns>True when this is a children route this plugin should consider.</returns>
    public static bool TryMatch(
        string? path,
        string? actionName,
        Guid? seriesId,
        Guid? seasonId,
        Guid? parentId,
        out TmdbShowChildrenMatch match)
    {
        match = default;

        if (IsNextUp(path, actionName))
        {
            if (!HasId(seriesId, out var nextUpSeriesId))
            {
                return false;
            }

            match = new TmdbShowChildrenMatch(TmdbShowChildrenKind.NextUp, nextUpSeriesId, null, null);
            return true;
        }

        if (IsSeasons(path, actionName))
        {
            if (!TryResolveSeriesId(seriesId, path, out var seasonsSeriesId))
            {
                return false;
            }

            match = new TmdbShowChildrenMatch(TmdbShowChildrenKind.Seasons, seasonsSeriesId, null, null);
            return true;
        }

        if (IsEpisodes(path, actionName))
        {
            if (!TryResolveSeriesId(seriesId, path, out var episodesSeriesId))
            {
                return false;
            }

            match = new TmdbShowChildrenMatch(
                TmdbShowChildrenKind.Episodes,
                episodesSeriesId,
                HasId(seasonId, out var resolvedSeasonId) ? resolvedSeasonId : null,
                null);
            return true;
        }

        if (IsParentItems(actionName) && HasId(parentId, out var resolvedParentId))
        {
            match = new TmdbShowChildrenMatch(
                TmdbShowChildrenKind.ParentItems,
                Guid.Empty,
                null,
                resolvedParentId);
            return true;
        }

        return false;
    }

    private static bool IsSeasons(string? path, string? actionName) =>
        ActionEquals(actionName, "GetSeasons") || PathEndsWithSegment(path, "/Seasons");

    private static bool IsEpisodes(string? path, string? actionName) =>
        ActionEquals(actionName, "GetEpisodes") || PathEndsWithSegment(path, "/Episodes");

    private static bool IsNextUp(string? path, string? actionName) =>
        ActionEquals(actionName, "GetNextUp")
        || (path is not null && path.EndsWith("/Shows/NextUp", StringComparison.OrdinalIgnoreCase));

    private static bool IsParentItems(string? actionName) =>
        ActionEquals(actionName, "GetItems") || ActionEquals(actionName, "GetItemsByUserIdLegacy");

    private static bool TryResolveSeriesId(Guid? seriesId, string? path, out Guid id)
    {
        if (HasId(seriesId, out id))
        {
            return true;
        }

        return TmdbItemActionFilter.TryGetItemIdFromPath(path, out id)
            || TryGetShowsItemIdFromPath(path, out id);
    }

    /// <summary>
    /// Parses a GUID after /Shows/ for season and episode routes.
    /// </summary>
    /// <param name="path">Request path such as /Shows/{guid}/Seasons.</param>
    /// <param name="itemId">The parsed series id when successful.</param>
    /// <returns>True when the path contains a GUID after /Shows/.</returns>
    public static bool TryGetShowsItemIdFromPath(string? path, out Guid itemId)
    {
        itemId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        const string marker = "/Shows/";
        var start = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return false;
        }

        var guidStart = start + marker.Length;
        var guidEnd = path.IndexOf('/', guidStart);
        var raw = guidEnd < 0 ? path[guidStart..] : path[guidStart..guidEnd];
        return Guid.TryParse(raw, out itemId) && itemId != Guid.Empty;
    }

    private static bool HasId(Guid? value, out Guid id)
    {
        id = value ?? Guid.Empty;
        return id != Guid.Empty;
    }

    private static bool PathEndsWithSegment(string? path, string suffix) =>
        path is not null && path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);

    private static bool ActionEquals(string? actionName, string expected) =>
        actionName is not null && string.Equals(actionName, expected, StringComparison.OrdinalIgnoreCase);
}
