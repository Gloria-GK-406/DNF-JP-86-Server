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
            var state = new OnlineStateRegistry(new DomainTransactionScope());
            Online = new OnlineUserDomainService(state);
            Outbound = new XoDelivery();
            Outbound.Connect(Connection);
            Context = Outbound.Context(Connection);
            Observer = new OnlineUserConnectionObserver(Online, new SelectedSessionDomainService(state), new NoCleanup(),
                new GeneralChannel(), new Config(), Outbound.Game, new NoCleanupDelivery(), TimeProvider.System);
        }

        public XoDelivery Outbound { get; }

        public OnlineUserDomainService Online { get; }

        public NetworkConnectionContext Context { get; }

        public OnlineUserConnectionObserver Observer { get; }

        public ValueTask DisposeAsync() => Outbound.DisposeAsync();
    }

    private sealed class GeneralChannel : IChannelEntryPolicy
    {
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
        public ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(
            SelectedSessionIdentity session, bool includeOwner, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoCleanupDelivery : ICleanupNotificationDelivery
    {
        public ValueTask DeliverAsync(SelectedSessionCleanupNotificationPlan plan, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
