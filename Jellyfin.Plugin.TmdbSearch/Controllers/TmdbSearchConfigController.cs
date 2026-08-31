using Jellyfin.Plugin.TmdbSearch.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch.Controllers;

/// <summary>
/// Dedicated settings API for TMDB Search (bypasses the generic plugin configuration endpoint).
/// </summary>
[ApiController]
[Route("TmdbSearch")]
public sealed class TmdbSearchConfigController : ControllerBase
{
    private readonly ILogger<TmdbSearchConfigController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbSearchConfigController"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public TmdbSearchConfigController(ILogger<TmdbSearchConfigController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Returns the current plugin settings.
    /// </summary>
    /// <returns>The settings DTO.</returns>
    [HttpGet("Configuration")]
    [Authorize]
    [ProducesResponseType(typeof(TmdbSearchConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<TmdbSearchConfigDto> GetConfiguration()
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return NotFound();
        }

        return Ok(TmdbSearchConfigDto.From(plugin.Configuration));
    }

    /// <summary>
    /// Persists plugin settings to disk.
    /// </summary>
    /// <param name="dto">The settings payload.</param>
    /// <returns>No content on success.</returns>
    [HttpPost("Configuration")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult SaveConfiguration([FromBody] TmdbSearchConfigDto? dto)
    {
        if (dto is null)
        {
            return BadRequest("Missing configuration body.");
        }

        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return NotFound();
        }

        var config = plugin.Configuration;
        dto.ApplyTo(config);
        plugin.UpdateConfiguration(config);

        _logger.LogInformation(
            "TMDB Search settings saved (api key configured: {HasKey}, language: {Language})",
            !string.IsNullOrWhiteSpace(config.TmdbApiKey),
            string.IsNullOrWhiteSpace(config.Language) ? "(server default)" : config.Language);

        return NoContent();
    }
}
