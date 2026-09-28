using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Hosting;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application;
using Dfo.Server.GameServer;
using Dfo.Server.Host.Modules;
using Dfo.Server.HttpApi;
using Dfo.Server.Infrastructure;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Host.Tests;

public sealed class ComponentLifecycleTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ComponentsOpenOnlyTheirConfiguredPortsAndReleaseThemOnStop(bool http, bool game)
    {
        var directory = NewDirectory();
        var ports = AvailablePorts();
        using var config = Configuration(directory, ports, http, game);
        using var host = Builder(config).Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var stopped = false;
        try
        {
            Assert.False(host.Services.GetRequiredService<IGameServerReadiness>().IsReady);
            await host.StartAsync(timeout.Token);
            Assert.Equal(http, await CanConnect(ports[0]));
            Assert.Equal(game, await CanConnect(ports[1]));
            Assert.Equal(game, await CanConnect(ports[2]));
            Assert.Equal(game, host.Services.GetRequiredService<IGameServerReadiness>().IsReady);
            if (http)
            {
                using var client = new HttpClient();
                using var response = await client.GetAsync(
                    $"http://127.0.0.1:{ports[0]}/api/launcher/v1/bootstrap", timeout.Token);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
            await host.StopAsync(timeout.Token);
            stopped = true;
            Assert.False(host.Services.GetRequiredService<IGameServerReadiness>().IsReady);
            Assert.All(ports, port =>
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start(); // Bind proves release; a connection failure alone can be transient.
            });
        }
        finally
        {
            if (!stopped) await host.StopAsync(CancellationToken.None);
            host.Dispose();
            await DeleteDatabaseDirectory(directory);
        }
    }

    [Fact]
    public async Task ContentPreparationBlocksBothListenersAndFailureDoesNotExposeEitherEndpoint()
    {
        var directory = NewDirectory();
        var archive = Path.Combine(directory, "invalid.pvf");
        await File.WriteAllTextAsync(archive, "not a PVF");
        var ports = AvailablePorts();
        using var config = Configuration(directory, ports, true, true);
        using var preparation = new FailingPreparation();
        using var host = Builder(config).ConfigureServices(services =>
        {
            services.AddSingleton(new PvfContentOptions { ArchivePath = archive });
            services.AddSingleton<IIndependentDropRuleService>(preparation);
        }).Build();
        var start = Task.Run(() => host.StartAsync());
        try
        {
            await preparation.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            foreach (var port in ports) Assert.False(await CanConnect(port));
            preparation.Release.Set();
            var failure = await Assert.ThrowsAsync<InvalidDataException>(() => start);
            Assert.Equal("content unavailable", failure.Message);
            foreach (var port in ports) Assert.False(await CanConnect(port));
            Assert.False(host.Services.GetRequiredService<IGameServerReadiness>().IsReady);
        }
        finally
        {
            preparation.Release.Set();
            try { await start; } catch (InvalidDataException) { }
            await host.StopAsync();
            host.Dispose();
            await DeleteDatabaseDirectory(directory);
        }
    }

    private static IHostBuilder Builder(IConfiguration configuration) =>
        Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureAppConfiguration((_, sources) =>
            {
                sources.Sources.Clear();
                sources.AddConfiguration(configuration);
            })
            .AddServerModules(new InfrastructureModule(), new PersistenceModule(), new ApplicationModule())
            .ConfigureGameServer()
            .ConfigureHttpApi(configuration);

    private static ConfigurationManager Configuration(string directory, int[] ports, bool http, bool game)
    {
        var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Path"] = Path.Combine(directory, "test.db"),
            ["LauncherAuth:Enabled"] = http.ToString(),
            ["LauncherAuth:ListenUrl"] = $"http://127.0.0.1:{ports[0]}",
            ["LauncherAuth:PublicChannel:Host"] = "127.0.0.1",
            ["LauncherAuth:PublicChannel:Port"] = ports[1].ToString(),
            ["Networking:Enabled"] = game.ToString(),
            ["Networking:ChannelPort"] = ports[1].ToString(),
            ["Networking:GamePorts:0"] = ports[2].ToString(),
            ["Channel:ScriptPath"] = Path.Combine(AppContext.BaseDirectory, "Resources", "Channel", "channel_info.etc"),
            ["Channel:PublicServerIp"] = "127.0.0.1",
            ["Channel:Channels:0:Host"] = "127.0.0.1",
            ["Channel:Channels:0:Port"] = ports[2].ToString(),
            ["Game:EndpointHost"] = "127.0.0.1",
            ["GameLogin:ConnectionPort"] = "10128",
            ["GameLogin:EndpointPortA"] = "17200",
            ["GameLogin:EndpointPortB"] = "17201",
        });
        return config;
    }

    private static string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-components-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static async Task DeleteDatabaseDirectory(string directory)
    {
        // The host ran inside this test process; its SQLite pool outlives the DI container.
        // Clear only this fixture's pool, never other tests' or running servers' connections.
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "test.db"),
        }.ToString());
        SqliteConnection.ClearPool(connection);
        await TestDirectory.DeleteAsync(directory);
    }

    private static int[] AvailablePorts()
    {
        var listeners = Enumerable.Range(0, 3).Select(_ => new TcpListener(IPAddress.Loopback, 0)).ToArray();
        try
        {
            foreach (var listener in listeners) listener.Start();
            return listeners.Select(l => ((IPEndPoint)l.LocalEndpoint).Port).ToArray();
        }
        finally { foreach (var listener in listeners) listener.Stop(); }
    }

    private static async Task<bool> CanConnect(int port)
    {
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try { await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token); return true; }
        catch (Exception error) when (error is SocketException or OperationCanceledException) { return false; }
    }

    private sealed class FailingPreparation : IIndependentDropRuleService, IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public int Initialize()
        {
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Test did not release preparation");
            throw new InvalidDataException("content unavailable");
        }
        public ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(MonsterDropRequest request, CancellationToken cancellationToken, int growthBonusPercent = 0) =>
            throw new NotSupportedException();
        public void Dispose() => Release.Dispose();
    }
}
