using Dfo.Server.Host;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

try
{
    using var configuration = ServerConfiguration.Load(args, out var environment);
    using var host = Host.CreateDefaultBuilder(args)
        .UseContentRoot(AppContext.BaseDirectory)
        .UseEnvironment(environment)
        .ConfigureAppConfiguration((_, sources) =>
        {
            sources.Sources.Clear();
            sources.AddConfiguration(configuration);
        })
        .Compose(configuration)
        .Build();

    await host.RunAsync();
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Server host terminated: {error}");
    return 1;
}
