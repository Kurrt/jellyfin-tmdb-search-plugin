using System.Security.Cryptography;
using System.Text;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Media kind used when building Gelato-compatible Stremio URIs.
/// </summary>
public enum StremioMediaKind
{
    /// <summary>Movie content.</summary>
    Movie,

    /// <summary>Series content.</summary>
    Series,
}

/// <summary>
/// Builds Gelato-compatible Stremio URIs and deterministic GUIDs for search stubs.
/// </summary>
public static class StremioGuidHelper
{
    /// <summary>
    /// Converts a Jellyfin item kind to a Stremio media kind when supported.
    /// </summary>
    /// <param name="kind">The Jellyfin base item kind.</param>
    /// <returns>The Stremio media kind, or null when unsupported.</returns>
    public static StremioMediaKind? ToStremioKind(BaseItemKind kind) =>
        kind switch
        {
            BaseItemKind.Movie => StremioMediaKind.Movie,
            BaseItemKind.Series => StremioMediaKind.Series,
            _ => null,
        };

    /// <summary>
    /// Builds the external id string used inside a Stremio URI.
    /// Prefers IMDb ids; falls back to TMDB prefixed ids.
    /// </summary>
    /// <param name="imdbId">Optional IMDb id (with or without tt prefix).</param>
    /// <param name="tmdbId">TMDB numeric id.</param>
    /// <returns>The external id segment.</returns>
    public static string BuildExternalId(string? imdbId, int tmdbId)
    {
        if (!string.IsNullOrWhiteSpace(imdbId))
        {
            return imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
                ? imdbId
                : $"tt{imdbId}";
        }

        return $"tmdb:{tmdbId}";
    }

    /// <summary>
    /// Computes the deterministic Gelato search GUID from a Stremio URI.
    /// </summary>
    /// <param name="kind">Movie or series.</param>
    /// <param name="externalId">The Stremio external id.</param>
    /// <returns>A stable GUID compatible with Gelato.</returns>
    public static Guid ToGuid(StremioMediaKind kind, string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            throw new ArgumentException("External id cannot be empty.", nameof(externalId));
        }

        var typeLabel = kind == StremioMediaKind.Movie ? "movie" : "series";
        var uri = $"stremio://{typeLabel}/{externalId}";
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(uri));
        return new Guid(hash);
    }

    /// <summary>
    /// Builds the Stremio external id for a season: tmdb:{id}:{season}.
    /// </summary>
    /// <param name="tmdbId">TMDB series id.</param>
    /// <param name="seasonNumber">Season number.</param>
    /// <returns>The external id segment.</returns>
    public static string BuildSeasonExternalId(int tmdbId, int seasonNumber) =>
        $"{BuildExternalId(imdbId: null, tmdbId)}:{seasonNumber}";

    /// <summary>
    /// Builds the Stremio external id for an episode: tmdb:{id}:{season}:{episode}.
    /// </summary>
    /// <param name="tmdbId">TMDB series id.</param>
    /// <param name="seasonNumber">Season number.</param>
    /// <param name="episodeNumber">Episode number.</param>
    /// <returns>The external id segment.</returns>
    public static string BuildEpisodeExternalId(int tmdbId, int seasonNumber, int episodeNumber) =>
        $"{BuildExternalId(imdbId: null, tmdbId)}:{seasonNumber}:{episodeNumber}";

    /// <summary>
    /// Computes the deterministic GUID for a TMDB season stub.
    /// </summary>
    /// <param name="tmdbId">TMDB series id.</param>
    /// <param name="seasonNumber">Season number.</param>
    /// <returns>A stable GUID in Gelato's series namespace.</returns>
    public static Guid ForSeason(int tmdbId, int seasonNumber) =>
        ToGuid(StremioMediaKind.Series, BuildSeasonExternalId(tmdbId, seasonNumber));

    /// <summary>
    /// Computes the deterministic GUID for a TMDB episode stub.
    /// </summary>
    /// <param name="tmdbId">TMDB series id.</param>
    /// <param name="seasonNumber">Season number.</param>
    /// <param name="episodeNumber">Episode number.</param>
    /// <returns>A stable GUID compatible with Gelato episode URIs.</returns>
    public static Guid ForEpisode(int tmdbId, int seasonNumber, int episodeNumber) =>
        ToGuid(StremioMediaKind.Series, BuildEpisodeExternalId(tmdbId, seasonNumber, episodeNumber));
}
