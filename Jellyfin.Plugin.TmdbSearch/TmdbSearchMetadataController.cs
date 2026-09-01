using Jellyfin.Plugin.TmdbSearch.Configuration;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Fast item metadata for TMDB search stubs, independent of Gelato stream sync.
/// </summary>
[ApiController]
[Authorize]
[Route("TmdbSearch")]
public sealed class TmdbSearchMetadataController : ControllerBase
{
    private readonly TmdbItemMetadataService _metadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbSearchMetadataController"/> class.
    /// </summary>
    /// <param name="metadata">Stub metadata service.</param>
    public TmdbSearchMetadataController(TmdbItemMetadataService metadata)
    {
        _metadata = metadata;
    }

    /// <summary>
    /// Returns TMDB-backed metadata for an unowned search stub.
    /// </summary>
    /// <param name="itemId">Synthetic search item id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item DTO without playable media sources, or 404 when unknown.</returns>
    [HttpGet("Items/{itemId:guid}/Metadata")]
    [ProducesResponseType(typeof(BaseItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseItemDto>> GetItemMetadata(
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken)
    {
        if (!PluginSettings.Current.EnableImmediateTmdbMetadata)
        {
            return NotFound();
        }

        var dto = await _metadata.TryGetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (dto is null)
        {
            return NotFound();
        }

        return Ok(dto);
    }
}
