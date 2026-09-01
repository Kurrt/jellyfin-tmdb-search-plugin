using System.Globalization;
using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for the in-memory TMDB poster cache used to proxy search-stub artwork.
/// </summary>
public sealed class TmdbPosterCacheTests
{
    /// <summary>
    /// Verifies a TMDB poster URL can be stored and read back by item id.
    /// </summary>
    [Fact]
    public void Set_StoresAllowedTmdbPosterUrl()
    {
        var cache = new TmdbPosterCache();
        var itemId = Guid.Parse("e1401760-fba7-607e-344e-f10279925267");
        var url = "https://image.tmdb.org/t/p/w780/p.jpg";

        cache.Set(itemId, url);

        Assert.True(cache.TryGet(itemId, out var stored));
        Assert.Equal(url, stored);
    }

    /// <summary>
    /// Verifies non-TMDB URLs are rejected so the image proxy cannot be used for SSRF.
    /// </summary>
    [Theory]
    [InlineData("https://evil.example/p.jpg")]
    [InlineData("http://image.tmdb.org/t/p/w780/p.jpg")]
    [InlineData("")]
    [InlineData(null)]
    public void Set_IgnoresUrlsOutsideTmdbCdn(string? url)
    {
        var cache = new TmdbPosterCache();
        var itemId = Guid.NewGuid();

        cache.Set(itemId, url);

        Assert.False(cache.TryGet(itemId, out _));
    }

    /// <summary>
    /// Verifies a search stub DTO can be served later even when no poster URL is present.
    /// </summary>
    [Fact]
    public void Set_StoresDtoForItemDetailLookup()
    {
        var cache = new TmdbPosterCache();
        var itemId = Guid.Parse("3ed52899-c7ff-a850-617d-da69c07207bf");
        var dto = new MediaBrowser.Model.Dto.BaseItemDto
        {
            Id = itemId,
            Name = "Fight Club",
        };

        cache.Set(itemId, dto, posterUrl: null);

        Assert.True(cache.TryGetDto(itemId, out var stored));
        Assert.Equal("Fight Club", stored.Name);
        Assert.False(cache.TryGet(itemId, out _));
    }

    /// <summary>
    /// Verifies expired entries are not served after the TTL elapses.
    /// </summary>
    [Fact]
    public void TryGet_ReturnsFalseAfterTtl()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-09-01T04:00:00Z", CultureInfo.InvariantCulture));
        var cache = new TmdbPosterCache(time, TimeSpan.FromMinutes(1));
        var itemId = Guid.NewGuid();

        cache.Set(itemId, "https://image.tmdb.org/t/p/w780/p.jpg");
        time.Advance(TimeSpan.FromMinutes(2));

        Assert.False(cache.TryGet(itemId, out _));
    }

    /// <summary>
    /// Controllable clock for TTL tests.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public void Advance(TimeSpan delta) => _utcNow += delta;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
