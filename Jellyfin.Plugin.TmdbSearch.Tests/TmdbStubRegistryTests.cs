using Jellyfin.Data.Enums;
using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for the stub GUID → TMDB id registry used to redirect stale search-stub lookups.
/// </summary>
public class TmdbStubRegistryTests
{
    /// <summary>
    /// Verifies a registered stub resolves back to its TMDB id and kind.
    /// </summary>
    [Fact]
    public void TryGetTmdbId_ReturnsRegisteredValue()
    {
        var registry = new TmdbStubRegistry();
        var stubId = StremioGuidHelper.ToGuid(StremioMediaKind.Movie, "tmdb:550");

        registry.Register(stubId, BaseItemKind.Movie, 550);

        Assert.True(registry.TryGetTmdbId(stubId, out var kind, out var tmdbId));
        Assert.Equal(BaseItemKind.Movie, kind);
        Assert.Equal(550, tmdbId);
    }

    /// <summary>
    /// Verifies an unknown GUID is reported as not found rather than defaulting silently.
    /// </summary>
    [Fact]
    public void TryGetTmdbId_ReturnsFalseForUnknownStub()
    {
        var registry = new TmdbStubRegistry();

        Assert.False(registry.TryGetTmdbId(Guid.NewGuid(), out var kind, out var tmdbId));
        Assert.Equal(default, kind);
        Assert.Equal(0, tmdbId);
    }

    /// <summary>
    /// Verifies re-registering the same stub GUID (e.g. the user searches again) simply
    /// overwrites the entry rather than throwing or duplicating state.
    /// </summary>
    [Fact]
    public void Register_OverwritesExistingEntry()
    {
        var registry = new TmdbStubRegistry();
        var stubId = StremioGuidHelper.ToGuid(StremioMediaKind.Series, "tmdb:1399");

        registry.Register(stubId, BaseItemKind.Series, 1399);
        registry.Register(stubId, BaseItemKind.Series, 1399);

        Assert.True(registry.TryGetTmdbId(stubId, out var kind, out var tmdbId));
        Assert.Equal(BaseItemKind.Series, kind);
        Assert.Equal(1399, tmdbId);
    }
}
