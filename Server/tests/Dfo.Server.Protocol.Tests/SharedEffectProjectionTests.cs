using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.UseStackable;
using Dfo.Server.Testing;
namespace Dfo.Server.Protocol.Tests;
public sealed class SharedEffectProjectionTests
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Ack_precedes_self_effect_and_only_visible_scene_members_receive_broadcast(bool success)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state); ITownAreaDomainService areas = new SessionPresenceDomainService();
            SelectedSessionIdentity Add(int id, byte area, bool visible)
            {
                var s = SelectedSessionIdentity.Create(new AccountId(id), new CharacterId(id), new NetworkConnectionId("shared-" + id), 2, 10011);
                Dfo.Server.Testing.TestSessions.EnterWorldWithConnection(sessions, state, s); areas.Enter(new(s.CharacterId, s.TownAreaLease(), new(1, 2, area), new(2, area, 0, 0, 0), visible)); return s;
            }
            var actor = Add(1, 12, true); var peer = Add(2, 12, true); Add(3, 13, true); Add(4, 12, false);
            var sender = new Sender(); var commands = new Commands(success);
            var request = new SharedEffectRequest(GameMessageMetadata.Empty, 10002625, 65);
            Assert.Equal(ReplayProjectionNeeds.Inventory, ((IEffectSettlementRequest)request).MinimumReplayNeeds);
            await new SharedEffectHandler(sessions, new InventorySlotMappingRuleService(), areas, commands, sender)
                .HandleAsync(new NetworkDispatchContext(new(actor.ConnectionId, NetworkEndpointKind.Game, 10011)), request, default);
            var delivery = Assert.Single(sender.Items); Assert.Equal(success, Assert.IsType<SharedEffectAck>(delivery.RequiredDirectMessages[0]).Success);
            Assert.Equal(1, commands.Count);
            if (success)
            {
                var self = Assert.IsType<SharedEffectNotification>(delivery.RequiredDirectMessages[1]); Assert.Equal(actor.WireUserId, self.Actor);
                var target = Assert.Single(delivery.BestEffortTargets); Assert.Equal(peer.ConnectionId, target.ConnectionId);
            }
            else { Assert.Single(delivery.RequiredDirectMessages); Assert.Empty(delivery.BestEffortTargets); }
        });
    }
    private sealed class Commands(bool success) : ICommandBus
    {
        public int Count { get; private set; }
        public ValueTask<T> SendAsync<T>(ICommand<T> command, CancellationToken ct = default) where T : notnull
        { Assert.IsType<SharedEffectCommand>(command); Count++; return ValueTask.FromResult((T)(object)new SharedEffectUseResult(success)); }
    }
    private sealed class Sender : IGameMessageSender
    {
        public List<GameExplicitMessageDelivery> Items { get; } = [];
        public ValueTask SendExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken ct) { Items.Add(delivery); return ValueTask.CompletedTask; }
        public ValueTask SendCmdAsync(NetworkConnectionId id, IGameCmdMessage m, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask SendNotiAsync(NetworkConnectionId id, IGameNotiMessage m, CancellationToken ct) => throw new NotSupportedException();
    }
}
