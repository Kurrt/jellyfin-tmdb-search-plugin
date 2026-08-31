using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for Gelato Stremio meta seeding used by click-to-insert.
/// </summary>
public sealed class GelatoMetaBridgeTests
{
    /// <summary>
    /// Verifies search stubs populate Gelato meta id, name, poster, and description.
    /// </summary>
    [Fact]
    public void PopulateMeta_SetsTmdbIdAndDisplayFields()
    {
        var meta = new FakeStremioMeta();

        GelatoMetaBridge.PopulateMeta(
            meta,
            StremioMediaKind.Movie,
            "tmdb:550",
            "Fight Club",
            "https://image.tmdb.org/t/p/w780/p.jpg",
            "An insomniac office worker...");

        Assert.Equal(StremioMediaKind.Movie, meta.Type);
        Assert.Equal("tmdb:550", meta.Id);
        Assert.Equal("Fight Club", meta.Name);
        Assert.Equal("https://image.tmdb.org/t/p/w780/p.jpg", meta.Poster);
        Assert.Equal("An insomniac office worker...", meta.Description);
    }

    /// <summary>
    /// Stand-in for Gelato.StremioMeta so reflection setters can be verified without Gelato.
    /// </summary>
    private sealed class FakeStremioMeta
    {
        public object? Type { get; set; }

        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Poster { get; set; }

        public string? Description { get; set; }
    }
}
