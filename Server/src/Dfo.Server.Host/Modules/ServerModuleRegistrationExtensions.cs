using Dfo.Server.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Host.Modules;

internal static class ServerModuleRegistrationExtensions
{
    public static IHostBuilder AddServerModules(
        this IHostBuilder builder,
        params IServerModule[] modules)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);

        var duplicateName = modules
            .GroupBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;

        if (duplicateName is not null)
        {
            throw new InvalidOperationException($"Server module '{duplicateName}' was registered more than once.");
        }

        return builder.ConfigureServices((context, services) =>
        {
            foreach (var module in modules)
            {
                module.ConfigureServices(services, context.Configuration);
                services.AddSingleton<IServerModule>(module);
            }
        });
    }
}
