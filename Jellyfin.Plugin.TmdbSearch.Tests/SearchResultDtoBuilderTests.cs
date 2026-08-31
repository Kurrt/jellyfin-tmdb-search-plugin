using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Xunit;

namespace Jellyfin.Plugin.TmdbSearch.Tests;

/// <summary>
/// Tests for lightweight search DTOs and Gelato seed payloads.
/// </summary>
public sealed class SearchResultDtoBuilderTests
{
    /// <summary>
    /// Verifies an unowned hit uses Gelato's TMDB Stremio GUID and provider id.
    /// </summary>
    [Fact]
    public void CreateStub_UsesGelatoTmdbGuidAndProviderId()
    {
        var hit = new TmdbSearchHit(
            550,
            BaseItemKind.Movie,
            "Fight Club",
            1999,
            "/p.jpg",
            "An insomniac office worker...",
            91.2);

        var stub = SearchResultDtoBuilder.CreateStub(hit);

        var expectedId = StremioGuidHelper.ToGuid(StremioMediaKind.Movie, "tmdb:550");
        Assert.Equal(expectedId, stub.Dto.Id);
        Assert.Equal("Fight Club", stub.Dto.Name);
        Assert.Equal("An insomniac office worker...", stub.Dto.Overview);
        Assert.Equal(1999, stub.Dto.ProductionYear);
        Assert.Equal(BaseItemKind.Movie, stub.Dto.Type);
        Assert.Equal(MediaType.Video, stub.Dto.MediaType);
        Assert.Equal("550", stub.Dto.ProviderIds[MetadataProvider.Tmdb.ToString()]);
        Assert.Contains(ImageType.Primary, stub.Dto.ImageTags.Keys);
        Assert.Equal("https://image.tmdb.org/t/p/w780/p.jpg", stub.Gelato.PosterUrl);
        Assert.Equal("tmdb:550", stub.Gelato.ExternalId);
        Assert.Equal("Fight Club", stub.Gelato.Name);
        Assert.Equal("An insomniac office worker...", stub.Gelato.Description);
        Assert.Equal(StremioMediaKind.Movie, stub.Gelato.Kind);
        Assert.Equal(expectedId, stub.Gelato.Guid);
    }

    /// <summary>
    /// Verifies Infuse-friendly media sources are local stubs, not remote URLs.
    /// </summary>
    [Fact]
    public void CreateStub_IncludesLocalMediaSourceStub()
    {
        var hit = new TmdbSearchHit(603, BaseItemKind.Movie, "The Matrix", 1999, null, null, 1);
        var stub = SearchResultDtoBuilder.CreateStub(hit);

        var source = Assert.Single(stub.Dto.MediaSources);
        Assert.Equal("/stub", source.Path);
        Assert.Equal(MediaProtocol.File, source.Protocol);
        Assert.False(source.IsRemote);
        Assert.False(source.SupportsDirectPlay);
        Assert.True(source.SupportsDirectStream);
    }

    /// <summary>
    /// Verifies a series stub uses the series Stremio GUID namespace.
    /// </summary>
    [Fact]
    public void CreateStub_SeriesGuidDiffersFromMovie()
    {
        var hit = new TmdbSearchHit(1399, BaseItemKind.Series, "Game of Thrones", 2011, null, "Westeros", 1);
        var stub = SearchResultDtoBuilder.CreateStub(hit);

        Assert.Equal(StremioGuidHelper.ToGuid(StremioMediaKind.Series, "tmdb:1399"), stub.Dto.Id);
        Assert.Equal(BaseItemKind.Series, stub.Dto.Type);
        Assert.Equal(StremioMediaKind.Series, stub.Gelato.Kind);
    }
}
