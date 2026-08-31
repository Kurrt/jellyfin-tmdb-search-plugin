using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Maps TMDB search JSON rows onto Remux-style in-memory hits.
/// </summary>
public static class TmdbSearchMapper
{
    /// <summary>
    /// TMDB poster size used by Remux search stubs.
    /// </summary>
    public const string PosterImageBase = "https://image.tmdb.org/t/p/w780";

    /// <summary>
    /// Maps a TMDB movie search row when the title is present.
    /// </summary>
    /// <param name="id">TMDB numeric id.</param>
    /// <param name="title">Movie title.</param>
    /// <param name="releaseDate">ISO release date when known.</param>
    /// <param name="posterPath">TMDB poster path segment.</param>
    /// <param name="overview">Plot overview.</param>
    /// <param name="popularity">TMDB popularity score.</param>
    /// <returns>The mapped hit, or null when the title is missing.</returns>
    public static TmdbSearchHit? TryMapMovie(
        int id,
        string? title,
        string? releaseDate,
        string? posterPath,
        string? overview,
        double popularity) =>
        TryMap(id, BaseItemKind.Movie, title, releaseDate, posterPath, overview, popularity);

    /// <summary>
    /// Maps a TMDB TV search row when the name is present.
    /// </summary>
    /// <param name="id">TMDB numeric id.</param>
    /// <param name="name">Series name.</param>
    /// <param name="firstAirDate">ISO first-air date when known.</param>
    /// <param name="posterPath">TMDB poster path segment.</param>
    /// <param name="overview">Plot overview.</param>
    /// <param name="popularity">TMDB popularity score.</param>
    /// <returns>The mapped hit, or null when the name is missing.</returns>
    public static TmdbSearchHit? TryMapSeries(
        int id,
        string? name,
        string? firstAirDate,
        string? posterPath,
        string? overview,
        double popularity) =>
        TryMap(id, BaseItemKind.Series, name, firstAirDate, posterPath, overview, popularity);

    /// <summary>
    /// Builds an absolute TMDB poster URL, or null when no path is present.
    /// </summary>
    /// <param name="posterPath">TMDB poster path segment.</param>
    /// <returns>The w780 image URL when <paramref name="posterPath"/> is set.</returns>
    public static string? ToPosterUrl(string? posterPath)
    {
        if (string.IsNullOrWhiteSpace(posterPath))
        {
            return null;
        }

        return $"{PosterImageBase}{posterPath}";
    }

    /// <summary>
    /// Maps a deserialized TMDB row for the requested kind.
    /// </summary>
    /// <param name="row">Raw TMDB search row.</param>
    /// <param name="kind">Movie or series.</param>
    /// <returns>The mapped hit, or null when unsupported or untitled.</returns>
    internal static TmdbSearchHit? TryMapRow(TmdbSearchResultRow row, BaseItemKind kind) =>
        kind switch
        {
            BaseItemKind.Movie => TryMapMovie(
                row.Id,
                row.Title,
                row.ReleaseDate,
                row.PosterPath,
                row.Overview,
                row.Popularity),
            BaseItemKind.Series => TryMapSeries(
                row.Id,
                row.Name,
                row.FirstAirDate,
                row.PosterPath,
                row.Overview,
                row.Popularity),
            _ => null,
        };

    private static TmdbSearchHit? TryMap(
        int id,
        BaseItemKind kind,
        string? title,
        string? date,
        string? posterPath,
        string? overview,
        double popularity)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return new TmdbSearchHit(
            id,
            kind,
            title.Trim(),
            ParseYear(date),
            posterPath,
            overview,
            popularity);
    }

    private static int? ParseYear(string? date)
    {
        if (string.IsNullOrWhiteSpace(date) || date.Length < 4)
        {
            return null;
        }

        return int.TryParse(date.AsSpan(0, 4), out var year) ? year : null;
    }
}
