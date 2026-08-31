using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Seeds Gelato's in-memory Stremio meta cache via reflection so click-to-insert works.
/// </summary>
public sealed class GelatoMetaBridge
{
    private readonly IServiceProvider _services;
    private readonly ILogger<GelatoMetaBridge> _logger;

    private Type? _managerType;
    private MethodInfo? _saveMetaMethod;
    private Type? _metaType;
    private Type? _mediaTypeEnum;
    private bool _lookupAttempted;

    /// <summary>
    /// Initializes a new instance of the <see cref="GelatoMetaBridge"/> class.
    /// </summary>
    /// <param name="services">Root service provider.</param>
    /// <param name="logger">Logger instance.</param>
    public GelatoMetaBridge(IServiceProvider services, ILogger<GelatoMetaBridge> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>
    /// Returns true when Gelato types were discovered in the running server.
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            EnsureLookup();
            return _saveMetaMethod is not null && _metaType is not null && _mediaTypeEnum is not null;
        }
    }

    /// <summary>
    /// Stores a minimal Stremio meta object in Gelato's cache for the given search GUID.
    /// </summary>
    /// <param name="guid">Deterministic Gelato search GUID.</param>
    /// <param name="kind">Movie or series.</param>
    /// <param name="externalId">Stremio external id (IMDb or tmdb:).</param>
    /// <param name="imdbId">Optional IMDb id for Gelato meta fetch.</param>
    public void SaveSearchMeta(Guid guid, StremioMediaKind kind, string externalId, string? imdbId)
    {
        EnsureLookup();
        if (_saveMetaMethod is null || _metaType is null || _mediaTypeEnum is null)
        {
            return;
        }

        var manager = _services.GetService(_managerType!);
        if (manager is null)
        {
            return;
        }

        try
        {
            var meta = Activator.CreateInstance(_metaType);
            if (meta is null)
            {
                return;
            }

            var enumValue = Enum.Parse(_mediaTypeEnum, kind.ToString());
            SetProperty(meta, "Type", enumValue);
            SetProperty(meta, "Id", externalId);
            SetProperty(meta, "ImdbId", imdbId);

            _saveMetaMethod.Invoke(manager, [guid, meta]);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to seed Gelato meta cache for {Guid}", guid);
        }
    }

    private void EnsureLookup()
    {
        if (_lookupAttempted)
        {
            return;
        }

        _lookupAttempted = true;

        var gelatoAssembly = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(static assembly => string.Equals(
                assembly.GetName().Name,
                "Gelato",
                StringComparison.OrdinalIgnoreCase));

        if (gelatoAssembly is null)
        {
            return;
        }

        _managerType = gelatoAssembly.GetType("Gelato.GelatoManager");
        _metaType = gelatoAssembly.GetType("Gelato.StremioMeta");
        _mediaTypeEnum = gelatoAssembly.GetType("Gelato.StremioMediaType");

        if (_managerType is null || _metaType is null || _mediaTypeEnum is null)
        {
            return;
        }

        _saveMetaMethod = _managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(method =>
                string.Equals(method.Name, "SaveStremioMeta", StringComparison.Ordinal)
                && method.GetParameters().Length == 2);
    }

    private static void SetProperty(object target, string propertyName, object? value)
    {
        var property = target.GetType().GetProperty(propertyName);
        if (property is null || !property.CanWrite)
        {
            return;
        }

        property.SetValue(target, value);
    }
}
