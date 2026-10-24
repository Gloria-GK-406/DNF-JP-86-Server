using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;
using Dfo.Server.Protocol;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.OnlineUsers;

/// <summary>
/// The disconnect of an old connection is a lifecycle CMD that runs after the CMD that replaced it,
/// never interleaved with it. Successors of the gate-race cases in OnlineUserServiceTests and
/// SelectCharacterLifecycleTests, which T-004c removes with the gates.
/// </summary>
public sealed class SerialDisconnectTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "dfo-serial-disconnect-" + Guid.NewGuid().ToString("N"));
    private readonly ConcurrentQueue<SelectedSessionIdentity> cleaned = new();

    [Fact]
    public async Task The_old_connections_disconnect_after_a_replacement_session_neither_cleans_nor_deselects_it()
    {
        await using var provider = Build();
        var account = new AccountId(4101);
        var old = Connection("Game:serial-old");
        var replacing = Connection("Game:serial-new");
        var (original, replacement) = TestGameplay.Run(provider, () =>
        {
            var first = Enter(provider, old, account, new CharacterId(5101));
            var second = Enter(provider, replacing, account, new CharacterId(5102));
            return (first, second);
        });

        await provider.GetRequiredService<INetworkConnectionLifecycleDispatcher>()
            .DispatchDisconnectedAsync(old, CancellationToken.None);

        Assert.Empty(cleaned);
        var sessions = provider.GetRequiredService<ISelectedSessionDomainService>();
        var online = provider.GetRequiredService<IOnlineUserDomainService>();
        TestGameplay.Run(provider, () =>
        {
            Assert.NotEqual(original, replacement);
            Assert.Equal(replacement, sessions.Find(account));
            Assert.True(sessions.IsInWorld(replacement));
            Assert.False(online.TryGetByConnection(old.ConnectionId, out _));
            Assert.True(online.TryGetByConnection(replacing.ConnectionId, out _));
        });
    }

    [Fact]
    public async Task The_old_connections_disconnect_after_a_newer_login_leaves_the_newer_identity_and_its_generation()
    {
        await using var provider = Build();
        var account = new AccountId(4102);
        var old = Connection("Game:serial-old-login");
        var newer = Connection("Game:serial-newer-login");
        var online = provider.GetRequiredService<IOnlineUserDomainService>();
        var state = provider.GetRequiredService<OnlineStateRegistry>();
        var authenticated = TestGameplay.Run(provider, () =>
        {
            state.Authenticate(online.RegisterConnection(old).OnlineUserId, account);
            var registered = online.RegisterConnection(newer);
            state.Authenticate(registered.OnlineUserId, account);
            return online.GetRequired(registered.OnlineUserId);
        });

        await provider.GetRequiredService<INetworkConnectionLifecycleDispatcher>()
            .DispatchDisconnectedAsync(old, CancellationToken.None);

        TestGameplay.Run(provider, () =>
        {
            Assert.False(online.TryGetByConnection(old.ConnectionId, out _));
            Assert.True(online.TryGetByConnection(newer.ConnectionId, out var current));
            Assert.Equal(authenticated, current);
            Assert.Equal(account, current!.AccountId);
        });
    }

    public void Dispose()
    {
        SqliteTestPools.ClearOwned(Path.Combine(directory, "test.db"));
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A SQLite handle may outlive the container by a moment; the directory is under the temp root.
        }
    }

    /// <summary>Selects a session for <paramref name="account"/> on a fresh authenticated connection and enters the world.</summary>
    private static SelectedSessionIdentity Enter(
        IServiceProvider provider, NetworkConnectionContext connection, AccountId account, CharacterId character)
    {
        var online = provider.GetRequiredService<IOnlineUserDomainService>();
        var sessions = provider.GetRequiredService<ISelectedSessionDomainService>();
        var registered = online.RegisterConnection(connection);
        provider.GetRequiredService<OnlineStateRegistry>().Authenticate(registered.OnlineUserId, account);
        var session = SelectedSessionIdentity.Create(account, character, connection.ConnectionId,
            online.GetRequired(registered.OnlineUserId).Generation, connection.ListenerPort);
        sessions.Select(session);
        sessions.EnterWorld(session);
        return session;
    }

    private ServiceProvider Build()
    {
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "channel_info.etc");
        File.WriteAllBytes(script, [1]);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Path"] = Path.Combine(directory, "test.db"),
            ["Channel:ScriptPath"] = script,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        new InfrastructureModule().ConfigureServices(services, configuration);
        new PersistenceModule().ConfigureServices(services, configuration);
        new ApplicationModule().ConfigureServices(services, configuration);
        new Dfo.Server.Networking.NetworkingModule().ConfigureServices(services, configuration);
        new ProtocolModule().ConfigureServices(services, configuration);
        var original = services.Last(x => x.ServiceType == typeof(ISelectedSessionCleanupDomainService));
        services.Remove(original);
        services.AddSingleton<ISelectedSessionCleanupDomainService>(sp => new RecordingCleanup(
            (ISelectedSessionCleanupDomainService)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), cleaned));
        return services.BuildServiceProvider();
    }

    private static NetworkConnectionContext Connection(string id) => new(new(id), NetworkEndpointKind.Game, 10011);

    private sealed class RecordingCleanup(ISelectedSessionCleanupDomainService inner, ConcurrentQueue<SelectedSessionIdentity> cleaned)
        : ISelectedSessionCleanupDomainService
    {
        public ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(
            SelectedSessionIdentity session, bool includeOwner, CancellationToken cancellationToken)
        {
            cleaned.Enqueue(session);
            return inner.CleanupAsync(session, includeOwner, cancellationToken);
        }
    }
}
