using System.Net;
using System.Text;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TmdbSearch.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for Remux-style TMDB search HTTP behavior.
/// </summary>
public sealed class TmdbClientTests
{
    /// <summary>
    /// Verifies HTTP timeouts stay short so hung IPv6 connects cannot stall search.
    /// </summary>
    [Fact]
    public void HttpTimeouts_AreFailFast()
    {
        Assert.Equal(TimeSpan.FromSeconds(4), TmdbClient.HttpTimeout);
        Assert.Equal(TimeSpan.FromSeconds(2), TmdbClient.ConnectTimeout);
        Assert.True(TmdbClient.HttpTimeout < TimeSpan.FromSeconds(10));
        Assert.True(TmdbClient.ConnectTimeout < TmdbClient.HttpTimeout);
    }

    /// <summary>
    /// Verifies an empty TMDB body is a successful empty list, not a timeout/failure.
    /// </summary>
    [Fact]
    public async Task SearchAsync_EmptyBodyIsSuccessfulEmptyList()
    {
        var handler = new ScriptedHandler(_ => Json("""{"results":[]}"""));
        var client = CreateClient(handler);

        var hits = await client.SearchAsync(
            "no-such-title-xyz",
            new HashSet<BaseItemKind> { BaseItemKind.Movie, BaseItemKind.Series },
            Config(),
            "en-US",
            CancellationToken.None);

        Assert.NotNull(hits);
        Assert.Empty(hits);
        Assert.Equal(2, handler.RequestUris.Count);
        Assert.Contains(handler.RequestUris, uri => uri.AbsolutePath.Contains("search/movie", StringComparison.Ordinal));
        Assert.Contains(handler.RequestUris, uri => uri.AbsolutePath.Contains("search/tv", StringComparison.Ordinal));
        Assert.DoesNotContain(handler.RequestUris, uri => uri.AbsolutePath.Contains("external_ids", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies movie and TV hits are merged and ranked by TMDB popularity.
    /// </summary>
    [Fact]
    public async Task SearchAsync_MapsMovieAndSeriesByPopularity()
    {
        var handler = new ScriptedHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("search/movie", StringComparison.Ordinal) == true)
            {
                return Json(
                    """{"results":[{"id":550,"title":"Fight Club","release_date":"1999-10-15","poster_path":"/p.jpg","overview":"Office worker","popularity":91.2}]}""");
            }

            return Json(
                """{"results":[{"id":1399,"name":"Game of Thrones","first_air_date":"2011-04-17","poster_path":"/g.jpg","overview":"Westeros","popularity":400.5}]}""");
        });
        var client = CreateClient(handler);

        var hits = await client.SearchAsync(
            "game",
            new HashSet<BaseItemKind> { BaseItemKind.Movie, BaseItemKind.Series },
            Config(),
            "en-US",
            CancellationToken.None);

        Assert.NotNull(hits);
        Assert.Equal(2, hits.Count);
        Assert.Equal("Game of Thrones", hits[0].Title);
        Assert.Equal(BaseItemKind.Series, hits[0].Kind);
        Assert.Equal("Fight Club", hits[1].Title);
        Assert.Equal(BaseItemKind.Movie, hits[1].Kind);
    }

    /// <summary>
    /// Verifies HTTP failures return an empty list instead of null so search never falls through.
    /// </summary>
    [Fact]
    public async Task SearchAsync_HttpFailureReturnsEmptyList()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        var hits = await client.SearchAsync(
            "matrix",
            new HashSet<BaseItemKind> { BaseItemKind.Movie },
            Config(),
            "en-US",
            CancellationToken.None);

        Assert.NotNull(hits);
        Assert.Empty(hits);
    }

    /// <summary>
    /// Verifies movie details include credits and runtime without touching search endpoints.
    /// </summary>
    [Fact]
    public async Task GetTitleDetailsAsync_MapsMovieCreditsAndRuntime()
    {
        var handler = new ScriptedHandler(_ => Json(
            """
            {"id":550,"title":"Fight Club","overview":"Office worker","release_date":"1999-10-15","runtime":139,"vote_average":8.4,"tagline":"Mischief. Mayhem. Soap.","poster_path":"/p.jpg","genres":[{"id":18,"name":"Drama"}],"production_companies":[{"name":"Fox 2000 Pictures"}],"credits":{"cast":[{"name":"Brad Pitt","character":"Tyler Durden","order":0}],"crew":[{"name":"David Fincher","job":"Director"}]}}
            """));
        var client = CreateClient(handler);

        var details = await client.GetTitleDetailsAsync(
            BaseItemKind.Movie,
            550,
            Config(),
            "en-US",
            CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal("Fight Club", details.Title);
        Assert.Equal(139, details.RuntimeMinutes);
        Assert.Equal(8.4f, details.VoteAverage);
        Assert.Equal(["Drama"], details.Genres);
        Assert.Contains(details.People, person => person.Name == "Brad Pitt" && person.Type == PersonKind.Actor);
        Assert.Contains(details.People, person => person.Name == "David Fincher" && person.Type == PersonKind.Director);
        Assert.Contains("movie/550", Assert.Single(handler.RequestUris).AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("append_to_response=credits", handler.RequestUris[0].Query, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies TV details use name, first-air date, and episode runtime.
    /// </summary>
    [Fact]
    public async Task GetTitleDetailsAsync_MapsSeriesEpisodeRuntime()
    {
        var handler = new ScriptedHandler(_ => Json(
            """
            {"id":1399,"name":"Game of Thrones","overview":"Westeros","first_air_date":"2011-04-17","episode_run_time":[60],"vote_average":8.9,"genres":[{"name":"Sci-Fi & Fantasy"}],"credits":{"cast":[],"crew":[{"name":"D. B. Weiss","job":"Writer"}]}}
            """));
        var client = CreateClient(handler);

        var details = await client.GetTitleDetailsAsync(
            BaseItemKind.Series,
            1399,
            Config(),
            "en-US",
            CancellationToken.None);

        Assert.NotNull(details);
        Assert.Equal(BaseItemKind.Series, details.Kind);
        Assert.Equal("Game of Thrones", details.Title);
        Assert.Equal(2011, details.Year);
        Assert.Equal(60, details.RuntimeMinutes);
        Assert.Contains(details.People, person => person.Type == PersonKind.Writer);
    }

    /// <summary>
    /// Verifies TMDB detail failures return null so the cached search stub can still paint.
    /// </summary>
    [Fact]
    public async Task GetTitleDetailsAsync_HttpFailureReturnsNull()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = CreateClient(handler);

        var details = await client.GetTitleDetailsAsync(
            BaseItemKind.Movie,
            550,
            Config(),
            "en-US",
            CancellationToken.None);

        Assert.Null(details);
    }

    /// <summary>
    /// Verifies untitled TMDB rows are dropped and remaining hits are still returned.
    /// </summary>
    [Fact]
    public async Task SearchAsync_SkipsUntitledRows()
    {
        var handler = new ScriptedHandler(_ => Json(
            """{"results":[{"id":1,"title":"","popularity":10},{"id":603,"title":"The Matrix","release_date":"1999-03-31","popularity":80}]}"""));
        var client = CreateClient(handler);

        var hits = await client.SearchAsync(
            "matrix",
            new HashSet<BaseItemKind> { BaseItemKind.Movie },
            Config(),
            "en-US",
            CancellationToken.None);

        Assert.NotNull(hits);
        var movie = Assert.Single(hits);
        Assert.Equal(603, movie.TmdbId);
        Assert.Equal("The Matrix", movie.Title);
        Assert.Equal(1999, movie.Year);
    }

    private static TmdbClient CreateClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.themoviedb.org/"),
            Timeout = TmdbClient.HttpTimeout,
        };

        return new TmdbClient(http, NullLogger<TmdbClient>.Instance);
    }

    private static PluginConfiguration Config() => new()
    {
        TmdbApiKey = "test-key",
        CacheTtlSeconds = 0,
    };

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    /// <summary>
    /// Returns scripted HTTP responses and records requested URIs.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null)
            {
                RequestUris.Add(request.RequestUri);
            }

            return Task.FromResult(_respond(request));
        }
    }
}
