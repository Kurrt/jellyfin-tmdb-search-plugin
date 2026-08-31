using System.Collections.Concurrent;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Lazy in-memory index mapping TMDB provider ids to Jellyfin item GUIDs.
/// </summary>
public sealed class TmdbLibraryIndex
{
    private readonly ConcurrentDictionary<(BaseItemKind Kind, int TmdbId), Guid> _index = new();

    /// <summary>
    /// Tries to resolve a library item id for the given TMDB id.
    /// </summary>
    /// <param name="kind">Movie or series.</param>
    /// <param name="tmdbId">TMDB numeric id.</param>
    /// <param name="itemId">The Jellyfin item id when found.</param>
    /// <returns>True when the item is indexed.</returns>
    public bool TryGetItemId(BaseItemKind kind, int tmdbId, out Guid itemId) =>
        _index.TryGetValue((kind, tmdbId), out itemId);

    /// <summary>
    /// Adds or updates an index entry for a library item.
    /// </summary>
    /// <param name="item">The library item.</param>
    public void IndexItem(BaseItem item)
    {
        var kind = item.GetBaseItemKind();
        if (kind is not (BaseItemKind.Movie or BaseItemKind.Series))
        {
            return;
        }

        var tmdbId = item.GetProviderId(MetadataProvider.Tmdb);
        if (!int.TryParse(tmdbId, out var parsedId))
        {
            return;
        }

        _index[(kind, parsedId)] = item.Id;
    }

    /// <summary>
    /// Removes an index entry for a library item.
    /// </summary>
    /// <param name="item">The removed library item.</param>
    public void RemoveItem(BaseItem item)
    {
        var kind = item.GetBaseItemKind();
        if (kind is not (BaseItemKind.Movie or BaseItemKind.Series))
        {
            return;
        }

        var tmdbId = item.GetProviderId(MetadataProvider.Tmdb);
        if (!int.TryParse(tmdbId, out var parsedId))
        {
            return;
        }

        _index.TryRemove((kind, parsedId), out _);
    }

    /// <summary>
    /// Replaces the index contents from a batch of library items.
    /// </summary>
    /// <param name="items">Items to index.</param>
    public void ReplaceBatch(IEnumerable<BaseItem> items)
    {
        foreach (var item in items)
        {
            IndexItem(item);
        }
    }
}

/// <summary>
/// Background service that warms the TMDB library index after startup.
/// </summary>
public sealed class TmdbLibraryIndexHostedService : IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly TmdbLibraryIndex _index;
    private readonly ILogger<TmdbLibraryIndexHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbLibraryIndexHostedService"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="index">Shared TMDB index.</param>
    /// <param name="logger">Logger instance.</param>
    public TmdbLibraryIndexHostedService(
        ILibraryManager libraryManager,
        TmdbLibraryIndex index,
        ILogger<TmdbLibraryIndexHostedService> logger)
    {
        _libraryManager = libraryManager;
        _index = index;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        _libraryManager.ItemRemoved += OnItemRemoved;

        _ = Task.Run(() => WarmIndexAsync(cancellationToken), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        return Task.CompletedTask;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e) => _index.IndexItem(e.Item);

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e) => _index.IndexItem(e.Item);

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e) => _index.RemoveItem(e.Item);

    private async Task WarmIndexAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
                Recursive = true,
                IsDeadPerson = true,
            };

            var items = _libraryManager.GetItemList(query);
            _index.ReplaceBatch(items);

            _logger.LogInformation(
                "TMDB Search indexed {Count} library movies/series with TMDB provider ids",
                items.Count);
        }
        catch (OperationCanceledException)
        {
            // Server is shutting down during warm-up.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TMDB Search library index warm-up failed");
        }
    }
}
