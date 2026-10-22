using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.CollectionBox;

public sealed record CollectionBoxRequest(GameMessageMetadata Metadata, CollectionBoxAction Action, byte BoxId,
    ushort SourceSlot = 0, int ItemId = 0) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameCollectionBox;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.CollectionBox
        | (Action == CollectionBoxAction.Select ? ReplayProjectionNeeds.None : ReplayProjectionNeeds.Inventory);
}
internal abstract class CollectionBoxDecoder(CollectionBoxAction action) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var length = action == CollectionBoxAction.Select ? 1 : action == CollectionBoxAction.Withdraw ? 5 : 8;
        if (body.Length != length || length == 8 && body[1] != 0)
            throw new InvalidDataException("Invalid collection box request length or reserved byte.");
        return new CollectionBoxRequest(metadata, action, body[0], length == 8 ? BinaryPrimitives.ReadUInt16LittleEndian(body[2..]) : (ushort)0,
            length == 1 ? 0 : BinaryPrimitives.ReadInt32LittleEndian(body[(length == 5 ? 1 : 4)..]));
    }
}
internal sealed class CollectionBoxSelectDecoder() : CollectionBoxDecoder(CollectionBoxAction.Select);
internal sealed class CollectionBoxPutDecoder() : CollectionBoxDecoder(CollectionBoxAction.Put);
internal sealed class CollectionBoxWithdrawDecoder() : CollectionBoxDecoder(CollectionBoxAction.Withdraw);
internal sealed class CollectionBoxExtendDecoder() : CollectionBoxDecoder(CollectionBoxAction.Extend);

public sealed record CollectionBoxCommand(SelectedSessionIdentity Actor, CollectionBoxAction Action, byte BoxId,
    BusinessSlot? Source, int ItemId) : ICommand<CollectionBoxMutation>, IAccountCommand, IRequestSettlementCommand
{
    public AccountId AccountId => Actor.AccountId;
    public SelectedSessionIdentity? Session => Actor;
}
public sealed class CollectionBoxCommandHandler(ICollectionBoxDomainService boxes, IRequestReplayProjectionDomainService replay)
    : ICommandHandler<CollectionBoxCommand, CollectionBoxMutation>
{
    public async ValueTask<CollectionBoxMutation> HandleAsync(CommandExecutionContext context, CollectionBoxCommand command, CancellationToken ct)
    {
        var result = await boxes.ExecuteAsync(command.Actor.AccountId, command.Actor.CharacterId, command.Action,
            command.BoxId, command.Source, command.ItemId, ct).ConfigureAwait(false);
        if (result.Inventory.Count > 0)
            replay.RecordInventorySlots(command.Actor, result.Inventory.Select(x => x.Slot).ToArray());
        return result;
    }
}
internal sealed record CollectionBoxAck(CollectionBoxAction Action, byte BoxId, byte? Error = null) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameCollectionBox; }
internal sealed class CollectionBoxAckEncoder : GameCmdMessageEncoder<CollectionBoxAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CollectionBoxAck m) =>
        OnePayload(checked((ushort)(0x03E5 + (int)m.Action)), m.Error is { } error ? GameCmdEnvelope.Refuse(error)
            : m.Action == CollectionBoxAction.Select ? GameCmdEnvelope.Accept([m.BoxId])
            : m.Action == CollectionBoxAction.Extend ? throw new InvalidDataException("Permanent boxes cannot extend.") : GameCmdEnvelope.Accept());
}
internal sealed record CollectionBoxContentsMessage(CollectionBoxContents Box, bool Selected)
    : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x03EB;
    internal override byte[] EncodeBody()
    {
        if (Box.BoxId == 0 || Box.ItemIds.IsDefault || Box.ItemIds.Length > 255 || Box.ItemIds.Any(x => x <= 0)
            || Box.ItemIds.Distinct().Count() != Box.ItemIds.Length) throw new InvalidDataException("Invalid collection projection.");
        var body = new byte[8 + Box.ItemIds.Length * 4];
        body[0] = Box.BoxId; body[1] = Selected ? (byte)1 : (byte)0;
        // remaining=0; permanent=1 takes precedence in the native A21 handler.
        body[6] = 1; body[7] = checked((byte)Box.ItemIds.Length);
        for (var i = 0; i < Box.ItemIds.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8 + i * 4), Box.ItemIds[i]);
        return body;
    }
    internal static IReadOnlyList<CollectionBoxContentsMessage> Full(CollectionBoxSnapshot snapshot)
    {
        if (snapshot.Boxes.IsDefault || snapshot.Boxes.Select(x => x.BoxId).Distinct().Count() != snapshot.Boxes.Length
            || snapshot.Boxes.Length > 0 && snapshot.Boxes.All(x => x.BoxId != snapshot.SelectedBoxId))
            throw new InvalidDataException("Invalid selected collection box.");
        var messages = snapshot.Boxes.OrderBy(x => x.BoxId).Select(x => new CollectionBoxContentsMessage(x, false)).ToList();
        if (snapshot.Boxes.SingleOrDefault(x => x.BoxId == snapshot.SelectedBoxId) is { } selected)
            messages.Add(new(selected, true));
        return messages;
    }
}
internal sealed class CollectionBoxContentsEncoder : GameNotiMessageEncoder<CollectionBoxContentsMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CollectionBoxContentsMessage m) => OnePayload(0x03EB, m.EncodeBody());
}
internal sealed class CollectionBoxHandler(ISelectedSessionDomainService sessions, ICommandBus commands, IGameMessageSender sender,
    SelectedSessionSafety safety, TimeProvider clock) : ClientMessageHandler<CollectionBoxRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, CollectionBoxRequest message, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(context.Connection.ConnectionId);
        if (actor is null) return;
        var recipient = safety.Capture(actor);
        safety.DemandCurrent(recipient);
        CollectionBoxMutation? result = null; byte? error = null;
        try
        {
            BusinessSlot? source = message.Action == CollectionBoxAction.Put
                ? new InventorySlotMappingRuleService().ToBusinessSlot(new(InventoryContainerType.Main, message.SourceSlot)) : null;
            result = await commands.SendAsync(new CollectionBoxCommand(actor, message.Action, message.BoxId, source, message.ItemId), ct).ConfigureAwait(false);
        }
        catch (CollectionBoxRejectedException e) { error = ErrorCode(message.Action, e.Reason); }
        catch (ContainerAdditionException e) when (e.Reason == ContainerAdditionFailure.NoRoom) { error = 0x04; }
        catch (ArgumentOutOfRangeException) { error = 0x15; }
        catch (InventorySlotMappingException) { error = 0x15; }
        safety.DemandCurrent(recipient);
        if (result is not null)
        {
            var mapping = new InventorySlotMappingRuleService();
            var frames = new List<IGameServerMessage>();
            foreach (var change in result.Inventory)
            {
                var physical = mapping.ToGameSlot(change.Slot);
                frames.Add(change.Item is { } item ? GameInventoryDelivery.Notification(new(physical.Space,
                    [new(item.ItemUid, physical.Index, item.ToCore(), item.AvatarDetail)], clock.GetUtcNow()))
                    : GameInventoryDelivery.ClearedSlotNotification(physical.Space, physical.Index, clock.GetUtcNow()));
            }
            frames.AddRange(CollectionBoxContentsMessage.Full(result.State));
            frames.Add(new CollectionBoxAck(message.Action, message.BoxId));
            await sender.SendBatchAsync(context.Connection, frames, ct).ConfigureAwait(false);
            return;
        }
        await sender.SendBatchAsync(context.Connection, [new CollectionBoxAck(message.Action, message.BoxId, error ?? 0x15)], ct).ConfigureAwait(false);
    }
    // Wire reasons belong here: their native UI meanings still await real-client verification.
    private static byte ErrorCode(CollectionBoxAction action, CollectionBoxRejection reason) => action switch
    {
        CollectionBoxAction.Select or CollectionBoxAction.Extend => 0x5F,
        CollectionBoxAction.Withdraw => reason == CollectionBoxRejection.MissingItem ? (byte)0x01 : (byte)0x15,
        _ => reason switch
        {
            CollectionBoxRejection.UnknownBox => 0x5F,
            CollectionBoxRejection.MissingItem => 0x12,
            CollectionBoxRejection.LockedOrTimed => 0x17,
            _ => 0x15,
        },
    };
}
