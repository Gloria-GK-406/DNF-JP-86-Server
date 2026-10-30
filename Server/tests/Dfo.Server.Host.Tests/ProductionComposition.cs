using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Host.Tests;

/// <summary>The executable's composition, every component switched on, over a throwaway database; built, not started.</summary>
internal sealed class ProductionComposition : IDisposable
{
    private readonly string directory;
    private readonly IHost host;

    private ProductionComposition(string directory, IHost host, IServiceCollection services) =>
        (this.directory, this.host, Services) = (directory, host, services);

    public IServiceCollection Services { get; }

    public IServiceProvider Provider => host.Services;

    /// <summary>
    /// This server's classes the container constructs: registered implementation types, and the instances factory
    /// registrations produce. Objects a service assembles for itself are not here; their owner holds the dependency.
    /// </summary>
    public IReadOnlyList<Type> Built()
    {
        using var scope = host.Services.CreateScope();
        var produced = Services
            .Where(x => !x.IsKeyedService && x.ImplementationFactory is not null && !x.ServiceType.ContainsGenericParameters)
            .Select(x => x.ServiceType).Distinct()
            .SelectMany(x => scope.ServiceProvider.GetServices(x)).OfType<object>().Select(x => x.GetType());
        return Services.Where(x => !x.IsKeyedService).Select(x => x.ImplementationType).OfType<Type>().Concat(produced)
            .Where(x => x.Assembly.GetName().Name!.StartsWith("Dfo.", StringComparison.Ordinal) && !x.ContainsGenericParameters)
            .Distinct().ToArray();
    }

    public static ProductionComposition Build()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"required-dependencies-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        // The shipped appsettings, as the executable loads them, with both HTTP features on so their services are in
        // the graph; the host is built, never started, so nothing listens.
        using var configuration = ServerConfiguration.Load(
        [
            $"--Database:Path={Path.Combine(directory, "test.db")}", "--ContentWarmup:Enabled=false", "--Networking:Enabled=false",
            "--LauncherAuth:Enabled=true", "--LauncherAuth:ListenUrl=http://127.0.0.1:41831/",
            "--GmWeb:Enabled=true", "--GmWeb:ListenUrl=http://127.0.0.1:41832/",
        ], out _);
        IServiceCollection? captured = null;
        var host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseEnvironment(Environments.Production)
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureAppConfiguration((_, sources) =>
            {
                sources.Sources.Clear();
                sources.AddConfiguration(configuration);
            })
            .Compose(configuration)
            .ConfigureServices(services => captured = services)
            .Build();
        return new(directory, host, captured!);
    }

    public void Dispose()
    {
        host.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }
}
