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

namespace Dfo.Server.Protocol.Game.ResetRandomOption;

public sealed record ResetRandomOptionRequest(GameMessageMetadata Metadata, ushort TargetSlot,
    ushort MaterialSlot, ushort OptionIndex, bool Valid = true) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameResetRandomOption;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
}

internal sealed class ResetRandomOptionDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        // A21 writes three u16s; the third field must not be discarded or truncated to a byte.
        var valid = body.Length == 6 || body.Length == 10 && GameBodyChecksum.IsValid(body[..6], body[6..]);
        return valid ? new ResetRandomOptionRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body),
            BinaryPrimitives.ReadUInt16LittleEndian(body[2..]), BinaryPrimitives.ReadUInt16LittleEndian(body[4..]))
            : new ResetRandomOptionRequest(metadata, 0, 0, 0, false);
    }
}

public sealed record ResetRandomOptionResponse(bool Success, byte Error = 0x13) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameResetRandomOption;
}

internal sealed class ResetRandomOptionEncoder : GameCmdMessageEncoder<ResetRandomOptionResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ResetRandomOptionResponse message)
        => OnePayload(message, message.Success ? [1] : GameCmdEnvelope.Refuse(message.Error));
}

public sealed class ResetRandomOptionHandler(ICommandBus commands) : ClientMessageHandler<ResetRandomOptionRequest>, IProtocolCommandMessageHandler<GameResetRandomOptionCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ResetRandomOptionRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameResetRandomOptionCommand(context, message), ct).ConfigureAwait(false);
}
public sealed record MagicSealGradeCompleted(EquipmentQualityReceipt Receipt) : INotification<MagicSealGradeCompleted>;

public sealed class MagicSealGradeCompletedHandler(ISelectedSessionDomainService sessions)
    : INotificationHandler<MagicSealGradeCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        MagicSealGradeCompleted notification, CancellationToken ct)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || !sessions.IsInWorld(receipt.Session))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var target = A21InventoryLayout.ToGameSlot(receipt.TargetSlot);
        var material = A21InventoryLayout.ToGameSlot(receipt.MaterialSlot);
        // Unlike 0051, this ACK carries no material count. Publish both committed slots first.
        var targetUpdate = GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(target.Space,
            [new InventoryItemState(receipt.Equipment.ItemUid, target.Index, receipt.Equipment.ToCore())], receipt.At));
        var materialUpdate = receipt.MaterialRemaining == 0
            ? GameInventoryDelivery.ClearedSlotNotification(material.Space, material.Index, receipt.At)
            : GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(material.Space,
                [new InventoryItemState(null, material.Index, (receipt.Material ?? throw new InvalidOperationException("Missing material receipt.")).ToCore())], receipt.At));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([targetUpdate, materialUpdate, new ResetRandomOptionResponse(true)]);
    }
}
