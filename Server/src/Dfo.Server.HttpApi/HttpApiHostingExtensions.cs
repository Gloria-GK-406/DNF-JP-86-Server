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
    public static IHostBuilder ConfigureHttpApi(this IHostBuilder builder, IConfiguration configuration)
    {
        var options = HttpApiOptions.FromConfiguration(configuration);
        if (!options.Enabled) return builder;

        return builder.ConfigureWebHostDefaults(web =>
        {
            web.UseUrls(options.ListenUrl!.AbsoluteUri.TrimEnd('/'));
            web.ConfigureServices((context, services) =>
            {
                var module = new HttpApiModule();
                module.ConfigureServices(services, context.Configuration);
                services.AddSingleton<IServerModule>(module);
            });
            web.Configure(app =>
            {
                HttpApiApplicationExtensions.UseHttpApiMiddleware(app);
                app.UseEndpoints(HttpApiApplicationExtensions.MapHttpApiEndpoints);
            });
        });
    }
}
