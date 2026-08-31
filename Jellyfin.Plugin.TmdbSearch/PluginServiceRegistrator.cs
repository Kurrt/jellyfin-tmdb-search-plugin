using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

        services.AddSingleton<TmdbLibraryIndex>();
        services.AddSingleton<GelatoMetaBridge>();
        services.AddSingleton<TmdbSearchActionFilter>();
        services.AddSingleton<IHostedService, TmdbLibraryIndexHostedService>();

        services.PostConfigure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
        {
            options.Filters.AddService<TmdbSearchActionFilter>(order: 0);
        });
    }
}
