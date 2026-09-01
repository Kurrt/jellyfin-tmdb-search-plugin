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
/// A person credit copied from TMDB details onto a Jellyfin DTO.
/// </summary>
/// <param name="Name">Person display name.</param>
/// <param name="Role">Character name or crew job when known.</param>
/// <param name="Type">Jellyfin person kind.</param>
public sealed record TmdbPersonCredit(string Name, string? Role, PersonKind Type);

/// <summary>
/// TMDB movie or series details used to paint item metadata without Gelato streams.
/// </summary>
/// <param name="TmdbId">TMDB numeric id.</param>
/// <param name="Kind">Movie or series.</param>
/// <param name="Title">Display title.</param>
/// <param name="Overview">Plot overview when available.</param>
/// <param name="Year">Release or first-air year when known.</param>
/// <param name="PremiereDate">Release or first-air date when known.</param>
/// <param name="PosterPath">TMDB poster path segment.</param>
/// <param name="RuntimeMinutes">Runtime in minutes when known.</param>
/// <param name="VoteAverage">TMDB vote average on a 0-10 scale.</param>
/// <param name="Tagline">Marketing tagline when known.</param>
/// <param name="Genres">Genre names.</param>
/// <param name="People">Cast and selected crew.</param>
/// <param name="Studios">Production company names.</param>
public sealed record TmdbTitleDetails(
    int TmdbId,
    BaseItemKind Kind,
    string Title,
    string? Overview,
    int? Year,
    DateTime? PremiereDate,
    string? PosterPath,
    int? RuntimeMinutes,
    float? VoteAverage,
    string? Tagline,
    IReadOnlyList<string> Genres,
    IReadOnlyList<TmdbPersonCredit> People,
    IReadOnlyList<string> Studios);

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

/// <summary>
/// TMDB movie or TV details payload, including appended credits.
/// </summary>
internal sealed class TmdbTitleDetailsResponse
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

    /// <summary>Gets or sets the overview text.</summary>
    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    /// <summary>Gets or sets the movie release date.</summary>
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    /// <summary>Gets or sets the series first air date.</summary>
    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }

    /// <summary>Gets or sets the poster path.</summary>
    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    /// <summary>Gets or sets the tagline.</summary>
    [JsonPropertyName("tagline")]
    public string? Tagline { get; set; }

    /// <summary>Gets or sets movie runtime in minutes.</summary>
    [JsonPropertyName("runtime")]
    public int? Runtime { get; set; }

    /// <summary>Gets or sets typical episode runtimes in minutes.</summary>
    [JsonPropertyName("episode_run_time")]
    public List<int> EpisodeRunTime { get; set; } = [];

    /// <summary>Gets or sets the vote average.</summary>
    [JsonPropertyName("vote_average")]
    public double VoteAverage { get; set; }

    /// <summary>Gets or sets genres.</summary>
    [JsonPropertyName("genres")]
    public List<TmdbNamedRow> Genres { get; set; } = [];

    /// <summary>Gets or sets production companies.</summary>
    [JsonPropertyName("production_companies")]
    public List<TmdbNamedRow> ProductionCompanies { get; set; } = [];

    /// <summary>Gets or sets appended credits.</summary>
    [JsonPropertyName("credits")]
    public TmdbCreditsResponse? Credits { get; set; }
}

/// <summary>
/// TMDB named entity such as a genre or studio.
/// </summary>
internal sealed class TmdbNamedRow
{
    /// <summary>Gets or sets the display name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// TMDB credits envelope.
/// </summary>
internal sealed class TmdbCreditsResponse
{
    /// <summary>Gets or sets cast members.</summary>
    [JsonPropertyName("cast")]
    public List<TmdbCastRow> Cast { get; set; } = [];

    /// <summary>Gets or sets crew members.</summary>
    [JsonPropertyName("crew")]
    public List<TmdbCrewRow> Crew { get; set; } = [];
}

/// <summary>
/// TMDB cast member.
/// </summary>
internal sealed class TmdbCastRow
{
    /// <summary>Gets or sets the person name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the character name.</summary>
    [JsonPropertyName("character")]
    public string? Character { get; set; }

    /// <summary>Gets or sets billing order.</summary>
    [JsonPropertyName("order")]
    public int Order { get; set; }
}

/// <summary>
/// TMDB crew member.
/// </summary>
internal sealed class TmdbCrewRow
{
    /// <summary>Gets or sets the person name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the crew job title.</summary>
    [JsonPropertyName("job")]
    public string? Job { get; set; }
}
