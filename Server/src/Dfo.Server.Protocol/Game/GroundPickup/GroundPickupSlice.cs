using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.DungeonEntry;
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

internal static class GroundPickupPayloadCodec
{
    /// <summary>The piece's number is all the reference server reads of the request.</summary>
    public const int MinimumBodyLength = 2;

    /// <summary>The notification's fixed length, as the reference builder writes it.</summary>
    public const int NotificationBodyLength = 18;

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
}

/// <summary>What a pick-up did, or why it did not.</summary>
public sealed record GroundPickupResult(GroundPickupStatus Status, int ItemId, int SlotIndex)
{
    public static GroundPickupResult Refused(GroundPickupStatus status, int itemId = 0) =>
        new(status, itemId, 0);
}

/// <summary>One character taking one piece off the floor of the room they are standing in.</summary>
public sealed record PickUpGroundDropCommand(CharacterId CharacterId, ushort GroundDropNumber)
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
    IDungeonRoomDropSource roomDrops,
    IItemPlacementDefinitionSource placements,
    ICharacterContainerDomainService containers)
    : ICommandHandler<PickUpGroundDropCommand, GroundPickupResult>
{
    /// <summary>What the archive's equipment index says every worn item is.</summary>
    private const byte EquipmentItemKind = 1;

    public async ValueTask<GroundPickupResult> HandleAsync(
        CommandExecutionContext context,
        PickUpGroundDropCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var run = runs.Find(command.CharacterId);
        if (run is null)
            return GroundPickupResult.Refused(GroundPickupStatus.NotInARun);

        var declared = await roomDrops
            .GetAsync(run.Layout.DungeonId, run.CurrentRoomMapId, cancellationToken)
            .ConfigureAwait(false);
        if (run.FindGroundDrop(declared, command.GroundDropNumber) is not { } found)
            return GroundPickupResult.Refused(GroundPickupStatus.NothingThere);

        var container = containers.FindContainer(command.CharacterId, InventoryContainerType.Main);
        if (container is null)
            return GroundPickupResult.Refused(GroundPickupStatus.NoBoundContainer, found.Drop.ItemId);

        if (runs.TryTakeGroundDrop(command.CharacterId, command.GroundDropNumber) is null)
            return GroundPickupResult.Refused(GroundPickupStatus.AlreadyTaken, found.Drop.ItemId);

        var outcome = await container.AddAsync(found.Drop.ItemId, 1, cancellationToken)
            .ConfigureAwait(false);
        switch (outcome)
        {
            case ItemPlacementOutcome.AddedToPile topped:
                return new GroundPickupResult(
                    GroundPickupStatus.PickedUp, found.Drop.ItemId, topped.SlotIndex);

            case ItemPlacementOutcome.PutInEmptySlot opened:
                await CarryOverAsync(container, run, found, opened.SlotIndex, cancellationToken)
                    .ConfigureAwait(false);
                return new GroundPickupResult(
                    GroundPickupStatus.PickedUp, found.Drop.ItemId, opened.SlotIndex);

            case ItemPlacementOutcome.NoRoom:
                return GroundPickupResult.Refused(
                    GroundPickupStatus.InventoryFull, found.Drop.ItemId);

            case ItemPlacementOutcome.UnknownItem:
                return GroundPickupResult.Refused(
                    GroundPickupStatus.UnknownItem, found.Drop.ItemId);

            default:
                // Listed rather than defaulted: an outcome nobody read would become a piece that
                // left the floor and never arrived anywhere, with nothing said about it.
                throw new ArgumentOutOfRangeException(
                    nameof(command), outcome, "No reading was decided for this outcome.");
        }
    }

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
        DungeonRunState run,
        GroundDrop found,
        int slotIndex,
        CancellationToken cancellationToken)
    {
        var placement = await placements.GetAsync(found.Drop.ItemId, cancellationToken)
            .ConfigureAwait(false);
        if (placement?.ItemKind != EquipmentItemKind)
            return;

        var landed = await container.ReadSlotAsync(slotIndex, cancellationToken)
            .ConfigureAwait(false);
        if (landed is null)
            return;

        await container.PutAsync(
            slotIndex,
            landed.Core with
            {
                Value = checked((int)DungeonEntryPayloadCodec.DropInstance(
                    run, found.Drop.ObjectIndex)),
                Durability = found.Drop.Durability,
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
            new PickUpGroundDropCommand(session.CharacterId, message.GroundDropNumber),
            cancellationToken).ConfigureAwait(false);

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
