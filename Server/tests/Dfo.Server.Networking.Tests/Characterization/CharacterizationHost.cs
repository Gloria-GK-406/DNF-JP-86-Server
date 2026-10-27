using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Dfo.Server.Application;
using Dfo.Server.GameServer;
using Dfo.Server.HttpApi;
using Dfo.Server.Infrastructure;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Networking.Tests.Characterization;

/// <summary>
/// A whole server in this process, composed the way <c>Dfo.Server.Host/Program.cs</c> composes it:
/// the three shared modules, the game component and the launcher HTTP component, over a private
/// SQLite file and the real Script.pvf.
/// </summary>
/// <remarks>
/// Characterization tests must keep passing while handlers, commands, gates and delivery are
/// rebuilt underneath them, so nothing here reaches into those. State is prepared on the wire.
/// Accounts and tickets come from the launcher HTTP endpoints because the game protocol cannot
/// register an account or issue a ticket. The only other permitted seams are a decorator on an
/// Abstractions <c>*DomainService</c> (see <see cref="CharacterizationOptions.Decorate"/>) and a
/// direct SQLite write that a test must justify where it makes it.
/// </remarks>
internal sealed class CharacterizationHost : IAsyncDisposable
{
    /// <summary>
    /// Every clock read through <see cref="TimeProvider"/> sees this instant, so the connection
    /// notice, entry timestamps, daily periods and fatigue are the same on every run.
    /// </summary>
    public static readonly DateTimeOffset Now = new(2026, 8, 13, 2, 30, 0, TimeSpan.Zero);

    private readonly IHost host;
    private readonly string directory;
    private readonly HttpClient http;

    private CharacterizationHost(IHost host, string directory, int gamePort, int httpPort)
    {
        this.host = host;
        this.directory = directory;
        GamePort = gamePort;
        http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{httpPort}") };
    }

    internal IServiceProvider Services => host.Services;

    public int GamePort { get; }

    public string DatabasePath => Path.Combine(directory, "characterization.db");

    public static async Task<CharacterizationHost> StartAsync(
        CharacterizationOptions? options,
        CancellationToken cancellationToken)
    {
        options ??= new CharacterizationOptions();
        var archive = new PvfContentOptions().ResolveArchivePath()
            ?? throw new InvalidOperationException("Characterization tests require Script.pvf.");
        var directory = Path.Combine(Path.GetTempPath(), "dfo-characterization-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "channel_info.etc");
        await File.WriteAllBytesAsync(script, [1], cancellationToken);
        var ports = AvailablePorts(3);
        var (channelPort, gamePort, httpPort) = (ports[0], ports[1], ports[2]);
        var settings = new Dictionary<string, string?>
        {
            ["Pvf:ArchivePath"] = archive,
            ["Database:Path"] = Path.Combine(directory, "characterization.db"),
            ["Networking:Enabled"] = "true",
            ["Networking:UdpDiscoveryEnabled"] = "false",
            ["Networking:ChannelPort"] = channelPort.ToString(),
            ["Networking:GamePorts:0"] = gamePort.ToString(),
            ["Channel:ScriptPath"] = script,
            ["Channel:PublicServerIp"] = "127.0.0.1",
            ["Channel:Channels:0:Id"] = "11",
            ["Channel:Channels:0:Name"] = "#ch.11",
            ["Channel:Channels:0:Host"] = "127.0.0.1",
            ["Channel:Channels:0:Port"] = gamePort.ToString(),
            ["Game:EndpointHost"] = "192.0.2.20",
            ["Game:DailyResetTime"] = "06:00:00",
            ["GameLogin:Environment"] = "1",
            ["GameLogin:ConnectionPort"] = "10128",
            ["GameLogin:EndpointPortA"] = "17200",
            ["GameLogin:EndpointPortB"] = "17201",
            ["Fatigue:BaseTotal"] = "156",
            ["LauncherAuth:Enabled"] = "true",
            ["LauncherAuth:ListenUrl"] = $"http://127.0.0.1:{httpPort}",
            ["LauncherAuth:PublicChannel:Host"] = "127.0.0.1",
            ["LauncherAuth:PublicChannel:Port"] = channelPort.ToString(),
            ["GmWeb:Enabled"] = "false",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        IHost? built = null;
        try
        {
            built = new HostBuilder()
                .UseContentRoot(directory)
                .ConfigureAppConfiguration(sources => sources.AddConfiguration(configuration))
                // DFO_CHARACTERIZATION_DEBUG=1 adds the sender's per-frame "S->C" debug lines.
                .ConfigureLogging(logging => logging
                    .AddSimpleConsole(console => console.SingleLine = true)
                    .SetMinimumLevel(LogLevel.Warning)
                    .AddFilter("Dfo.Server.Networking", Environment.GetEnvironmentVariable("DFO_CHARACTERIZATION_DEBUG") == "1"
                        ? LogLevel.Debug : LogLevel.Warning))
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
                    new InfrastructureModule().ConfigureServices(services, context.Configuration);
                    new PersistenceModule().ConfigureServices(services, context.Configuration);
                    new ApplicationModule().ConfigureServices(services, context.Configuration);
                })
                .ConfigureGameServer()
                .ConfigureHttpApi(configuration)
                .ConfigureServices(services =>
                {
                    DeterministicLayouts.Register(services);
                    options.Decorate?.Invoke(services);
                })
                .Build();
            await built.StartAsync(cancellationToken);
            var started = new CharacterizationHost(built, directory, gamePort, httpPort);
            await started.WaitForLauncherAsync(cancellationToken);
            return started;
        }
        catch
        {
            built?.Dispose();
            DeleteDirectory(directory);
            throw;
        }
    }

    /// <summary>Registers an account and redeems a fresh launcher ticket, as WireSmoke does.</summary>
    public async Task<(string MId, string Ticket)> RegisterAndIssueTicketAsync(
        string loginId,
        CancellationToken cancellationToken)
    {
        var credentials = new { loginId, password = "characterization password" };
        using (var registration = await http.PostAsJsonAsync("/api/launcher/v1/register", credentials, cancellationToken))
            Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        using var login = await http.PostAsJsonAsync("/api/launcher/v1/login", credentials, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var json = JsonDocument.Parse(await login.Content.ReadAsStreamAsync(cancellationToken));
        return (json.RootElement.GetProperty("mId").GetString()!, json.RootElement.GetProperty("ticket").GetString()!);
    }

    public async Task<WireClient> ConnectAsync(string label, CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(IPAddress.Loopback, GamePort, cancellationToken);
        return new WireClient(label, client);
    }

    /// <summary>
    /// Logs a fresh connection in, creates one character on it and selects that character, which
    /// is the only way a player reaches the world. Returns the connection standing in town.
    /// </summary>
    public async Task<WireClient> EnterWorldAsync(
        string label,
        string loginId,
        string characterName,
        CancellationToken cancellationToken,
        Func<Task>? beforeSelect = null)
    {
        var (mId, ticket) = await RegisterAndIssueTicketAsync(loginId, cancellationToken);
        var client = await ConnectAsync(label, cancellationToken);
        await client.LoginAsync(mId, ticket, cancellationToken);
        await client.CreateCharacterAsync(characterName, cancellationToken);
        if (beforeSelect is not null) await beforeSelect();
        await client.SelectCharacterAsync(cancellationToken);
        return client;
    }

    public async Task ExecuteSqlAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        http.Dispose();
        try
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await host.StopAsync(stop.Token);
        }
        finally
        {
            host.Dispose();
            SqliteConnection.ClearAllPools();
            DeleteDirectory(directory);
        }
    }

    private async Task WaitForLauncherAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                using var response = await http.GetAsync("/api/launcher/v1/bootstrap", cancellationToken);
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException)
            {
                // Kestrel is still binding.
            }

            await Task.Delay(25, cancellationToken);
        }
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A SQLite handle may outlive the host by a moment; the directory is under the temp root.
        }
    }

    private static int[] AvailablePorts(int count)
    {
        var ports = new List<int>(count);
        while (ports.Count < count)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            if (!ports.Contains(port)) ports.Add(port);
        }

        return [.. ports];
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}

internal sealed class CharacterizationOptions
{
    /// <summary>
    /// Runs after every module has registered. Only decorators of Abstractions
    /// <c>*DomainService</c> interfaces belong here.
    /// </summary>
    public Action<IServiceCollection>? Decorate { get; init; }
}

internal static class DomainServiceDecoration
{
    /// <summary>Replaces the last registration of <typeparamref name="TService"/> with a decorator.</summary>
    public static void Decorate<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService, TService> decorate)
        where TService : class
    {
        if (!typeof(TService).Name.EndsWith("DomainService", StringComparison.Ordinal)
            || typeof(TService).Assembly.GetName().Name != "Dfo.Server.Abstractions")
        {
            throw new InvalidOperationException(
                $"Characterization tests may only decorate Abstractions *DomainService interfaces; {typeof(TService)} is not one.");
        }

        var descriptor = services.Last(service => service.ServiceType == typeof(TService));
        services.Remove(descriptor);
        services.AddSingleton<TService>(provider => decorate(
            provider,
            descriptor.ImplementationInstance as TService
                ?? (descriptor.ImplementationFactory is not null
                    ? (TService)descriptor.ImplementationFactory(provider)
                    : (TService)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!))));
    }
}
