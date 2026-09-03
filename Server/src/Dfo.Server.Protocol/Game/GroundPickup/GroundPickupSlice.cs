using System.Buffers.Binary;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
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
    /// <b>The figure it adds to is the character's carried gold, which this server does not keep.</b>
    /// What it was told at entry is the account cargo's gold, in a different container of the
    /// item-list frame. So the number the player watches go up here is not backed by anything
    /// stored, and it is gone by the next entry. Giving it something to be backed by is a separate
    /// piece of work; nothing about this frame changes when that happens.
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

/// <summary>Why a piece was not picked up, in the server's own terms.</summary>
/// <remarks>
/// Named separately from the wire's single refusal because only one of these is a refusal the
/// player caused. The rest are ways for the two sides to disagree about what is on the floor, and
/// the log has to be able to say which one happened.
/// </remarks>
public enum GroundPickupStatus
{
    PickedUp = 0,

    /// <summary>The character is not in a dungeon run.</summary>
    NotInARun,

    /// <summary>This room has nothing lying under that number, or no longer has.</summary>
    NothingThere,

    /// <summary>Another request took it first.</summary>
    AlreadyTaken,

    /// <summary>The character is not bound to a main container.</summary>
    NoBoundContainer,

    /// <summary>The item's band of the inventory is full.</summary>
    InventoryFull,

    /// <summary>The archive declares nothing holdable under that identifier.</summary>
    UnknownItem,

    /// <summary>Gold, which goes to the account's cargo rather than into a bag.</summary>
    Banked,
}

/// <summary>What a pick-up did, or why it did not.</summary>
/// <param name="Amount">
/// How much gold was taken, and what the account's cargo holds afterwards. Zero for anything that
/// went into a bag.
/// </param>
public sealed record GroundPickupResult(
    GroundPickupStatus Status, int ItemId, int SlotIndex, int Amount = 0, int Total = 0)
{
    public static GroundPickupResult Refused(GroundPickupStatus status, int itemId = 0) =>
        new(status, itemId, 0);
}

/// <summary>One character taking one piece off the floor of the room they are standing in.</summary>
public sealed record PickUpGroundDropCommand(
    AccountId AccountId, CharacterId CharacterId, ushort GroundDropNumber)
    : ICommand<GroundPickupResult>;

/// <summary>
/// Moves a declared drop off the room's floor and into the character's bag.
/// </summary>
/// <remarks>
/// What is on the floor is read back out of the archive rather than remembered: a room declares its
/// breakable objects, and the run remembers only which of them have been taken. That is why the
/// piece is claimed before it is banked -- the claim is what makes a second request for the same
/// number fail, and without it a player could walk out of a room and back in and be handed the box
/// again.
/// <para>
/// The claim is taken before the insert and not given back if the insert fails. A failed insert is
/// a full bag, and the piece it would have gone into is one this server can no longer put back on
/// the floor without renumbering everything after it. Losing it is the smaller wrong: the refusal
/// tells the player their bag is full, which is true, and the room is already cleared.
/// </para>
/// </remarks>
public sealed class PickUpGroundDropCommandHandler(
    IDungeonRunDomainService runs,
    IGroundPickupDomainService floor,
    ICharacterContainerDomainService containers,
    IAccountCargoGoldService cargoGold)
    : ICommandHandler<PickUpGroundDropCommand, GroundPickupResult>
{
    public async ValueTask<GroundPickupResult> HandleAsync(
        CommandExecutionContext context,
        PickUpGroundDropCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var run = runs.Find(command.CharacterId);
        if (run is null)
            return GroundPickupResult.Refused(GroundPickupStatus.NotInARun);

        var found = await floor.FindOnFloorAsync(run, command.GroundDropNumber, cancellationToken)
            .ConfigureAwait(false);
        if (found is null)
            return GroundPickupResult.Refused(GroundPickupStatus.NothingThere);

        var piece = Announced(run, found);

        // Gold goes to the account's cargo and never touches a bag, so it is claimed and banked
        // before a container is even looked for -- a character with no bag bound can still pick up
        // coins. The reference server puts it on the character instead; this server has no carried
        // gold yet, which is why it lands here.
        if (piece.ItemId == RolledDrop.GoldItemId)
        {
            if (runs.TryTakeGroundDrop(command.CharacterId, command.GroundDropNumber) is null)
                return GroundPickupResult.Refused(GroundPickupStatus.AlreadyTaken);

            var total = await cargoGold.AddAsync(command.AccountId, piece.Count, cancellationToken)
                .ConfigureAwait(false);
            return new GroundPickupResult(
                GroundPickupStatus.Banked, RolledDrop.GoldItemId, 0, piece.Count, total);
        }

        var container = containers.FindContainer(command.CharacterId, InventoryContainerType.Main);
        if (container is null)
            return GroundPickupResult.Refused(GroundPickupStatus.NoBoundContainer, piece.ItemId);

        if (runs.TryTakeGroundDrop(command.CharacterId, command.GroundDropNumber) is null)
            return GroundPickupResult.Refused(GroundPickupStatus.AlreadyTaken, piece.ItemId);

        var outcome = await container.AddAsync(piece.ItemId, piece.Count, cancellationToken)
            .ConfigureAwait(false);
        switch (outcome)
        {
            case ItemPlacementOutcome.AddedToPile topped:
                return new GroundPickupResult(
                    GroundPickupStatus.PickedUp, piece.ItemId, topped.SlotIndex);

            case ItemPlacementOutcome.PutInEmptySlot opened:
                await CarryOverAsync(container, piece, opened.SlotIndex, cancellationToken)
                    .ConfigureAwait(false);
                return new GroundPickupResult(
                    GroundPickupStatus.PickedUp, piece.ItemId, opened.SlotIndex);

            case ItemPlacementOutcome.NoRoom:
                return GroundPickupResult.Refused(
                    GroundPickupStatus.InventoryFull, piece.ItemId);

            case ItemPlacementOutcome.UnknownItem:
                return GroundPickupResult.Refused(
                    GroundPickupStatus.UnknownItem, piece.ItemId);

            default:
                // Listed rather than defaulted: an outcome nobody read would become a piece that
                // left the floor and never arrived anywhere, with nothing said about it.
                throw new ArgumentOutOfRangeException(
                    nameof(command), outcome, "No reading was decided for this outcome.");
        }
    }

    /// <summary>
    /// One piece on a floor, from wherever it came from.
    /// </summary>
    /// <param name="Instance">
    /// The value that tells this piece of equipment from another. It is announced when the piece
    /// arrives on the floor and has to be the same value the bag ends up holding.
    /// </param>
    private sealed record FloorPickup(
        int ItemId, int Count, ushort Durability, uint Instance, bool IsEquipment);

    /// <summary>
    /// The piece with the value it was announced under.
    /// </summary>
    /// <remarks>
    /// A piece a kill dropped takes its value from the kill, not from the piece's number -- which
    /// is what the kill's own frame announced, and what makes every piece of one kill carry one
    /// value the way the captured frames do. Deriving it from the number here would put a
    /// different value in the bag than the one the player was shown on the floor.
    /// </remarks>
    private static FloorPickup Announced(DungeonRunState run, GroundPiece found) =>
        new(found.ItemId,
            found.Count,
            found.Durability,
            found.FromKillActorSequence is { } actorSequence
                ? GameDropIdentity.ForKill(run, actorSequence)
                : GameDropIdentity.ForDeclaredObject(run, found.ObjectIndex!.Value),
            found.IsEquipment);

    /// <summary>
    /// Gives the banked piece what it was lying on the floor carrying.
    /// </summary>
    /// <remarks>
    /// Only equipment, and only into a slot that was empty. What the bag opens a slot with is what
    /// a stackable needs -- a kind, an identifier and a count -- and a piece of equipment needs two
    /// things more, both of which were already announced when the room was described: how worn it
    /// is, and the value that tells one piece of it from another. A working A21 session's dropped
    /// shoulder and the one the player ends up wearing carry the same value, so this is the
    /// announcement being honoured rather than a second one being invented.
    ///
    /// A durability of zero would be a broken piece rather than an unspecified one, which is why
    /// this cannot be left to the bag's own defaults.
    /// </remarks>
    private async ValueTask CarryOverAsync(
        IItemContainer container,
        FloorPickup found,
        int slotIndex,
        CancellationToken cancellationToken)
    {
        if (!found.IsEquipment)
            return;

        var landed = await container.ReadSlotAsync(slotIndex, cancellationToken)
            .ConfigureAwait(false);
        if (landed is null)
            return;

        await container.PutAsync(
            slotIndex,
            landed.Core with
            {
                Value = checked((int)found.Instance),
                Durability = found.Durability,
            },
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Answers a pick-up with the acknowledgement and the notification, in that order.
/// </summary>
/// <remarks>
/// The order is the reference server's and it is not decoration: the client consumes the one-byte
/// acknowledgement and only then reads the notification that says where the piece went.
/// </remarks>
public sealed class GetItemHandler(
    ISelectedSessionDirectory selectedSessions,
    ICommandBus commandBus,
    IGameMessageSender sender,
    ILogger<GetItemHandler> logger) : ClientMessageHandler<GetItemRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        GetItemRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Get item {ConnectionId}: no published session, ignoring drop {Number}",
                context.Connection.ConnectionId,
                message.GroundDropNumber);
            return;
        }

        var result = await commandBus.SendAsync(
            new PickUpGroundDropCommand(
                session.AccountId, session.CharacterId, message.GroundDropNumber),
            cancellationToken).ConfigureAwait(false);

        if (result.Status is GroundPickupStatus.Banked)
        {
            logger.LogInformation(
                "Get item {ConnectionId} user={WireUserId}: drop {Number} is {Amount} gold, "
                    + "account cargo now holds {Total}",
                context.Connection.ConnectionId,
                session.WireUserId,
                message.GroundDropNumber,
                result.Amount,
                result.Total);

            await sender.SendExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [
                        new GetItemResponse(Accepted: true),
                        new GroundGoldPickupNotification(
                            message.GroundDropNumber, session.WireUserId, result.Amount),
                    ],
                    []),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (result.Status is GroundPickupStatus.PickedUp)
        {
            logger.LogInformation(
                "Get item {ConnectionId} user={WireUserId}: drop {Number} is item {ItemId}, "
                    + "into slot {Slot}",
                context.Connection.ConnectionId,
                session.WireUserId,
                message.GroundDropNumber,
                result.ItemId,
                result.SlotIndex);

            await sender.SendExplicitAsync(
                context.Connection.ConnectionId,
                new GameExplicitMessageDelivery(
                    [
                        new GetItemResponse(Accepted: true),
                        new GroundPickupNotification(
                            message.GroundDropNumber,
                            session.WireUserId,
                            checked((ushort)result.SlotIndex)),
                    ],
                    []),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        logger.LogInformation(
            "Get item {ConnectionId} user={WireUserId}: drop {Number} refused as {Status}",
            context.Connection.ConnectionId,
            session.WireUserId,
            message.GroundDropNumber,
            result.Status);

        // Only the full bag is answered. See GetItemResponse for why the rest are silent.
        if (result.Status is not GroundPickupStatus.InventoryFull)
            return;

        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([new GetItemResponse(Accepted: false)], []),
            cancellationToken).ConfigureAwait(false);
    }
}
