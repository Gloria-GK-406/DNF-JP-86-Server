using Dfo.Server.Application.Dispatching;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
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
public sealed record GameSharedEffectCommand(NetworkDispatchContext Dispatch, SharedEffectRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameSharedEffectCommandHandler(ISelectedSessionDomainService sessions, IInventorySlotMappingRuleService mappings,
    ITownAreaDomainService areas, ISharedEffectDomainService effects, ICommandWorkUnits units, IGameCommandOutbox outbox) : ICommandHandler<GameSharedEffectCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameSharedEffectCommand command, CancellationToken ct)
    {
        var c = command.Dispatch; var m = command.Input;
        var request = execution.Request ?? throw new InvalidOperationException("SharedEffect requires its explicit request.");
        var actor = sessions.FindByConnection(c.Connection.ConnectionId);
        BusinessSlot slot;
        try { slot = mappings.ToBusinessSlot(new GameSlot(InventoryContainerType.Main, m.Slot)); }
        catch (ArgumentOutOfRangeException) { await outbox.RegisterExplicitAsync(c.Connection.ConnectionId, new([new SharedEffectAck(false, m.Slot)], []), ct).ConfigureAwait(false); return false; }
        var result = new SharedEffectUseResult(false);
        if (actor is not null && request is not CommandRequestContext.ProtocolEffectRejected)
        {
            if (request is not CommandRequestContext.ProtocolEffectSelected selected || selected.Recipient.Session != actor)
                throw new InvalidOperationException("SharedEffect requires its originally selected actor.");
            result = await units.WriteAsync(execution, new("SharedEffect", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
                (unit, token) => effects.UseAsync(actor, slot, m.ItemId, unit.Footprint, token), ct).ConfigureAwait(false);
        }
        var delivery = SharedEffectProjection.Compose(actor, m, result, sessions, areas);
        await outbox.RegisterExplicitAsync(c.Connection.ConnectionId, delivery, ct).ConfigureAwait(false);
        return result.Succeeded;
    }
}

internal sealed class SharedEffectHandler(ICommandBus commands) : ClientMessageHandler<SharedEffectRequest>, IProtocolCommandMessageHandler<GameSharedEffectCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, SharedEffectRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSharedEffectCommand(context, message), ct).ConfigureAwait(false);
}

internal static class SharedEffectProjection
{
    internal static GameExplicitMessageDelivery Compose(SelectedSessionIdentity? actor, SharedEffectRequest m, SharedEffectUseResult result,
        ISelectedSessionDomainService sessions, ITownAreaDomainService areas)
    {
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
        return new(messages, targets);
    }
}
