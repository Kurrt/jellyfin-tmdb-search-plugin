namespace Jellyfin.Plugin.TmdbSearch.Configuration;

/// <summary>
/// Live plugin settings, read at request time so toggles apply without a rebuild.
/// </summary>
public static class PluginSettings
{
    private static readonly AsyncLocal<PluginConfiguration?> Override = new();
    private static Func<PluginConfiguration?>? _live;

    /// <summary>
    /// Gets the current configuration, or constructor defaults when the plugin is not loaded.
    /// </summary>
    public static PluginConfiguration Current =>
        Override.Value ?? _live?.Invoke() ?? new PluginConfiguration();

    /// <summary>
    /// Binds a live configuration source. Called when the plugin starts so this type
    /// does not load Jellyfin plugin assemblies in unit tests.
    /// </summary>
    /// <param name="live">Returns the running plugin configuration, or null when unavailable.</param>
    public static void Bind(Func<PluginConfiguration?> live)
    {
        ArgumentNullException.ThrowIfNull(live);
        _live = live;
    }

    /// <summary>
    /// Overrides <see cref="Current"/> for the remainder of the async context. Used by tests.
    /// </summary>
    /// <param name="config">Configuration to expose as current.</param>
    /// <returns>A scope that restores the previous override when disposed.</returns>
    public static IDisposable OverrideCurrent(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var previous = Override.Value;
        Override.Value = config;
        return new OverrideScope(previous);
    }

    private sealed class OverrideScope : IDisposable
    {
        private readonly PluginConfiguration? _previous;
        private bool _disposed;

        public OverrideScope(PluginConfiguration? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Override.Value = _previous;
            _disposed = true;
        }
    }
}
