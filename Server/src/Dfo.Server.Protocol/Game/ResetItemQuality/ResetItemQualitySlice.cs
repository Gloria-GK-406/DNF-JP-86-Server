using Dfo.Server.Application.Dispatching;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.ResetItemQuality;

public sealed record ResetItemQualityRequest(GameMessageMetadata Metadata, ushort TargetSlot, int ExpectedItemId,
    ushort MaterialSlot, bool Valid = true) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameResetItemQuality;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
}
internal sealed class ResetItemQualityDecoder(ILogger<ResetItemQualityDecoder> logger) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        // Request layout is 70Reference/upstream; A21 builder is not yet recovered.
        var valid = body.Length == 8 || body.Length == 12
            && GameBodyChecksum.IsValid(body[..8], body[8..]);
        if (!valid) logger.LogWarning("RESET_ITEM_ATTR invalid body length={Length} hex={Body}", body.Length,
            Convert.ToHexString(body[..Math.Min(body.Length, 32)]));
        return valid ? new ResetItemQualityRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body),
            BinaryPrimitives.ReadInt32LittleEndian(body[2..]), BinaryPrimitives.ReadUInt16LittleEndian(body[6..]))
            : new ResetItemQualityRequest(metadata, 0, 0, 0, false);
    }
}
public sealed record ResetItemQualityResponse(bool Success, ushort MaterialSlot = 0, int Remaining = 0,
    byte Error = 0x13, ushort Operation = 2) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameResetItemQuality;
}
internal sealed class ResetItemQualityEncoder : GameCmdMessageEncoder<ResetItemQualityResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ResetItemQualityResponse message)
    {
        if (!message.Success) return OnePayload(0x0051, GameCmdEnvelope.Refuse(message.Error));
        var body = new byte[9]; body[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(1), message.MaterialSlot);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(3), message.Remaining);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(7), message.Operation);
        return OnePayload(0x0051, body);
    }
}
public sealed class ResetItemQualityHandler(ICommandBus commands) : ClientMessageHandler<ResetItemQualityRequest>, IProtocolCommandMessageHandler<GameResetItemQualityCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ResetItemQualityRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameResetItemQualityCommand(context, message), ct).ConfigureAwait(false);
}
public sealed record EquipmentQualityCompleted(EquipmentQualityReceipt Receipt) : INotification<EquipmentQualityCompleted>;
public sealed class EquipmentQualityCompletedHandler(ISelectedSessionDomainService sessions, IInventorySlotProjectionDomainService slots)
    : INotificationHandler<EquipmentQualityCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        EquipmentQualityCompleted notification, CancellationToken ct)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || !sessions.IsInWorld(receipt.Session))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var material = slots.Project(receipt.MaterialSlot);
        var target = slots.Project(receipt.TargetSlot);
        // CMD0051 updates/removes the material itself. Only the target needs NOTI000E.
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([
            new ResetItemQualityResponse(true, checked((ushort)material.Index), receipt.MaterialRemaining, Operation: receipt.Operation),
            GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(target.Space,
                [new InventoryItemState(receipt.Equipment.ItemUid, target.Index, receipt.Equipment.ToCore())], receipt.At))]);
    }
}
