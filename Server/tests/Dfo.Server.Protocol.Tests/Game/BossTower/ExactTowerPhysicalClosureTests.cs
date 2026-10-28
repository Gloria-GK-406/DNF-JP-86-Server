using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Parties;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Tests.Game.Party;
using Dfo.Server.Protocol.Tests.Outbox;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.BossTower;

public sealed partial class BossTowerProjectionConcurrencyTests
{
    [Fact]
    public async Task Typed_tower_party_leave_failure_does_not_close_a_same_ID_replacement_physical_connection()
    {
        var first = Session(1); var second = Session(2);
        var sessions = new Sessions(first, second);
        var firstPhysical = new NetworkConnectionContext(first.ConnectionId, NetworkEndpointKind.Game, 10011);
        var oldPeerPhysical = new NetworkConnectionContext(second.ConnectionId, NetworkEndpointKind.Game, 10011);
        var replacement = oldPeerPhysical with { };
        Assert.Equal(oldPeerPhysical, replacement); Assert.NotSame(oldPeerPhysical, replacement);
        var users = new PhysicalUsers((first, firstPhysical), (second, oldPeerPhysical));
        var parties = new PartyService(new PartyStatus(), new PartyModeRuleService(), TimeProvider.System, new NoPartyEndpoints(), new Dfo.Server.Application.World.OnlineCharacterLocationDomainService());
        TestGameplay.Run(() =>
        {
            Assert.True(parties.RequestInvite(PartyCharacter(first), PartyCharacter(second), new(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(PartyCharacter(second), PartyCharacter(first)).Succeeded);
        });
        var tower = new TowerDomain(PartyRun(Guid.NewGuid(), first, second));
        var towerWrites = 0;
        var requestedCloses = new List<NetworkConnectionContext>();
        XoDelivery? world = null;
        var domain = ProtocolAlgorithmFixture.Decorate<IBossTowerDomainService>(tower, (method, arguments) =>
        {
            if (method.Name != nameof(IBossTowerDomainService.LeaveAsync)) return (false, null);
            var result = tower.Leave(((BossTowerActionContext)arguments[0]!).Session);
            towerWrites++;
            // Replace only the physical connection while the command is inside its durable domain stage.
            // Its logical selected identity remains equal, so an accidental late ID lookup can close the new socket.
            users.Replace(second, replacement);
            var current = world ?? throw new InvalidOperationException("The outbound fixture is not ready.");
            ((XoConnections)current.Services.GetRequiredService<INetworkConnectionContexts>()).Remove(second.ConnectionId);
            current.Connect(replacement);
            return (true, ValueTask.FromResult(result));
        });
        await using var outbound = new XoDelivery(configure: services =>
        {
            services.AddSingleton<ISelectedSessionDomainService>(sessions);
            services.AddSingleton<IOnlineUserQueryDomainService>(users);
            services.AddCommandDispatching();
            services.AddProtocolCommandHandler<GameLeavePartyCommand, bool, GamePartyTransitionCommandHandler>(
                new ProtocolCommandMessageBinding(ClientMessageType.GameLeaveParty, typeof(LeavePartyRequest)));
            services.AddScoped<ICommandHandler<GameLeavePartyCommand, bool>>(provider =>
                ProtocolAlgorithmFixture.Create<GamePartyTransitionCommandHandler>(sessions, users, domain,
                    new PartyMutationProbe(parties, throwAfterLeave: true),
                    provider.GetRequiredService<ICommandWorkUnits>(), new ObservedPhysicalClosures(provider.GetRequiredService<ICommandConnectionClosures>(), requestedCloses),
                    (world ?? throw new InvalidOperationException("The outbound fixture is not ready.")).Game));
        });
        world = outbound;
        outbound.Connect(firstPhysical); outbound.Connect(oldPeerPhysical);
        var metadata = new GameMessageMetadata(1, GameProtocol.LeavePartyType, 0, 1) { InboundOrdinal = 1, SequenceIsFresh = true }.WithRawBody([]);
        var command = new GameLeavePartyCommand(new(firstPhysical), new(metadata));
        Assert.False(await outbound.Services.GetRequiredService<ICommandBus>().SendAsync(command));
        await outbound.SettledAsync();
        Assert.Equal(1, towerWrites);
        Assert.Null(tower.Find(first));
        Assert.NotNull(tower.Find(second));
        Assert.Contains(requestedCloses, connection => ReferenceEquals(connection, oldPeerPhysical));
        Assert.DoesNotContain(requestedCloses, connection => ReferenceEquals(connection, replacement));
        Assert.Contains(outbound.Recorder.Entries, entry => entry.IsClose && ReferenceEquals(entry.Connection, firstPhysical));
        Assert.DoesNotContain(outbound.Recorder.Entries, entry => entry.IsClose && ReferenceEquals(entry.Connection, replacement));
        Assert.Null(outbound.Transport.Resolve(firstPhysical));
        Assert.NotNull(outbound.Transport.Resolve(replacement));
        Assert.NotNull(outbound.Transport.Resolve(oldPeerPhysical));
    }

    private sealed class ObservedPhysicalClosures(ICommandConnectionClosures inner, List<NetworkConnectionContext> requested) : ICommandConnectionClosures
    {
        public ValueTask RequestCloseAsync(NetworkConnectionContext connection, CancellationToken ct)
        { requested.Add(connection); return inner.RequestCloseAsync(connection, ct); }
    }

    private sealed class PhysicalUsers(params (SelectedSessionIdentity Session, NetworkConnectionContext Physical)[] initial) : IOnlineUserQueryDomainService
    {
        private readonly Dictionary<NetworkConnectionId, OnlineUserSnapshot> values = initial.ToDictionary(
            pair => pair.Session.ConnectionId, pair => Create(pair.Session, pair.Physical));
        private static OnlineUserSnapshot Create(SelectedSessionIdentity session, NetworkConnectionContext physical) =>
            new(OnlineUserId.New(), physical, session.AccountId, session.CharacterId, OnlineUserState.CharacterActive, session.ConnectionGeneration);
        internal void Replace(SelectedSessionIdentity session, NetworkConnectionContext physical) => values[session.ConnectionId] = Create(session, physical);
        public OnlineUserSnapshot GetRequired(OnlineUserId id) => values.Values.Single(value => value.OnlineUserId == id);
        public bool TryGetByConnection(NetworkConnectionId id, out OnlineUserSnapshot? user) => values.TryGetValue(id, out user);
    }
}
