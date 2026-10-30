using Dfo.Server.Application.Dispatching;
using System.Buffers.Binary;
using System.Collections.Immutable;
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
namespace Dfo.Server.Protocol.Game.ItemSockets;

public sealed record WireEmblemPlacement(ushort MaterialSlot, int ItemId, byte SocketIndex);
public sealed record ItemSocketRequest(GameMessageMetadata Metadata, InventoryContainerType TargetList,
    ushort TargetSlot, int ExpectedItemId, ushort DrillSlot, ImmutableArray<WireEmblemPlacement> Emblems, bool Valid = true) : IEffectSettlementRequest
{
    public bool Open => Metadata.Type is 0x037A or 0x00CE;
    public bool Avatar => Metadata.Type is 0x00CE or 0x00C9;
    public ClientMessageType MessageType => Metadata.Type switch
    {
        0x037A => ClientMessageType.GameEquipmentSocketOpen, 0x0379 => ClientMessageType.GameEquipmentEmblem,
        0x00CE => ClientMessageType.GameAvatarSocketOpen, 0x00C9 => ClientMessageType.GameAvatarEmblem,
        _ => throw new InvalidOperationException("Unsupported socket opcode."),
    };
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
}
internal sealed class ItemSocketDecoder(ILogger<ItemSocketDecoder> logger) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var open = metadata.Type is 0x037A or 0x00CE;
        var avatar = metadata.Type is 0x00CE or 0x00C9;
        var prefix = avatar && !open ? 1 : 0;
        var minimum = open ? 8 : 7 + prefix;
        var expected = body.Length < minimum ? -1 : open ? 8 : minimum + body[6 + prefix] * 7;
        var valid = expected >= 0 && (body.Length == expected
            || body.Length == expected + 4 && GameBodyChecksum.IsValid(body[..expected], body[expected..]));
        if (valid && !open) valid = body[6 + prefix] is > 0 && body[6 + prefix] <= (avatar ? 3 : 2);
        if (!valid)
        {
            logger.LogWarning("Socket request invalid type={Type:X4} length={Length} body={Body}", metadata.Type, body.Length,
                Convert.ToHexString(body[..Math.Min(48, body.Length)]));
            return new ItemSocketRequest(metadata, InventoryContainerType.Main, 0, 0, 0, [], false);
        }
        var list = avatar ? open ? InventoryContainerType.Avatar : (InventoryContainerType)body[0] : InventoryContainerType.Main;
        var slot = BinaryPrimitives.ReadUInt16LittleEndian(body[prefix..]);
        var id = BinaryPrimitives.ReadInt32LittleEndian(body[(prefix + 2)..]);
        var entries = ImmutableArray.CreateBuilder<WireEmblemPlacement>();
        if (!open)
            for (var at = minimum; at < expected; at += 7)
                entries.Add(new(BinaryPrimitives.ReadUInt16LittleEndian(body[at..]), BinaryPrimitives.ReadInt32LittleEndian(body[(at + 2)..]), body[at + 6]));
        return new ItemSocketRequest(metadata, list, slot, id, open ? BinaryPrimitives.ReadUInt16LittleEndian(body[6..]) : (ushort)0,
            entries.ToImmutable());
    }
}
public sealed record WireSocketRemainder(ushort Slot, int Remaining);
public sealed record ItemSocketResponse(ushort Type, bool Success, ushort TargetSlot = 0, ushort DrillSlot = ushort.MaxValue,
    ImmutableArray<WireSocketRemainder> Materials = default, byte Error = 0x04) : IGameCmdMessage
{
    public ServerMessageType MessageType => Type switch
    {
        0x037A => ServerMessageType.GameEquipmentSocketOpen, 0x0379 => ServerMessageType.GameEquipmentEmblem,
        0x00CE => ServerMessageType.GameAvatarSocketOpen, 0x00C9 => ServerMessageType.GameAvatarEmblem,
        _ => throw new InvalidOperationException("Unsupported socket opcode."),
    };
}
internal sealed class ItemSocketEncoder : GameCmdMessageEncoder<ItemSocketResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ItemSocketResponse message)
    {
        if (!message.Success) return OnePayload(message.Type, GameCmdEnvelope.Refuse(message.Error));
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((byte)1);
        if (message.Type is 0x037A or 0x00CE) { writer.Write(message.TargetSlot); writer.Write(message.DrillSlot); }
        else
        {
            writer.Write(checked((byte)(message.Materials.IsDefault ? 0 : message.Materials.Length)));
            if (!message.Materials.IsDefault)
                foreach (var material in message.Materials) { writer.Write(material.Slot); writer.Write(material.Remaining); }
        }
        return OnePayload(message.Type, stream.ToArray());
    }
}
public sealed class ItemSocketHandler(ICommandBus commands) : ClientMessageHandler<ItemSocketRequest>, IProtocolCommandMessageHandler<GameItemSocketCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, ItemSocketRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameItemSocketCommand(context, message), ct).ConfigureAwait(false);
}
public sealed record ItemSocketCompleted(ushort Type, ItemSocketReceipt Receipt) : INotification<ItemSocketCompleted>;
public sealed class ItemSocketCompletedHandler(ISelectedSessionDomainService sessions,
    IBusinessInventorySlotDomainService inventory) : INotificationHandler<ItemSocketCompleted>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, ItemSocketCompleted notification, CancellationToken ct)
    {
        var receipt = notification.Receipt;
        if (recipient.CharacterId != receipt.Session.CharacterId || recipient.ConnectionId != receipt.Session.ConnectionId
            || !sessions.IsInWorld(receipt.Session)) return [];
        var messages = new List<IServerNetworkMessage>();
        var remains = ImmutableArray.CreateBuilder<WireSocketRemainder>();
        foreach (var material in receipt.Materials)
        {
            var state = await Read(material.Slot);
            remains.Add(new(checked((ushort)A21InventoryLayout.ToGameSlot(material.Slot).Index), state?.Count ?? 0));
        }
        // Open ACK decrements one drill locally. It must precede the authoritative material NOTI.
        messages.Add(new ItemSocketResponse(notification.Type, true, checked((ushort)A21InventoryLayout.ToGameSlot(receipt.Input.Target).Index),
            receipt.Input.Open && receipt.Materials.Length != 0 ? remains[0].Slot : ushort.MaxValue, remains.ToImmutable()));
        foreach (var material in receipt.Materials) Add(material.Slot, await Read(material.Slot));
        Add(receipt.Input.Target, await Read(receipt.Input.Target));
        return messages;
        async ValueTask<ItemSnapshot?> Read(BusinessSlot slot)
        {
            var bound = inventory.Find(receipt.Session.CharacterId, slot);
            return bound is null ? null : (await bound.Container.GetAsync(slot.Index, ct).ConfigureAwait(false))?.Snapshot;
        }
        void Add(BusinessSlot slot, ItemSnapshot? item)
        {
            var game = A21InventoryLayout.ToGameSlot(slot);
            messages.Add(item is null ? GameInventoryDelivery.ClearedSlotNotification(game.Space, game.Index, receipt.At)
                : GameInventoryDelivery.Notification(new CommittedInventoryUpdateState(game.Space,
                    [new InventoryItemState(item.ItemUid, game.Index, item.ToCore(), item.AvatarDetail)], receipt.At)));
        }
    }
}
