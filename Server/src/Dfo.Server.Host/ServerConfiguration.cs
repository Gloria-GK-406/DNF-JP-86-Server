using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Host;

/// <summary>Loads one configuration before optional components are selected.</summary>
internal static class ServerConfiguration
{
    public static ConfigurationManager Load(string[] args, out string environment)
    {
        using var hostSettings = new ConfigurationManager();
        hostSettings.AddEnvironmentVariables("ASPNETCORE_")
            .AddEnvironmentVariables("DOTNET_")
            .AddCommandLine(args);
        environment = hostSettings[HostDefaults.EnvironmentKey] ?? Environments.Production;
        var configuration = new ConfigurationManager();
        configuration.SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            // Preserve the existing host's balance-file precedence.
            .AddJsonFile("experience.json", optional: true, reloadOnChange: false);
        ScriptChannelConfiguration.Apply(configuration);
        return configuration;
    }
}
