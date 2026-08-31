using Jellyfin.Data.Enums;
using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for mapping TMDB search JSON rows onto Remux-style stubs.
/// </summary>
public sealed class TmdbSearchMapperTests
{
    /// <summary>
    /// Verifies a movie row maps title, year, poster path, overview, and popularity.
    /// </summary>
    [Fact]
    public void TryMapMovie_ParsesYearAndFields()
    {
        var hit = TmdbSearchMapper.TryMapMovie(
            id: 550,
            title: "Fight Club",
            releaseDate: "1999-10-15",
            posterPath: "/pB8BM7pdSp9Mwx8oTfS2aK9Ffa.jpg",
            overview: "An insomniac office worker...",
            popularity: 91.2);

        Assert.NotNull(hit);
        Assert.Equal(550, hit.TmdbId);
        Assert.Equal(BaseItemKind.Movie, hit.Kind);
        Assert.Equal("Fight Club", hit.Title);
        Assert.Equal(1999, hit.Year);
        Assert.Equal("/pB8BM7pdSp9Mwx8oTfS2aK9Ffa.jpg", hit.PosterPath);
        Assert.Equal("An insomniac office worker...", hit.Overview);
        Assert.Equal(91.2, hit.Popularity);
    }

    /// <summary>
    /// Verifies a TV row uses name and first-air date.
    /// </summary>
    [Fact]
    public void TryMapSeries_UsesNameAndFirstAirDate()
    {
        var hit = TmdbSearchMapper.TryMapSeries(
            id: 1399,
            name: "Game of Thrones",
            firstAirDate: "2011-04-17",
            posterPath: "/1XS1xqBVoiofOZT1kUFUmicCGBb.jpg",
            overview: "Seven noble families...",
            popularity: 400.5);

        Assert.NotNull(hit);
        Assert.Equal(1399, hit.TmdbId);
        Assert.Equal(BaseItemKind.Series, hit.Kind);
        Assert.Equal("Game of Thrones", hit.Title);
        Assert.Equal(2011, hit.Year);
    }

    /// <summary>
    /// Verifies untitled rows are skipped rather than becoming empty stubs.
    /// </summary>
    [Fact]
    public void TryMapMovie_SkipsUntitledRows()
    {
        Assert.Null(TmdbSearchMapper.TryMapMovie(1, title: "  ", releaseDate: "2020-01-01", posterPath: null, overview: null, popularity: 1));
        Assert.Null(TmdbSearchMapper.TryMapMovie(1, title: null, releaseDate: null, posterPath: null, overview: null, popularity: 1));
    }

    /// <summary>
    /// Verifies short or missing dates do not invent a year.
    /// </summary>
    [Fact]
    public void TryMapMovie_MissingDateYieldsNullYear()
    {
        var missing = TmdbSearchMapper.TryMapMovie(2, "Untitled", releaseDate: null, posterPath: null, overview: null, popularity: 0);
        var shortDate = TmdbSearchMapper.TryMapMovie(3, "Untitled", releaseDate: "19", posterPath: null, overview: null, popularity: 0);

        Assert.NotNull(missing);
        Assert.Null(missing.Year);
        Assert.NotNull(shortDate);
        Assert.Null(shortDate.Year);
    }

    /// <summary>
    /// Verifies poster URLs use Remux's w780 TMDB image size.
    /// </summary>
    [Fact]
    public void ToPosterUrl_UsesW780ImageSize()
    {
        Assert.Equal(
            "https://image.tmdb.org/t/p/w780/poster.jpg",
            TmdbSearchMapper.ToPosterUrl("/poster.jpg"));
        Assert.Null(TmdbSearchMapper.ToPosterUrl(null));
        Assert.Null(TmdbSearchMapper.ToPosterUrl(" "));
    }
}
