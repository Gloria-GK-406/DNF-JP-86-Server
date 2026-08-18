using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.UseStackable;

/// <summary>
/// CMD 0x002C <c>USE_STACKABLE</c>: the player consuming one item off a pile.
/// </summary>
/// <remarks>
/// Observed live on 2026-08-18: a potion drunk from quick slot 3 in the tutorial. The client
/// applies the recovery itself and then asks the server to take one off the pile, so an
/// unanswered request looks like a potion that heals forever -- which is what the session showed
/// before this existed.
///
/// The static census marks the body's layout only partly derivable, because the send path goes
/// through a virtual call whose payload depends on the runtime item type. The prefix the reference
/// server reads is fixed regardless, and the live frame matches it field for field.
/// </remarks>
public sealed record UseStackableRequest(
    GameMessageMetadata Metadata,
    short SlotIndex,
    byte ListType,
    int InstanceValue,
    int ItemCode) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameUseStackable;
}

/// <summary>The CMD acknowledgement for the same type.</summary>
public sealed record UseStackableResponse(
    byte? ErrorCode,
    short SlotIndex,
    byte ListType,
    int InstanceValue,
    int ItemCode) : IGameCmdMessage
{
    /// <summary>The reference server's code for "that is not an item you can use".</summary>
    public const byte InvalidItem = 23;

    public ServerMessageType MessageType => ServerMessageType.GameUseStackable;

    public static UseStackableResponse Success(UseStackableRequest request, int itemCode) =>
        new(null, request.SlotIndex, request.ListType, request.InstanceValue, itemCode);

    public static UseStackableResponse Failure(UseStackableRequest request, byte errorCode) =>
        new(errorCode, request.SlotIndex, request.ListType, request.InstanceValue, request.ItemCode);
}

internal static class UseStackablePayloadCodec
{
    /// <summary>
    /// The shortest body the reference server will parse. The item identifier past offset 7 is
    /// read only when the body carries it; a shorter frame names the slot and leaves the server to
    /// resolve what is in it.
    /// </summary>
    public const int MinimumBodyLength = 7;

    private const int ItemCodeBodyLength = 11;

    public static byte[] Encode(UseStackableResponse message)
    {
        // A refusal has no slot: the reference builder writes the code where the success writes
        // the slot, so the two bodies differ in length as well as in content.
        if (message.ErrorCode is { } errorCode)
        {
            var failure = new byte[11];
            failure[0] = 0x00;
            failure[1] = errorCode;
            failure[2] = message.ListType;
            BinaryPrimitives.WriteInt32LittleEndian(failure.AsSpan(3, 4), message.InstanceValue);
            BinaryPrimitives.WriteInt32LittleEndian(failure.AsSpan(7, 4), message.ItemCode);
            return failure;
        }

        var body = new byte[12];
        body[0] = 0x01;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1, 2), message.SlotIndex);
        body[3] = message.ListType;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4, 4), message.InstanceValue);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8, 4), message.ItemCode);
        return body;
    }

    public static UseStackableRequest Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new(metadata,
            BinaryPrimitives.ReadInt16LittleEndian(body),
            body[2],
            BinaryPrimitives.ReadInt32LittleEndian(body[3..]),
            body.Length >= ItemCodeBodyLength ? BinaryPrimitives.ReadInt32LittleEndian(body[7..]) : 0);
}

internal sealed class UseStackableDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length < UseStackablePayloadCodec.MinimumBodyLength)
        {
            throw new InvalidDataException(
                $"USE_STACKABLE requires at least {UseStackablePayloadCodec.MinimumBodyLength} "
                    + $"body bytes; received '{body.Length}'.");
        }

        return UseStackablePayloadCodec.Decode(metadata, body);
    }
}

internal sealed class UseStackableResponseEncoder : GameCmdMessageEncoder<UseStackableResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(UseStackableResponse message) =>
        OnePayload(GameProtocol.UseStackableType, UseStackablePayloadCodec.Encode(message));
}

/// <summary>
/// Takes one item off the pile the client named.
/// </summary>
/// <remarks>
/// Exactly one, whatever the pile holds: the client draws the recovery and decrements its own
/// count by one before the answer arrives, so consuming any other number would leave the two
/// disagreeing about a slot.
///
/// Equipment is refused. The request can name any slot of the list and the reference server
/// requires the item to be stackable before it will delete from it; without that check a drag onto
/// a quick slot followed by a key press would destroy a piece of gear one press at a time.
///
/// The recovery itself is not the server's to send here. The reference server's use path mutates
/// the inventory and nothing else -- health follows from the item's own effect, which the client
/// already applied.
/// </remarks>
public sealed class UseStackableHandler(
    ISelectedSessionDirectory selectedSessions,
    ICharacterContainerDomainService containers,
    IItemPlacementDefinitionSource placements,
    IGameMessageSender sender,
    ILogger<UseStackableHandler> logger) : ClientMessageHandler<UseStackableRequest>
{
    private const byte MainListType = (byte)InventoryContainerType.Main;
    private const byte EquipmentItemKind = 1;

    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        UseStackableRequest message,
        CancellationToken cancellationToken)
    {
        var response = await ResolveAsync(context, message, cancellationToken).ConfigureAwait(false);
        await sender.SendExplicitAsync(
            context.Connection.ConnectionId,
            new GameExplicitMessageDelivery([response], []),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<UseStackableResponse> ResolveAsync(
        NetworkDispatchContext context,
        UseStackableRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || message.ListType != MainListType)
        {
            logger.LogInformation(
                "Use stackable {ConnectionId}: list {List} slot {Slot} is not served",
                context.Connection.ConnectionId,
                message.ListType,
                message.SlotIndex);
            return UseStackableResponse.Failure(message, UseStackableResponse.InvalidItem);
        }

        var container = containers.FindContainer(session.CharacterId, InventoryContainerType.Main);
        if (container is null)
        {
            logger.LogWarning(
                "Use stackable {ConnectionId} character={CharacterId}: no bound main container",
                context.Connection.ConnectionId,
                session.CharacterId.Value);
            return UseStackableResponse.Failure(message, UseStackableResponse.InvalidItem);
        }

        var item = await container.ReadSlotAsync(message.SlotIndex, cancellationToken)
            .ConfigureAwait(false);

        // The client names what it believes it is using. A slot that holds something else means
        // the two disagree about the inventory, and deleting from it anyway would resolve that
        // disagreement in favour of whichever one is wrong.
        if (item is null
            || (message.ItemCode > 0 && item.ItemId != message.ItemCode)
            || item.ItemKind == EquipmentItemKind)
        {
            logger.LogInformation(
                "Use stackable {ConnectionId} character={CharacterId}: slot {Slot} does not hold a "
                    + "usable item {ItemCode}",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                message.SlotIndex,
                message.ItemCode);
            return UseStackableResponse.Failure(message, UseStackableResponse.InvalidItem);
        }

        // Content has the last word on whether this identifier is something the archive declares
        // as holdable at all. An item nobody can place is one nobody can use.
        var placement = await placements.GetAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
        if (placement is null || placement.ItemKind == EquipmentItemKind)
            return UseStackableResponse.Failure(message, UseStackableResponse.InvalidItem);

        var consumed = await container.ConsumeAsync(item.StorageEntryId, 1, cancellationToken)
            .ConfigureAwait(false);
        if (consumed is null)
        {
            logger.LogWarning(
                "Use stackable {ConnectionId} character={CharacterId}: slot {Slot} changed under "
                    + "the decision, item {ItemId} not consumed",
                context.Connection.ConnectionId,
                session.CharacterId.Value,
                message.SlotIndex,
                item.ItemId);
            return UseStackableResponse.Failure(message, UseStackableResponse.InvalidItem);
        }

        logger.LogInformation(
            "Use stackable {ConnectionId} character={CharacterId}: consumed 1 of item {ItemId} "
                + "from slot {Slot}, {Remaining} left",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            consumed.ItemId,
            consumed.SlotIndex,
            consumed.RemainingCount);

        return UseStackableResponse.Success(message, consumed.ItemId);
    }
}
