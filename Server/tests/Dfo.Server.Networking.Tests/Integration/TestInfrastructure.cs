using Microsoft.Data.Sqlite;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Networking.Tests.Integration;

// Socket tests still use real command registration and therefore require the complete
// persistence/content graph. Keep each host's database and channel script isolated.
internal sealed class TestInfrastructure(IHost inner, string directory) : IHost
{
    public IServiceProvider Services => inner.Services;
    public Task StartAsync(CancellationToken cancellationToken = default) => inner.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken = default) => inner.StopAsync(cancellationToken);
    public void Dispose()
    {
        inner.Dispose();
        using var poolKey = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(directory, "test.db") }.ToString());
        SqliteConnection.ClearPool(poolKey);
        Directory.Delete(directory, recursive: true);
    }

    public static IHost Build(HostApplicationBuilder builder)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dfo-network-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var script = Path.Combine(directory, "channel_info.etc");
            File.WriteAllBytes(script, [1]);
            var configuration = new ConfigurationBuilder().AddConfiguration(builder.Configuration)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Path"] = Path.Combine(directory, "test.db"),
                    ["Channel:ScriptPath"] = script,
                }).Build();
            new InfrastructureModule().ConfigureServices(builder.Services, configuration);
            new PersistenceModule().ConfigureServices(builder.Services, configuration);
            return new TestInfrastructure(builder.Build(), directory);
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }
}
