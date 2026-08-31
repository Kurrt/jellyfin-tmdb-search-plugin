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
}
