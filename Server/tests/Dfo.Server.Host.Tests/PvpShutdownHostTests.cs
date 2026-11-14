using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.GameServer;
using Dfo.Server.Host.Modules;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Host.Tests;

/// <summary>
/// Spec RQ-042 / AC-042 on a real host: a normal stop cancels a running duel before the transport tears the
/// connections down, so the disconnects that teardown makes cost nobody a loss.
/// </summary>
/// <remarks>
/// The TCP services close their sessions in their own <c>StopAsync</c> and dispatch each close as a disconnect.
/// <see cref="ClosingTransport"/> stands in for them: a hosted service whose <c>StopAsync</c> dispatches the two
/// fighters' disconnects through the production lifecycle dispatcher, as TouchSocket's close callbacks do, so the
/// whole selected-session cleanup chain runs at the same point of the stop.
/// </remarks>
public sealed class PvpShutdownHostTests
{
    private const int DuelPort = 10068;

    [Fact]
    public async Task A_normal_stop_cancels_a_running_fight_before_the_connections_go_and_nobody_loses()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-pvp-shutdown-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var transport = new ClosingTransport();
        using var host = Builder(directory, transport).Build();
        try
        {
            await host.StartAsync();
            var services = host.Services;
            var sessions = services.GetRequiredService<ISelectedSessionDomainService>();
            var state = services.GetRequiredService<OnlineStateRegistry>();
            SelectedSessionIdentity[] fighters =
            [
                sessions.InWorld(state, new AccountId(901), new CharacterId(901), "pvp-shutdown-a", DuelPort),
                sessions.InWorld(state, new AccountId(902), new CharacterId(902), "pvp-shutdown-b", DuelPort),
            ];
            var rooms = services.GetRequiredService<IPvpRoomDomainService>();
            Task<T> Write<T>(Func<ValueTask<T>> call) => TestGameplay.RunAsync(services, () => TestUseCase.RunAsync(services, call));
            var created = Assert.IsType<PvpRoomOutcome.Changed>(await Write(() => rooms.CreateAsync(
                new PvpRoomCreation(fighters[0], 0, [0x41], null, 1, PvpBattleMode.Individual), default)));
            var room = created.After!.Id;
            Assert.IsType<PvpRoomOutcome.Changed>(await Write(() => rooms.EnterAsync(fighters[1], created.After.Number, null, default)));
            await Write(() => rooms.SetReadyAsync(fighters[1], true, default));
            await Write(() => rooms.SetReadyAsync(fighters[0], true, default));
            foreach (var fighter in fighters) await Write(() => rooms.ReportLoadedAsync(fighter, default));
            foreach (var fighter in fighters) await Write(() => rooms.ReportPeerLinkedAsync(fighter, default));
            Assert.Equal(PvpRoomPhase.Fighting, rooms.Find(room)!.Phase);
            transport.Arm(services, room, fighters);

            await host.StopAsync();

            // When the transport closed the connections, the battle had already been cancelled.
            Assert.Equal(PvpRoomPhase.Waiting, transport.PhaseAtClose);
            Assert.Equal(2, transport.Disconnected);
            Assert.Null(rooms.Find(room));
            Assert.Empty(rooms.PendingConclusions());
        }
        finally
        {
            host.Dispose();
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directory, "test.db"),
            }.ToString());
            SqliteConnection.ClearPool(connection);
            await TestDirectory.DeleteAsync(directory);
        }

        // Nothing was written for the cancelled battle: no result, so no loss.
        Assert.Equal(0, transport.MatchesWritten);
    }

    private static IHostBuilder Builder(string directory, ClosingTransport transport)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Path"] = Path.Combine(directory, "test.db"),
            ["ContentWarmup:Enabled"] = "false",
            ["LauncherAuth:Enabled"] = "false",
            ["Networking:Enabled"] = "false",
            ["Channel:ScriptPath"] = Path.Combine(AppContext.BaseDirectory, "Resources", "Channel", "channel_info.etc"),
            ["Channel:PublicServerIp"] = "127.0.0.1",
            ["Game:EndpointHost"] = "127.0.0.1",
        });
        return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureAppConfiguration((_, sources) =>
            {
                sources.Sources.Clear();
                sources.AddConfiguration(configuration);
            })
            .AddServerModules(new InfrastructureModule(), new PersistenceModule(), new ApplicationModule())
            .ConfigureGameServer()
            .ConfigureServices(services =>
            {
                // A ranked duel channel and duel content of the test's own, so the battle needs no archive.
                services.AddSingleton<IChannelEntryDomainService>(new DuelChannels());
                services.AddSingleton<IPvpMapDefinitionSource>(new Maps());
                services.AddSingleton<IPvpGradeDefinitionSource>(new Grades());
                services.AddSingleton<IHostedService>(transport);
                transport.DatabasePath = Path.Combine(directory, "test.db");
            });
    }

    /// <summary>Stands in for the TCP services: their stop closes every session and dispatches its disconnect.</summary>
    private sealed class ClosingTransport : IHostedService
    {
        private IServiceProvider? services;
        private PvpRoomId room;
        private SelectedSessionIdentity[] fighters = [];

        public string DatabasePath { get; set; } = "";
        public PvpRoomPhase? PhaseAtClose { get; private set; }
        public int Disconnected { get; private set; }
        public long MatchesWritten { get; private set; } = -1;

        public void Arm(IServiceProvider provider, PvpRoomId duel, SelectedSessionIdentity[] sessions)
        {
            services = provider;
            room = duel;
            fighters = sessions;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (services is null) return;
            PhaseAtClose = services.GetRequiredService<IPvpRoomDomainService>().Find(room)?.Phase;
            var lifecycle = services.GetRequiredService<INetworkConnectionLifecycleDispatcher>();
            foreach (var fighter in fighters)
            {
                await lifecycle.DispatchDisconnectedAsync(
                    new NetworkConnectionContext(fighter.ConnectionId, NetworkEndpointKind.Game, DuelPort), cancellationToken);
                Disconnected++;
            }
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString());
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pvp_matches";
            MatchesWritten = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
    }

    private sealed class DuelChannels : IChannelEntryDomainService
    {
        public ValueTask<ChannelEntryIdentity> ResolveAsync(int listenerPort, CancellationToken cancellationToken) =>
            ValueTask.FromResult(listenerPort == DuelPort
                ? new ChannelEntryIdentity(70, DuelPort, ChannelEntryKind.Pvp, 24)
                : new ChannelEntryIdentity(11, listenerPort, ChannelEntryKind.General));

        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel) => null;

        public ValueTask<ChannelLoginEntry> ResolveLoginAsync(int listenerPort, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Maps : IPvpMapDefinitionSource
    {
        public PvpMapCatalog Load() => new([0, 101, 102]);
    }

    private sealed class Grades : IPvpGradeDefinitionSource
    {
        public PvpGradeCatalog Load() => new(
        [
            new(0, 0, 100, 50, 0),
            new(1, 100, 200, 50, -20),
        ], 1, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(2));
    }
}
