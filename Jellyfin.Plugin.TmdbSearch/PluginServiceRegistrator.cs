using Jellyfin.Plugin.TmdbSearch.Web;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TmdbSearch;

/// <summary>
/// Registers plugin services with Jellyfin dependency injection.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddHttpClient<TmdbClient>(client =>
            {
                client.BaseAddress = new Uri("https://api.themoviedb.org/");
                client.Timeout = TmdbClient.HttpTimeout;
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler
            {
                ConnectTimeout = TmdbClient.ConnectTimeout,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddSingleton(sp =>
        {
            var paths = sp.GetRequiredService<IApplicationPaths>();
            var logger = sp.GetRequiredService<ILogger<TmdbPosterCache>>();
            var persistPath = Path.Combine(paths.DataPath, "tmdbsearch-stubs.json");
            return new TmdbPosterCache(persistPath: persistPath, logger: logger);
        });
        services.AddSingleton<TmdbLibraryIndex>();
        services.AddSingleton<GelatoMetaBridge>();
        services.AddSingleton<TmdbSearchActionFilter>();
        services.AddSingleton<TmdbImageResourceFilter>();
        services.AddSingleton<TmdbItemActionFilter>();
        services.AddSingleton<IHostedService, TmdbLibraryIndexHostedService>();
        services.AddSingleton<IHostedService, TmdbSearchJavaScriptRegistrationService>();

        services.AddHttpClient(TmdbImageResourceFilter.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.PostConfigure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
        {
            options.Filters.AddService<TmdbImageResourceFilter>(order: -1);
            options.Filters.AddService<TmdbSearchActionFilter>(order: 0);
            options.Filters.AddService<TmdbItemActionFilter>(order: 2);
        });
    }
}
