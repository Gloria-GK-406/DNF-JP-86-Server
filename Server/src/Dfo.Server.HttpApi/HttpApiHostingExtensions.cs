using Dfo.Server.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.HttpApi;

/// <summary>Registers HTTP on the same Generic Host, without a second container or executable.</summary>
public static class HttpApiHostingExtensions
{
    public static IHostBuilder ConfigureHttpApi(this IHostBuilder builder, IConfiguration configuration,
        params IHttpApiFeature[] features)
    {
        var options = HttpApiOptions.FromConfiguration(configuration);
        var active = features.Where(feature => feature.Enabled).ToArray();
        var listeners = (options.Enabled ? new[] { options.ListenUrl! } : Array.Empty<Uri>())
            .Concat(active.Select(feature => feature.ListenUrl
                ?? throw new InvalidOperationException("An enabled HTTP feature requires a listener."))).ToArray();
        if (listeners.Length == 0) return builder;
        if (listeners.Select(url => url.Port).Distinct().Count() != listeners.Length)
            throw new InvalidOperationException("HTTP features require distinct listener ports.");

        return builder.ConfigureWebHostDefaults(web =>
        {
            web.UseUrls(listeners.Select(url => url.AbsoluteUri.TrimEnd('/')).ToArray());
            web.ConfigureServices((context, services) =>
            {
                var module = new HttpApiModule();
                module.ConfigureServices(services, context.Configuration);
                services.AddSingleton<IServerModule>(module);
                foreach (var feature in active) feature.ConfigureServices(services);
            });
            web.Configure(app =>
            {
                HttpApiApplicationExtensions.UseHttpApiMiddleware(app);
                foreach (var feature in active) feature.ConfigureMiddleware(app);
                app.UseEndpoints(endpoints =>
                {
                    if (options.Enabled)
                        HttpApiApplicationExtensions.MapHttpApiEndpoints(endpoints,
                            new HttpListenerScope(options.ListenUrl!.Port));
                    foreach (var feature in active) feature.MapEndpoints(endpoints);
                });
            });
        });
    }
}
