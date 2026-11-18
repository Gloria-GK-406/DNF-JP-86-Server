using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.AvatarCompound;

public sealed record AvatarCompoundPlusRequest(GameMessageMetadata Metadata, AvatarCompoundPlusInput? Input)
    : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameAvatarCompoundPlus;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.AvatarCompoundPlus;
}

internal sealed class AvatarCompoundPlusDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        const int count = AvatarCompoundPlusInput.IngredientCount;
        if (body.Length != 145 || body[16] != 0 || BinaryPrimitives.ReadUInt16LittleEndian(body[23..]) != count)
            return new AvatarCompoundPlusRequest(metadata, null);

        var material = BinaryPrimitives.ReadInt16LittleEndian(body[14..]);
        var target = BinaryPrimitives.ReadInt32LittleEndian(body[17..]);
        var ability = BinaryPrimitives.ReadUInt16LittleEndian(body[21..]);
        if (material < 0 || target <= 0)
            return new AvatarCompoundPlusRequest(metadata, null);

        try
        {
            var ingredients = ImmutableArray.CreateBuilder<AvatarCompoundPlusIngredient>(count);
            for (var index = 0; index < count; index++)
            {
                var row = body[(25 + 6 * index)..];
                var slot = BinaryPrimitives.ReadInt16LittleEndian(row);
                var itemId = BinaryPrimitives.ReadInt32LittleEndian(row[2..]);
                if (slot < 0 || itemId <= 0)
                    return new AvatarCompoundPlusRequest(metadata, null);
                ingredients.Add(new(A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Avatar, slot)), itemId));
            }
            var converter = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.Main, material));
            return new AvatarCompoundPlusRequest(metadata, new(converter, ingredients.MoveToImmutable(), target, ability));
        }
        catch (InventorySlotMappingException)
        {
            return new AvatarCompoundPlusRequest(metadata, null);
        }
    }
}

public sealed record AvatarCompoundPlusResponse(AvatarCompoundReceipt? Receipt) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAvatarCompoundPlus;
}

internal sealed class AvatarCompoundPlusEncoder : GameCmdMessageEncoder<AvatarCompoundPlusResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AvatarCompoundPlusResponse message)
    {
        if (message.Receipt is not { } receipt)
            return OnePayload(message, GameCmdEnvelope.Refuse(0x16));

        var output = receipt.Outputs.Single();
        var converter = A21InventoryLayout.ToGameSlot(receipt.Deductions[^1].Slot);
        var body = new byte[55];
        body[0] = 1;
        // A21 reads the whole block and skips its first four bytes. Retain the reference
        // server's leading one and use the actual converter slot in those ignored bytes.
        body[1] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(3), checked((short)converter.Index));
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(5), checked((short)A21InventoryLayout.ToGameSlot(output.Slot).Index));
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7), output.Item.ItemId);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(11), output.Item.Attribute);
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(13), 1);
        for (var index = 0; index < AvatarCompoundPlusInput.IngredientCount; index++)
        {
            var ingredient = A21InventoryLayout.ToGameSlot(receipt.Deductions[index].Slot);
            BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(15 + index * 2), checked((short)ingredient.Index));
        }
        return OnePayload(message, body);
    }
}

public sealed class AvatarCompoundPlusHandler(ICommandBus commands) : ClientMessageHandler<AvatarCompoundPlusRequest>,
    IProtocolCommandMessageHandler<GameAvatarCompoundPlusCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, AvatarCompoundPlusRequest request, CancellationToken ct)
        => await commands.SendAsync(new GameAvatarCompoundPlusCommand(context, request), ct).ConfigureAwait(false);
}

public sealed record AvatarCompoundPlusCompleted(AvatarCompoundReceipt Receipt) : INotification<AvatarCompoundPlusCompleted>;

public sealed class AvatarCompoundPlusCompletedHandler(ISelectedSessionDomainService sessions) : INotificationHandler<AvatarCompoundPlusCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,
        AvatarCompoundPlusCompleted notification, CancellationToken ct)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || !sessions.IsInWorld(receipt.Session))
            return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>([]);
        var converter = A21InventoryLayout.ToGameSlot(receipt.Deductions[^1].Slot);
        var remaining = receipt.MaterialRemaining;
        var update = remaining is null
            ? GameInventoryDelivery.ClearedSlotNotification(converter.Space, converter.Index, receipt.At)
            : GameInventoryDelivery.Notification(new(converter.Space,
                [new InventoryItemState(null, converter.Index, remaining.ToCore())], receipt.At));
        var output = receipt.Outputs.Single();
        var destination = A21InventoryLayout.ToGameSlot(output.Slot);
        var resultSlot = GameInventoryDelivery.Notification(new(destination.Space,
            [new InventoryItemState(output.Item.ItemUid, destination.Index, output.Item.ToCore(), output.Item.AvatarDetail)], receipt.At));
        return ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(
            [new AvatarCompoundPlusResponse(receipt), update, resultSlot]);
    }
}
