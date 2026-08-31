using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// A normalized TMDB search hit used by the search filter.
/// </summary>
/// <param name="TmdbId">TMDB numeric id.</param>
/// <param name="Kind">Movie or series.</param>
/// <param name="Title">Display title.</param>
/// <param name="Year">Release or first-air year when known.</param>
/// <param name="PosterPath">TMDB poster path segment.</param>
/// <param name="Overview">Plot overview when available.</param>
/// <param name="Popularity">TMDB popularity score for ranking.</param>
public sealed record TmdbSearchHit(
    int TmdbId,
    BaseItemKind Kind,
    string Title,
    int? Year,
    string? PosterPath,
    string? Overview,
    double Popularity);

/// <summary>
/// TMDB paginated search response envelope.
/// </summary>
internal sealed class TmdbSearchResponse
{
    /// <summary>Gets or sets result rows.</summary>
    [JsonPropertyName("results")]
    public List<TmdbSearchResultRow> Results { get; set; } = [];
}

/// <summary>
/// A single TMDB search result row (movie or TV).
/// </summary>
internal sealed class TmdbSearchResultRow
{
    /// <summary>Gets or sets the TMDB id.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the movie title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the series name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the movie release date.</summary>
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    /// <summary>Gets or sets the series first air date.</summary>
    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    /// <summary>Gets or sets the poster path.</summary>
    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    /// <summary>Gets or sets the overview text.</summary>
    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    /// <summary>Gets or sets the popularity score.</summary>
    [JsonPropertyName("popularity")]
    public double Popularity { get; set; }
}
