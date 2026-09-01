using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch.Web;

/// <summary>
/// Registers the async stream-loader script with File Transformation and/or JavaScript Injector.
/// </summary>
public sealed class TmdbSearchJavaScriptRegistrationService : IHostedService, IDisposable
{
    private readonly ILogger<TmdbSearchJavaScriptRegistrationService> _logger;
    private readonly object _sync = new();
    private bool _injectorRegistered;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbSearchJavaScriptRegistrationService"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public TmdbSearchJavaScriptRegistrationService(ILogger<TmdbSearchJavaScriptRegistrationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Plugin.PluginConfigurationChanged += OnConfigurationChanged;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not subscribe to TMDB Search configuration changes");
        }

        ApplyRegistration(Plugin.Instance?.Configuration.EnableAsyncStreamUi ?? true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            Plugin.PluginConfigurationChanged -= OnConfigurationChanged;
        }
        catch (Exception)
        {
            // ignored
        }

        lock (_sync)
        {
            UnregisterJavaScriptInjector();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Plugin.PluginConfigurationChanged -= OnConfigurationChanged;
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private void OnConfigurationChanged(Configuration.PluginConfiguration config)
    {
        ApplyRegistration(config.EnableAsyncStreamUi);
    }

    private void ApplyRegistration(bool enabled)
    {
        lock (_sync)
        {
            var fileTransformation = TryRegisterFileTransformation();
            if (enabled)
            {
                var injector = TryRegisterJavaScriptInjector();
                if (!fileTransformation && !injector)
                {
                    _logger.LogWarning(
                        "Async stream UI is enabled but neither File Transformation nor JavaScript Injector is installed. " +
                        "Item details will keep blocking on Gelato stream sync until one of those plugins is installed.");
                }
            }
            else
            {
                UnregisterJavaScriptInjector();
                if (fileTransformation)
                {
                    _logger.LogInformation(
                        "Async stream UI disabled; File Transformation will omit the loader on the next index.html request.");
                }
            }
        }
    }

    private bool TryRegisterFileTransformation()
    {
        try
        {
            var assembly = FindAssembly("Jellyfin.Plugin.FileTransformation");
            if (assembly is null)
            {
                _logger.LogDebug("File Transformation assembly was not found");
                return false;
            }

            var pluginInterface = assembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
            var registerMethod = pluginInterface?.GetMethod(
                "RegisterTransformation",
                BindingFlags.Public | BindingFlags.Static);
            if (registerMethod is null)
            {
                _logger.LogDebug("File Transformation PluginInterface.RegisterTransformation was not found");
                return false;
            }

            var payload = CreateNewtonsoftObject(
                registerMethod.GetParameters()[0].ParameterType,
                new Dictionary<string, object?>
                {
                    ["id"] = Plugin.PluginId.ToString(),
                    ["fileNamePattern"] = @"index\.html",
                    ["callbackAssembly"] = typeof(WebInjection).Assembly.FullName,
                    ["callbackClass"] = typeof(WebInjection).FullName,
                    ["callbackMethod"] = nameof(WebInjection.TransformIndexHtml),
                });

            registerMethod.Invoke(null, [payload]);
            _logger.LogInformation("Registered async stream UI with File Transformation");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register async stream UI with File Transformation");
            return false;
        }
    }

    private bool TryRegisterJavaScriptInjector()
    {
        try
        {
            var assembly = FindAssembly("Jellyfin.Plugin.JavaScriptInjector");
            var pluginInterface = assembly?.GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
            var registerMethod = pluginInterface?.GetMethod("RegisterScript", BindingFlags.Public | BindingFlags.Static);
            if (registerMethod is null)
            {
                _logger.LogDebug("JavaScript Injector PluginInterface.RegisterScript was not found");
                return false;
            }

            UnregisterJavaScriptInjector(pluginInterface);

            var pluginId = Plugin.PluginId.ToString();
            var pluginName = Plugin.Instance?.Name ?? "TMDB Search";
            var pluginVersion = typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown";
            var script = WebInjection.ReadAsyncStreamLoaderScript();
            var payloadType = registerMethod.GetParameters()[0].ParameterType;
            var payload = CreateNewtonsoftObject(
                payloadType,
                new Dictionary<string, object?>
                {
                    ["id"] = $"{WebInjection.ScriptElementId}-{pluginId}",
                    ["name"] = WebInjection.ScriptResourceSuffix,
                    ["script"] = script,
                    ["enabled"] = true,
                    ["requiresAuthentication"] = false,
                    ["pluginId"] = pluginId,
                    ["pluginName"] = pluginName,
                    ["pluginVersion"] = pluginVersion,
                });

            var result = registerMethod.Invoke(null, [payload]);
            if (result is true)
            {
                _injectorRegistered = true;
                _logger.LogInformation("Registered async stream UI with JavaScript Injector");
                return true;
            }

            _logger.LogWarning("JavaScript Injector RegisterScript returned {Result}", result);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register async stream UI with JavaScript Injector");
            return false;
        }
    }

    private void UnregisterJavaScriptInjector()
    {
        if (!_injectorRegistered)
        {
            return;
        }

        var assembly = FindAssembly("Jellyfin.Plugin.JavaScriptInjector");
        var pluginInterface = assembly?.GetType("Jellyfin.Plugin.JavaScriptInjector.PluginInterface");
        UnregisterJavaScriptInjector(pluginInterface);
    }

    private void UnregisterJavaScriptInjector(Type? pluginInterface)
    {
        if (pluginInterface is null)
        {
            _injectorRegistered = false;
            return;
        }

        try
        {
            var unregister = pluginInterface.GetMethod(
                "UnregisterAllScriptsFromPlugin",
                BindingFlags.Public | BindingFlags.Static);
            var result = unregister?.Invoke(null, [Plugin.PluginId.ToString()]);
            if (result is int removed)
            {
                _logger.LogInformation(
                    "Unregistered {Count} script(s) from JavaScript Injector",
                    removed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to unregister scripts from JavaScript Injector");
        }

        _injectorRegistered = false;
    }

    private static Assembly? FindAssembly(string assemblyName) =>
        AssemblyLoadContext.All
            .SelectMany(static context => context.Assemblies)
            .FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)
                || (assembly.FullName?.Contains(assemblyName, StringComparison.OrdinalIgnoreCase) ?? false));

    /// <summary>
    /// Builds a Newtonsoft JObject via reflection so this plugin does not take a compile-time JSON package.
    /// </summary>
    /// <param name="objectType">JObject (or compatible) parameter type.</param>
    /// <param name="values">Property bag to assign.</param>
    /// <returns>The constructed JSON object.</returns>
    private static object CreateNewtonsoftObject(Type objectType, IReadOnlyDictionary<string, object?> values)
    {
        var payload = Activator.CreateInstance(objectType)
            ?? throw new InvalidOperationException($"Could not construct {objectType.FullName}");
        var indexer = objectType.GetProperty("Item", [typeof(string)])
            ?? throw new InvalidOperationException($"{objectType.FullName} has no string indexer");
        var jValueType = objectType.Assembly.GetType("Newtonsoft.Json.Linq.JValue")
            ?? Type.GetType("Newtonsoft.Json.Linq.JValue, Newtonsoft.Json")
            ?? throw new InvalidOperationException("Newtonsoft.Json.Linq.JValue was not found");

        foreach (var (key, value) in values)
        {
            var boxed = value ?? string.Empty;
            var token = Activator.CreateInstance(jValueType, boxed)
                ?? throw new InvalidOperationException("Could not wrap JValue");
            indexer.SetValue(payload, token, [key]);
        }

        return payload;
    }
}
