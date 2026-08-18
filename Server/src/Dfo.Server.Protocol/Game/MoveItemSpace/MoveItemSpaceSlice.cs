using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MoveItemSpace;

/// <summary>
/// CMD 0x0013 <c>MOVE_ITEMSPACE</c>: an item changing slots.
/// </summary>
/// <remarks>
/// The one request behind every inventory drag, and behind the quick-slot bar as well -- the bar is
/// not its own container but slots 3 to 8 of the main list, so binding an item to a number key is
/// an ordinary move within one list.
///
/// Observed live on 2026-08-18. The client sent it unprompted, one frame after the tutorial reward
/// acknowledgement said the potion had landed in slot 65, asking for slot 3. Nothing answered, and
/// that frame is the last in the capture: the client holds the inventory closed until the
/// acknowledgement arrives, which is why every later drag looked like it did nothing at all.
/// </remarks>
public sealed record MoveItemSpaceRequest(
    GameMessageMetadata Metadata,
    byte SourceListType,
    short SourceSlotIndex,
    int SourceInstanceValue,
    int MoveCount,
    byte DestinationListType,
    short DestinationSlotIndex,
    int DestinationInstanceValue) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameMoveItemSpace;
}

/// <summary>
/// The CMD acknowledgement for the same type: either where the item ended up, or why it did not
/// move.
/// </summary>
public sealed record MoveItemSpaceResponse(
    byte? ErrorCode,
    byte SourceListType,
    short SourceSlotIndex,
    int MoveValue,
    byte DestinationListType,
    short DestinationSlotIndex) : IGameCmdMessage
{
    /// <summary>The client's code for "that move is not allowed".</summary>
    public const byte InvalidOperation = 0x02;

    /// <summary>The client's code for "the server could not carry that out".</summary>
    public const byte Rejected = 0x04;

    public ServerMessageType MessageType => ServerMessageType.GameMoveItemSpace;

    public static MoveItemSpaceResponse Success(
        MoveItemSpaceRequest request, int moveValue, short destinationSlotIndex) =>
        new(null, request.SourceListType, request.SourceSlotIndex, moveValue,
            request.DestinationListType, destinationSlotIndex);

    public static MoveItemSpaceResponse Failure(MoveItemSpaceRequest request, byte errorCode) =>
        new(errorCode, request.SourceListType, request.SourceSlotIndex, 0,
            request.DestinationListType, request.DestinationSlotIndex);
}

internal static class MoveItemSpacePayloadCodec
{
    /// <summary>
    /// The shortest body the reference server will parse. Live frames are 24 bytes; the two fields
    /// past offset 14 are read only when the body is long enough to carry them, so a shorter one is
    /// still a valid request rather than a truncated frame.
    /// </summary>
    public const int MinimumBodyLength = 14;

    public static byte[] Encode(MoveItemSpaceResponse message)
    {
        if (message.ErrorCode is { } errorCode)
            return [0x00, errorCode, message.SourceListType, message.DestinationListType];

        var body = new byte[11];
        body[0] = 0x01;
        body[1] = message.SourceListType;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(2, 2), message.SourceSlotIndex);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4, 4), message.MoveValue);
        body[8] = message.DestinationListType;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(9, 2), message.DestinationSlotIndex);
        return body;
    }
}

internal sealed class MoveItemSpaceDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < MoveItemSpacePayloadCodec.MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"MOVE_ITEMSPACE requires at least {MoveItemSpacePayloadCodec.MinimumBodyLength} "
                    + $"body bytes; received '{body.Length}'.");
        }

        return new MoveItemSpaceRequest(
            metadata,
            body[0],
            BinaryPrimitives.ReadInt16LittleEndian(body[1..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[3..]),
            BinaryPrimitives.ReadInt32LittleEndian(body[7..]),
            body[11],
            BinaryPrimitives.ReadInt16LittleEndian(body[12..]),
            body.Length >= 18 ? BinaryPrimitives.ReadInt32LittleEndian(body[14..]) : 0);
    }
}

internal sealed class MoveItemSpaceResponseEncoder : GameCmdMessageEncoder<MoveItemSpaceResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MoveItemSpaceResponse message) =>
        OnePayload(GameProtocol.MoveItemSpaceType, MoveItemSpacePayloadCodec.Encode(message));
}

/// <summary>
/// Moves an item between two slots of the main inventory and tells the client where it ended up.
/// </summary>
/// <remarks>
/// Only the main list is served. The other containers this request can name -- avatar, cargo,
/// equipment, pet, the knight shield catalogue -- each carry their own rules about what may enter
/// them, and answering for them without those rules would let an item through a door that should
/// have been shut. They are refused as an invalid operation, which is a state the client already
/// draws.
///
/// The request may arrive with its two ends the other way round. When the source slot is empty the
/// client is naming the slot it wants filled, and the item to put there is the one it described as
/// the destination; the reference server carries the same reversal. Both readings are answered with
/// the request's own source and destination echoed back, because the client matches the
/// acknowledgement against what it sent.
/// </remarks>
public sealed class MoveItemSpaceHandler(
    ISelectedSessionDirectory selectedSessions,
    IEntryInventoryMutationRepository inventory,
    IItemPlacementDefinitionSource placements,
    IItemSlotRuleService slotRules,
    IGameMessageSender sender,
    ILogger<MoveItemSpaceHandler> logger) : ClientMessageHandler<MoveItemSpaceRequest>
{
    private const byte MainListType = (byte)InventoryContainerType.Main;

    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        MoveItemSpaceRequest message,
        CancellationToken cancellationToken)
    {
        var response = await ResolveAsync(context, message, cancellationToken).ConfigureAwait(false);
        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([response], []),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<MoveItemSpaceResponse> ResolveAsync(
        NetworkDispatchContext context,
        MoveItemSpaceRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Move item space {ConnectionId}: no published session",
                context.Connection.ConnectionId);
            return MoveItemSpaceResponse.Failure(message, MoveItemSpaceResponse.Rejected);
        }

        if (message.SourceListType != MainListType || message.DestinationListType != MainListType)
        {
            logger.LogInformation(
                "Move item space {ConnectionId}: lists {Source}->{Destination} are not served",
                context.Connection.ConnectionId,
                message.SourceListType,
                message.DestinationListType);
            return MoveItemSpaceResponse.Failure(message, MoveItemSpaceResponse.InvalidOperation);
        }

        // Naming one slot twice is not a move. The client sends it while settling a drag that
        // ended where it started, and it expects a success rather than a refusal.
        if (message.SourceSlotIndex == message.DestinationSlotIndex)
            return MoveItemSpaceResponse.Success(message, 0, message.DestinationSlotIndex);

        var slots = await inventory.ReadMainSlotsAsync(
            session.CharacterId, message.SourceSlotIndex, message.DestinationSlotIndex, cancellationToken)
            .ConfigureAwait(false);

        var (moving, displaced, targetSlot) = slots.Source is not null
            ? (slots.Source, slots.Destination, message.DestinationSlotIndex)
            : (slots.Destination, null, message.SourceSlotIndex);

        if (moving is null)
        {
            logger.LogInformation(
                "Move item space {ConnectionId} character={CharacterId}: slots {Source} and "
                    + "{Destination} are both empty",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                message.SourceSlotIndex,
                message.DestinationSlotIndex);
            return MoveItemSpaceResponse.Failure(message, MoveItemSpaceResponse.Rejected);
        }

        var plan = await PlanAsync(moving, displaced, targetSlot, message.MoveCount, cancellationToken)
            .ConfigureAwait(false);
        if (plan is null)
            return MoveItemSpaceResponse.Failure(message, MoveItemSpaceResponse.InvalidOperation);

        if (!await inventory.ApplyMainMoveAsync(session.CharacterId, plan, cancellationToken)
                .ConfigureAwait(false))
        {
            logger.LogWarning(
                "Move item space {ConnectionId} character={CharacterId}: slots changed under the "
                    + "decision, item {ItemId} stays at {Slot}",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                moving.ItemId,
                moving.SlotIndex);
            return MoveItemSpaceResponse.Failure(message, MoveItemSpaceResponse.Rejected);
        }

        logger.LogInformation(
            "Move item space {ConnectionId} character={CharacterId}: item {ItemId} {Kind} "
                + "{From}->{To} count={Count}",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            moving.ItemId,
            plan.Kind,
            moving.SlotIndex,
            plan.TargetSlotIndex,
            plan.MoveCount);

        return MoveItemSpaceResponse.Success(
            message, plan.MoveCount, message.DestinationSlotIndex);
    }

    /// <summary>
    /// Decides what the move does, or answers null when the item may not go there.
    /// </summary>
    /// <remarks>
    /// A count of zero, or one larger than the stack, means the whole stack: the client leaves the
    /// field at zero for a plain drag and only fills it in when a split dialog asked the player for
    /// a number.
    /// </remarks>
    private async ValueTask<MainInventoryMovePlan?> PlanAsync(
        MainInventoryOccupant moving,
        MainInventoryOccupant? displaced,
        short targetSlot,
        int requestedCount,
        CancellationToken cancellationToken)
    {
        var placement = await placements.GetAsync(moving.ItemId, cancellationToken).ConfigureAwait(false);
        if (placement is null || !slotRules.CanOccupyMainSlot(placement, targetSlot))
            return null;

        var moveCount = requestedCount <= 0 || requestedCount > moving.Count
            ? moving.Count
            : requestedCount;

        if (displaced is null)
        {
            return new MainInventoryMovePlan(
                moving.StorageEntryId, targetSlot, moveCount, null,
                moveCount == moving.Count
                    ? MainInventoryMoveKind.Relocate
                    : MainInventoryMoveKind.Split);
        }

        // Two piles of the same item merge; anything else trades places. A merge that would burst
        // the stack limit is refused rather than silently truncated, so the client keeps drawing
        // the counts the server holds.
        if (displaced.ItemId == moving.ItemId)
        {
            return placement.StackLimit > 1 && displaced.Count + moveCount <= placement.StackLimit
                ? new MainInventoryMovePlan(
                    moving.StorageEntryId, targetSlot, moveCount, displaced.StorageEntryId,
                    MainInventoryMoveKind.Merge)
                : null;
        }

        // A swap has to satisfy both ends: the displaced item is going to the slot this one is
        // leaving, and it has its own band.
        if (moveCount != moving.Count)
            return null;

        var displacedPlacement = await placements.GetAsync(displaced.ItemId, cancellationToken)
            .ConfigureAwait(false);
        if (displacedPlacement is null
            || !slotRules.CanOccupyMainSlot(displacedPlacement, moving.SlotIndex))
            return null;

        return new MainInventoryMovePlan(
            moving.StorageEntryId, targetSlot, moveCount, displaced.StorageEntryId,
            MainInventoryMoveKind.Swap);
    }
}
