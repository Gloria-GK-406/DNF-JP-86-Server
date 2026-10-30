using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Testing;
using Dfo.Server.Protocol.Tests.Support;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.InitialConnection;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Outbox;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>DR-12 (TASK-24): the initial connection notice through the real registration adapters and outbox.</summary>
public sealed class InitialConnectionDeliveryFailureTests
{
    private static readonly NetworkConnectionId Connection = new("Game:initial-xo");

    /// <summary>
    /// T14: the notice fails in transport. The connect CMD has succeeded, so the identity stays until the sender's
    /// close comes back as the connection's own disconnect, which removes it (on the wire: TASK-23 N9).
    /// </summary>
    [Fact]
    public async Task An_initial_notice_that_fails_in_transport_is_closed_by_the_sender_and_the_disconnect_removes_the_identity()
    {
        await using var world = new InitialWorld();
        world.Outbound.Transport.Fail(Connection);

        await world.Outbound.RunAsync(() => world.Observer.ConnectedAsync(world.Context, CancellationToken.None).AsTask());
        await world.Outbound.SettledAsync();

        Assert.Contains(world.Outbound.Messages(Connection), message => message is GameInitialConnectionNotice);
        Assert.True(await world.Outbound.ClosedAsync(Connection));
        Assert.DoesNotContain(world.Outbound.Recorder.Entries, entry => entry.IsClose);
        Assert.True(await world.Outbound.RunAsync(() => Task.FromResult(world.Online.TryGetByConnection(Connection, out _))));

        await world.Outbound.RunAsync(() => world.Observer.DisconnectedAsync(world.Context, CancellationToken.None).AsTask());
        Assert.False(await world.Outbound.RunAsync(() => Task.FromResult(world.Online.TryGetByConnection(Connection, out _))));
    }

    /// <summary>R11, the kept branch: the notice cannot be registered; the exact identity is removed and it is thrown.</summary>
    [Fact]
    public async Task An_initial_notice_that_cannot_be_registered_removes_the_identity_and_is_thrown()
    {
        await using var world = new InitialWorld();
        world.Outbound.Encoder.Fail<GameInitialConnectionNotice>(Connection, () => new IOException("initial notice failed"));

        await Assert.ThrowsAsync<IOException>(() => world.Outbound.RunFromAsync(
            Connection, () => world.Observer.ConnectedAsync(world.Context, CancellationToken.None).AsTask()));
        await world.Outbound.SettledAsync();

        Assert.Empty(world.Outbound.Messages(Connection));
        Assert.False(await world.Outbound.RunAsync(() => Task.FromResult(world.Online.TryGetByConnection(Connection, out _))));
        Assert.True(await world.Outbound.ClosedAsync(Connection));
    }

    private sealed class InitialWorld : IAsyncDisposable
    {
        public InitialWorld()
        {
            var state = new OnlineStateRegistry(TestRequired.TransactionScope());
            Online = new OnlineUserDomainService(state);
            Outbound = new XoDelivery();
            Outbound.Connect(Connection);
            Context = Outbound.Context(Connection);
            Observer = new OnlineUserConnectionObserver(Online,new SelectedSessionDomainService(state),new Dfo.Server.Application.Commands.CommandSelectedSessionCleanup(new NoCleanup()),                new GeneralChannel(),new Config(),new LifecycleTestOutbox(Outbound.Game),new NoCleanupDelivery(),TimeProvider.System,clientSettings: ProtocolAlgorithmFixture.Decorate(ProtocolAlgorithmFixture.Default<IClientSettingsDomainService>(),
                (method, args) =>
                {
                    if (method.Name != nameof(IClientSettingsDomainService.ForgetConnection)) return (false, null);
                    // No profile was activated by this connection-only fixture.
                    var target = args[0] is NetworkConnectionId id ? id
                        : Assert.IsType<Dfo.Server.Data.OnlineUsers.GameSessionIdentity>(args[0]).ConnectionId;
                    Assert.Equal(Connection, target);
                    return (true, null);
                }));
        }

        public XoDelivery Outbound { get; }

        public OnlineUserDomainService Online { get; }

        public NetworkConnectionContext Context { get; }

        public OnlineUserConnectionObserver Observer { get; }

        public ValueTask DisposeAsync() => Outbound.DisposeAsync();
    }

    private sealed class GeneralChannel : IChannelEntryDomainService
    {
        public ValueTask<Dfo.Server.Data.Channel.ChannelLoginEntry> ResolveLoginAsync(int listenerPort,CancellationToken ct) =>
            ValueTask.FromException<Dfo.Server.Data.Channel.ChannelLoginEntry>(new NotSupportedException("This channel-identity fixture does not supply login deployment facts."));

        public ValueTask<ChannelEntryIdentity> ResolveAsync(int listenerPort, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ChannelEntryIdentity(11, listenerPort, ChannelEntryKind.General));

        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel) => null;
    }

    private sealed class Config : IServerConfigService
    {
        public T GetConfig<T>(string key) => (T)(object)"192.0.2.20";
    }

    private sealed class NoCleanup : ISelectedSessionCleanupDomainService
    {
        public IReadOnlySet<SelectedSessionIdentity> AffectedSessions(SelectedSessionIdentity session) => new HashSet<SelectedSessionIdentity> { session };
        public ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(
            SelectedSessionIdentity session, bool includeOwner, Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder footprint, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoCleanupDelivery : ICleanupNotificationDelivery
    {
        public ValueTask DeliverAsync(SelectedSessionCleanupNotificationPlan plan, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
