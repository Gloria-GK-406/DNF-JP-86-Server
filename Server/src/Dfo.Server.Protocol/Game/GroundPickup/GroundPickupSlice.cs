using System.Buffers.Binary;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.GroundPickup;

/// <summary>
/// CMD 0x002B <c>GET_ITEM</c>: the player picking something up off a room's floor.
/// </summary>
/// <remarks>
/// Observed live on 2026-08-26: the tutorial's second room, its box already broken and the shoulder
/// lying on the ground, the player standing on it pressing the pick-up key. The client sent this
/// eighteen-byte frame, nothing answered it, and the item stayed where it was.
/// <para>
/// Only the first two bytes are read, which is what the reference server reads. They name the piece
/// by the number the room's own <c>START_MAP</c> gave it -- not by what it is, and not by where it
/// is lying. The rest of the body is the player's position and the item's, which nothing here acts
/// on: the server already knows where the piece is, having been the one to put it there.
/// </para>
/// </remarks>
public sealed record GetItemRequest(GameMessageMetadata Metadata, ushort GroundDropNumber)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameGetItem;
}

/// <summary>
/// The CMD acknowledgement under the same type: one byte for yes, two for a full bag.
/// </summary>
/// <remarks>
/// There is no third answer. The reference server sends this refusal when the inventory has no
/// room and sends nothing at all for everything else -- a piece that is not there, a run that has
/// ended, a character with no bag bound. Silence is the honest answer to those: each of them means
/// the two sides disagree about what is on the floor, and every sentence this type can draw tells
/// the player their bag is full.
/// </remarks>
public sealed record GetItemResponse(bool Accepted) : IGameCmdMessage
{
    /// <summary>「背包已满」 -- the only refusal this type carries.</summary>
    public const byte InventoryFull = 0x04;

    public ServerMessageType MessageType => ServerMessageType.GameGetItem;
}

/// <summary>
/// NOTI 0x0027 <c>GET_ITEM</c>: the piece taken off the floor, and the slot it went into.
/// </summary>
public sealed record GroundPickupNotification(
    ushort GroundDropNumber,
    ushort PickerActorId,
    ushort DestinationSlot) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGroundPickup;
}

/// <summary>
/// NOTI 0x0027 <c>GET_ITEM</c>, the form the client reads as gold rather than an item.
/// </summary>
/// <remarks>
/// The same type as an item pick-up and a different body. What tells them apart is the byte at
/// <c>+4</c>: an item writes one there, gold writes zero.
/// </remarks>
public sealed record GroundGoldPickupNotification(
    ushort GroundDropNumber,
    ushort PickerActorId,
    int Amount) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGroundPickup;
}

internal static class GroundPickupPayloadCodec
{
    /// <summary>The piece's number is all the reference server reads of the request.</summary>
    public const int MinimumBodyLength = 2;

    /// <summary>The notification's fixed length, as the reference builder writes it.</summary>
    public const int NotificationBodyLength = 18;

    /// <summary>The gold form's length, measured rather than transcribed.</summary>
    public const int GoldNotificationBodyLength = 117;

    /// <summary>
    /// Everything after the amount, which no capture has ever varied.
    /// </summary>
    /// <remarks>
    /// A hundred and seven bytes, <b>byte-for-byte identical in two independent captures</b> that
    /// differ in the amount, the piece and the picker -- 38 gold to user 3 in
    /// <c>packet_log.upstream-killexp-20260827.txt</c> and 131 to user 1 in
    /// <c>packet_log.reference-20260823.txt</c>. It repeats on a fourteen-byte period and this
    /// server has not read what it means, which is why it is carried as the constant it was
    /// observed to be rather than generated from a structure nobody has confirmed.
    /// </remarks>
    private static readonly byte[] GoldNotificationTail =
    [
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    /// <summary>
    /// The gold form: the piece, who took it, and how much.
    /// </summary>
    /// <remarks>
    /// Measured against two captures. The amount is a signed 32-bit field at <c>+6</c>: the client
    /// adds it to the figure it is already displaying rather than being told a total.
    /// <para>
    /// The figure it adds to is the character's carried gold, which this server now keeps. What
    /// it was told at entry is that same figure, read out of the character; so the number the
    /// player watches go up here is the number that will still be there at the next entry.
    /// </para>
    /// </remarks>
    public static byte[] EncodeGoldNotification(GroundGoldPickupNotification message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var body = new byte[GoldNotificationBodyLength];
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0), message.GroundDropNumber);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), message.PickerActorId);
        body[4] = 0x00; // money, where an item writes one
        body[5] = 0x01;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(6), message.Amount);
        GoldNotificationTail.CopyTo(body.AsSpan(10));
        return body;
    }

    public static byte[] EncodeResponse(GetItemResponse message) =>
        message.Accepted ? [0x01] : [0x00, GetItemResponse.InventoryFull];

    /// <remarks>
    /// Eighteen bytes, and the picker is named twice. The first pair says who took it and the
    /// second pair says who now holds it, which are the same actor for a pick-up and stop being
    /// the same the moment a party member is handed something. The two dwords between them are
    /// written as zero by the reference builder on every path this server has.
    /// </remarks>
    public static byte[] EncodeNotification(GroundPickupNotification message)
    {
        var body = new byte[NotificationBodyLength];
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0), message.GroundDropNumber);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), message.PickerActorId);
        body[4] = 0x01;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5), 0);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(9), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(13), message.PickerActorId);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(15), message.DestinationSlot);
        body[17] = 0x00;
        return body;
    }
}

internal sealed class GetItemDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < GroundPickupPayloadCodec.MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"GET_ITEM requires at least two body bytes; received '{body.Length}'.");
        }

        return new GetItemRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body));
    }
}

internal sealed class GetItemResponseEncoder : GameCmdMessageEncoder<GetItemResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GetItemResponse message) =>
        OnePayload(GameProtocol.GetItemType, GroundPickupPayloadCodec.EncodeResponse(message));
}

internal sealed class GroundGoldPickupEncoder : GameNotiMessageEncoder<GroundGoldPickupNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        GroundGoldPickupNotification message) =>
        OnePayload(
            GameProtocol.GroundPickupNotificationType,
            GroundPickupPayloadCodec.EncodeGoldNotification(message));
}

internal sealed class GroundPickupEncoder : GameNotiMessageEncoder<GroundPickupNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        GroundPickupNotification message) =>
        OnePayload(
            GameProtocol.GroundPickupNotificationType,
            GroundPickupPayloadCodec.EncodeNotification(message));
}

/// <summary>What a pick-up did, or why it did not.</summary>
/// <param name="Amount">
/// How much gold was taken, and what the character carries afterwards. Zero for anything that
/// went into a bag.
/// </param>
public sealed record GroundPickupResult(
    GroundPickupStatus Status,
    int ItemId,
    int SlotIndex,
    int Amount = 0,
    int Total = 0,
    DungeonPickupChange? Change = null,
    QuestProgressSnapshot? QuestProgress = null,
    DungeonPickupCommit? PendingCommit = null,
    InventoryRuntimeValueState? AccountMaterial = null,
    EpicPieceAddition? EpicPiece = null)
{
    public DungeonPickupChange? CommittedChange => this.PendingCommit?.Change ?? this.Change;

    public static GroundPickupResult Refused(GroundPickupStatus status, int itemId = 0) =>
        new(status, itemId, 0);
}

/// <summary>One character taking one piece off the floor of the room they are standing in.</summary>
internal sealed record GroundPickupInput(
    AccountId AccountId,
    CharacterId CharacterId,
    ushort GroundDropNumber,
    DungeonRunId ExpectedRunId = default,
    long? ExpectedRoomVersion = null)
;

/// <summary>
/// Moves a declared drop off the room's floor and into the character's bag.
/// </summary>
/// <remarks>
/// The current Cell owns the complete ground-item collection. The piece is claimed from that
/// collection before it is banked so a concurrent request cannot reserve the same stable number.
/// <para>
/// The floor reservation is committed only after the transactional persistent write succeeds. A
/// refusal or exception releases it. A claim is bound to the exact run, room version and Cell
/// revision, and room movement is refused while a claim is outstanding.
/// </para>
/// </remarks>
public sealed class GetItemHandler(ICommandBus commands)
    : ClientMessageHandler<GetItemRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameGetItemCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GetItemRequest message, CancellationToken token) =>
        _ = await commands.SendAsync(new GameGetItemCommand(context,message),token).ConfigureAwait(false);
}
