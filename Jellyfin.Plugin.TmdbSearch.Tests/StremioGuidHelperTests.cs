using Jellyfin.Data.Enums;
using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for Gelato-compatible Stremio GUID generation.
/// </summary>
public class StremioGuidHelperTests
{
    /// <summary>
    /// Verifies TMDB external ids use the tmdb: prefix.
    /// </summary>
    [Fact]
    public void BuildExternalId_UsesTmdbPrefixWhenNoImdb()
    {
        var externalId = StremioGuidHelper.BuildExternalId(null, 550);
        Assert.Equal("tmdb:550", externalId);
    }

    /// <summary>
    /// Verifies IMDb ids are preferred and normalized with tt prefix.
    /// </summary>
    [Fact]
    public void BuildExternalId_PrefersImdbId()
    {
        var externalId = StremioGuidHelper.BuildExternalId("0111161", 550);
        Assert.Equal("tt0111161", externalId);
    }

    /// <summary>
    /// Verifies GUID generation is stable for the same Stremio URI.
    /// </summary>
    [Fact]
    public void ToGuid_IsDeterministicForMovie()
    {
        var first = StremioGuidHelper.ToGuid(StremioMediaKind.Movie, "tt0133093");
        var second = StremioGuidHelper.ToGuid(StremioMediaKind.Movie, "tt0133093");

        Assert.Equal(first, second);
        Assert.NotEqual(Guid.Empty, first);
    }

    /// <summary>
    /// Verifies movie and series URIs produce different GUIDs.
    /// </summary>
    [Fact]
    public void ToGuid_DiffersBetweenMovieAndSeries()
    {
        var movie = StremioGuidHelper.ToGuid(StremioMediaKind.Movie, "tmdb:1399");
        var series = StremioGuidHelper.ToGuid(StremioMediaKind.Series, "tmdb:1399");

        Assert.NotEqual(movie, series);
    }

    /// <summary>
    /// Verifies only movie and series kinds map to Stremio media kinds.
    /// </summary>
    [Fact]
    public void ToStremioKind_SupportsMovieAndSeriesOnly()
    {
        Assert.Equal(StremioMediaKind.Movie, StremioGuidHelper.ToStremioKind(BaseItemKind.Movie));
        Assert.Equal(StremioMediaKind.Series, StremioGuidHelper.ToStremioKind(BaseItemKind.Series));
        Assert.Null(StremioGuidHelper.ToStremioKind(BaseItemKind.Audio));
    }
}
