using Dfo.Server.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Host.Modules;

internal static class ServerModuleRegistrationExtensions
{
    public static IHostApplicationBuilder AddServerModules(
        this IHostApplicationBuilder builder,
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

        foreach (var module in modules)
        {
            module.ConfigureServices(builder.Services, builder.Configuration);
            builder.Services.AddSingleton<IServerModule>(module);
        }

        return builder;
    }
}
