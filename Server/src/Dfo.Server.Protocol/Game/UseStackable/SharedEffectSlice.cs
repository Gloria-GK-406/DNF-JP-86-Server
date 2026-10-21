using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MarriageProposal;
namespace Dfo.Server.Protocol.Game.UseStackable;
public sealed record SharedEffectRequest(GameMessageMetadata Metadata, int ItemId, ushort Slot) : IEffectSettlementRequest
{ public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory; public ClientMessageType MessageType => ClientMessageType.GameSharedEffect; }
internal sealed class SharedEffectDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata m, ReadOnlySpan<byte> b)
    {
        if (b.Length != 6) throw new InvalidDataException("Shared effect requires six body bytes.");
        return new SharedEffectRequest(m, BinaryPrimitives.ReadInt32LittleEndian(b), BinaryPrimitives.ReadUInt16LittleEndian(b[4..]));
    }
}
internal sealed record SharedEffectAck(bool Success, ushort Slot) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameSharedEffect; }
internal sealed class SharedEffectAckEncoder : GameCmdMessageEncoder<SharedEffectAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SharedEffectAck m)
    {
        if (!m.Success) return OnePayload(0x00FB, new byte[] { 0, 19 });
        var b = new byte[4]; b[0] = 1; BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(1), m.Slot);
        return OnePayload(0x00FB, b);
    }
}
internal sealed record SharedEffectNotification(ushort Actor, int ItemId, byte Selector) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameSharedEffectNotification; }
internal sealed class SharedEffectNotificationEncoder : GameNotiMessageEncoder<SharedEffectNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SharedEffectNotification m)
    {
        var b = new byte[7]; BinaryPrimitives.WriteUInt16LittleEndian(b, m.Actor);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(2), m.ItemId); b[6] = m.Selector;
        return OnePayload(0x00DB, b);
    }
}
public sealed record SharedEffectCommand(SelectedSessionIdentity Actor, BusinessSlot Slot, int ItemId) : ICommand<SharedEffectUseResult>, IAccountCommand, IRequestSettlementCommand
{ public AccountId AccountId => Actor.AccountId; public SelectedSessionIdentity? Session => Actor; }
public sealed class SharedEffectCommandHandler(ISharedEffectDomainService effects) : ICommandHandler<SharedEffectCommand, SharedEffectUseResult>
{
    public ValueTask<SharedEffectUseResult> HandleAsync(CommandExecutionContext c, SharedEffectCommand q, CancellationToken ct) => effects.UseAsync(q.Actor, q.Slot, q.ItemId, ct);
}
internal sealed class SharedEffectHandler(ISelectedSessionDomainService sessions, IInventorySlotMappingRuleService mappings,
    ITownAreaDomainService areas, ICommandBus commands, IGameMessageSender sender, SelectedSessionTransitionGate gate) : ClientMessageHandler<SharedEffectRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext c, SharedEffectRequest m, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(c.Connection.ConnectionId);
        await WeddingProposalExecution.RunAsync(gate, actor, actor, async () =>
        {
            BusinessSlot slot;
            try { slot = mappings.ToBusinessSlot(new GameSlot(InventoryContainerType.Main, m.Slot)); }
            catch (ArgumentOutOfRangeException) { await sender.SendExplicitAsync(c.Connection.ConnectionId, new([new SharedEffectAck(false, m.Slot)], []), ct).ConfigureAwait(false); return; }
            var result = actor is null ? new SharedEffectUseResult(false) : await commands.SendAsync(new SharedEffectCommand(actor, slot, m.ItemId), ct).ConfigureAwait(false);
            var messages = new List<IGameServerMessage> { new SharedEffectAck(result.Succeeded, m.Slot) };
            var targets = new List<GameExplicitMessageTarget>();
            if (result.Succeeded && actor is not null)
            {
                var effect = new SharedEffectNotification(actor.WireUserId, m.ItemId, result.Selector);
                // ACK performs the native decrement; authoritative absolute state follows it.
                if (result.State is { } state)
                    messages.Add(state.Item is { } item
                        ? GameInventoryDelivery.Notification(new(InventoryContainerType.Main, [new(item.ItemUid, m.Slot, item.ToCore(), item.AvatarDetail)], state.PreparedAt))
                        : GameInventoryDelivery.ClearedSlotNotification(InventoryContainerType.Main, m.Slot, state.PreparedAt));
                messages.Add(effect); // The ACK alone never renders the caster's effect.
                if (areas.Find(actor.CharacterId) is { } self && actor.Owns(self) && areas.Find(self.Area) is { } scene)
                    foreach (var member in scene.VisibleTo(actor.CharacterId).Where(x => x.CharacterId != actor.CharacterId))
                        if (sessions.TryGetInWorld(member.CharacterId, out var peer) && peer.Owns(member))
                            targets.Add(new(peer.ConnectionId, [effect]));
            }
            await sender.SendExplicitAsync(c.Connection.ConnectionId, new(messages, targets), ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }
}
