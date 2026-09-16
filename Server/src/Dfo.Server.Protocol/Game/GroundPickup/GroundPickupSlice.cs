using System.Buffers.Binary;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonEntry;
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

    /// <summary>The piece belongs to another participant.</summary>
    NotOwner,

    /// <summary>Gold, which goes onto the character rather than into a bag.</summary>
    Banked,
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
    SeekingQuestProgressSynchronization? QuestProgress = null,
    DungeonPickupCommit? PendingCommit = null)
{
    public DungeonPickupChange? CommittedChange => this.PendingCommit?.Change ?? this.Change;

    public static GroundPickupResult Refused(GroundPickupStatus status, int itemId = 0) =>
        new(status, itemId, 0);
}

/// <summary>One character taking one piece off the floor of the room they are standing in.</summary>
public sealed record PickUpGroundDropCommand(
    AccountId AccountId,
    CharacterId CharacterId,
    ushort GroundDropNumber,
    DungeonRunId ExpectedRunId = default,
    long? ExpectedRoomVersion = null)
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
/// The floor reservation is committed only after the transactional persistent write succeeds. A
/// refusal or exception releases it, so the same exact run room may retry without consuming a
/// similarly numbered piece in a replacement room.
/// </para>
/// </remarks>
public sealed class PickUpGroundDropCommandHandler(
    IDungeonRunDomainService runs,
    IGroundPickupDomainService floor,
    ICharacterContainerDomainService containers,
    ICharacterDomainService characters,
    ISeekingQuestProgressDomainService seekingQuests)
    : ICommandHandler<PickUpGroundDropCommand, GroundPickupResult>
{
    public async ValueTask<GroundPickupResult> HandleAsync(
        CommandExecutionContext context,
        PickUpGroundDropCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var run = runs.Find(command.CharacterId);
        if (run is null
            || (command.ExpectedRunId != default && run.RunId != command.ExpectedRunId)
            || (command.ExpectedRoomVersion is { } roomVersion
                && run.RoomVersion != roomVersion))
            return GroundPickupResult.Refused(GroundPickupStatus.NotInARun);

        var found = await floor.FindOnFloorAsync(run, command.GroundDropNumber, cancellationToken)
            .ConfigureAwait(false);
        if (found is null)
            return GroundPickupResult.Refused(GroundPickupStatus.NothingThere);
        if (found.OwnerCharacterId is { } owner && owner != command.CharacterId)
            return GroundPickupResult.Refused(GroundPickupStatus.NotOwner, found.ItemId);

        var piece = Announced(run, found);

        // Gold goes onto the character and never touches a bag, so it is claimed and credited
        // before a container is even looked for -- a character with no bag bound can still pick up
        // coins.
        if (piece.ItemId == RolledDrop.GoldItemId)
        {
            var claim = runs.TryClaimGroundDrop(
                command.CharacterId, run.RunId, run.RoomVersion, command.GroundDropNumber);
            if (claim is null)
                return GroundPickupResult.Refused(GroundPickupStatus.AlreadyTaken);

            try
            {
                var total = await characters.Get(command.CharacterId)
                    .AddGoldAsync(piece.Count, cancellationToken).ConfigureAwait(false);
                var staged = runs.StageGroundDrop(claim, DungeonPickupKind.Gold);
                if (!staged.IsDeferred && staged.Change is null)
                {
                    throw new InvalidOperationException(
                        "The ground-drop claim was no longer current after its durable gold award.");
                }
                return new GroundPickupResult(
                    GroundPickupStatus.Banked,
                    RolledDrop.GoldItemId,
                    0,
                    piece.Count,
                    total,
                    Change: staged.Change,
                    PendingCommit: staged.IsDeferred ? staged : null);
            }
            catch
            {
                runs.ReleaseGroundDrop(claim);
                throw;
            }
        }

        var container = containers.FindContainer(command.CharacterId, InventoryContainerType.Main);
        if (container is null)
            return GroundPickupResult.Refused(GroundPickupStatus.NoBoundContainer, piece.ItemId);

        var itemClaim = runs.TryClaimGroundDrop(
            command.CharacterId, run.RunId, run.RoomVersion, command.GroundDropNumber);
        if (itemClaim is null)
            return GroundPickupResult.Refused(GroundPickupStatus.AlreadyTaken, piece.ItemId);

        try
        {
            var outcome = await container.AddAsync(piece.ItemId, piece.Count, cancellationToken)
                .ConfigureAwait(false);
            int slotIndex;
            switch (outcome)
            {
                case ItemPlacementOutcome.AddedToPile topped:
                    slotIndex = topped.SlotIndex;
                    break;

                case ItemPlacementOutcome.PutInEmptySlot opened:
                    slotIndex = opened.SlotIndex;
                    await CarryOverAsync(container, piece, opened.SlotIndex, cancellationToken)
                        .ConfigureAwait(false);
                    break;

                case ItemPlacementOutcome.NoRoom:
                    runs.ReleaseGroundDrop(itemClaim);
                    return GroundPickupResult.Refused(
                        GroundPickupStatus.InventoryFull, piece.ItemId);

                case ItemPlacementOutcome.UnknownItem:
                    runs.ReleaseGroundDrop(itemClaim);
                    return GroundPickupResult.Refused(
                        GroundPickupStatus.UnknownItem, piece.ItemId);

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(command), outcome, "No reading was decided for this outcome.");
            }

            var questProgress = await seekingQuests.SynchronizeAsync(
                command.AccountId,
                command.CharacterId,
                cancellationToken).ConfigureAwait(false);
            var staged = runs.StageGroundDrop(itemClaim, DungeonPickupKind.Item);
            if (!staged.IsDeferred && staged.Change is null)
            {
                throw new InvalidOperationException(
                    "The ground-drop claim was no longer current after its durable item award.");
            }
            return new GroundPickupResult(
                GroundPickupStatus.PickedUp,
                piece.ItemId,
                slotIndex,
                Change: staged.Change,
                QuestProgress: questProgress,
                PendingCommit: staged.IsDeferred ? staged : null);
        }
        catch
        {
            runs.ReleaseGroundDrop(itemClaim);
            throw;
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
    ///
    /// <b>An avatar must not be written the announced value.</b> It is one of the pieces the
    /// archive calls equipment -- <c>IsEquipmentAsync</c> answers "not interchangeable" -- but the
    /// value inside its core is not the kind the paragraph above is about: it is the identity the
    /// bag drew when it opened the slot, and the key <c>SqliteItemDomainStore.FillAsync</c> looks
    /// the six avatar facts up by. Replacing it leaves the row and its facts unable to find each
    /// other -- the second write is no longer any of the three things <c>Survivors</c> recognises,
    /// so the row is rebuilt, and the facts are searched for under the value that has just replaced
    /// the one they were filed under. Six columns come back NULL and the next entry load refuses
    /// the row: <c>InvalidDataException: Avatar detail N is unavailable.</c>
    /// <para>
    /// So an avatar takes the wear and not the value. The wear is a plain field of the core and
    /// changing it alone leaves the row the same thing standing in the same place, which is what
    /// keeps its facts on it. The class is the archive's answer carried on the slot rather than
    /// the kind byte in the core, for the reason <c>ContainerSlotItem.WorldClass</c> gives.
    /// </para>
    /// <para>
    /// This is the narrow repair, and it is narrow deliberately: what is wrong is one path writing
    /// one field, not the write itself. <c>IItemContainer.PutAsync</c> passes no avatar facts on
    /// any call -- see its own summary -- so any other caller that came to replace a standing
    /// avatar's value would go the same way. None does today; that reading is recorded with this
    /// outcome rather than guarded against here, because a guard in the write would be a rule
    /// about avatars living somewhere no reader of this path would look for it.
    /// </para>
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

        var carried = landed.WorldClass is ItemWorldClass.Avatar
            ? landed.Core with { Durability = found.Durability }
            : landed.Core with
            {
                Value = checked((int)found.Instance),
                Durability = found.Durability,
            };

        await container.PutAsync(slotIndex, carried, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Answers a pick-up with the acknowledgement and the notification, in that order.
/// </summary>
/// <remarks>
/// The order is the reference server's and it is not decoration: the client consumes the one-byte
/// acknowledgement and only then reads the notification that says where the piece went.
/// </remarks>
/// <summary>Projects one committed shared pickup onto current transport generations.</summary>
public sealed class DungeonPickupProjectionDelivery(
    ISelectedSessionDomainService selectedSessions,
    DungeonRunDeliveryRegistry deliveries,
    IGameMessageSender sender,
    TimeProvider clock)
{
    public async ValueTask<bool> DeliverAsync(
        SelectedSessionIdentity requester,
        GroundPickupResult result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(result);
        var change = result.CommittedChange;
        if (change is null || change.Picker != requester.CharacterId)
            return false;

        var recipients = change.Recipients.ToHashSet();
        var frozenPicker = deliveries.Find(change.RunId)
            .SingleOrDefault(frozen => frozen.CharacterId == change.Picker);
        if (!recipients.Contains(change.Picker)
            || frozenPicker is null
            || frozenPicker != requester
            || selectedSessions.FindByCharacter(change.Picker) != frozenPicker)
        {
            return false;
        }

        IGameServerMessage notification;
        IReadOnlyList<IGameServerMessage> required;
        switch (result.Status)
        {
            case GroundPickupStatus.Banked when change.Kind is DungeonPickupKind.Gold:
                notification = new GroundGoldPickupNotification(
                    change.Drop.Number, requester.WireUserId, result.Amount);
                required =
                [
                    new GetItemResponse(Accepted: true),
                    notification,
                    GameInventoryDelivery.CarriedGoldNotification(
                        result.Total, clock.GetUtcNow()),
                ];
                break;

            case GroundPickupStatus.PickedUp when change.Kind is DungeonPickupKind.Item:
                notification = new GroundPickupNotification(
                    change.Drop.Number,
                    requester.WireUserId,
                    checked((ushort)result.SlotIndex));
                var itemMessages = new List<IGameServerMessage>
                {
                    new GetItemResponse(Accepted: true),
                    notification,
                };
                if (result.QuestProgress is { } questProgress)
                {
                    itemMessages.Add(new AcceptedQuestProgressMessage(
                        [.. questProgress.Quests
                            .OrderBy(quest => quest.Slot)
                            .Select(quest => new AcceptedQuestProgressState(
                                checked((ushort)quest.QuestId),
                                unchecked((uint)quest.TriggerValue)))]));
                }
                required = itemMessages;
                break;

            default:
                return false;
        }

        var secondary = deliveries.Find(change.RunId)
            .Where(frozen => frozen.CharacterId != change.Picker)
            .Where(frozen => recipients.Contains(frozen.CharacterId))
            .Where(frozen => selectedSessions.FindByCharacter(frozen.CharacterId) == frozen)
            .Select(frozen => new GameExplicitMessageTarget(
                frozen.ConnectionId,
                [notification]))
            .ToArray();

        await sender.SendExplicitAsync(
            requester.ConnectionId,
            new GameExplicitMessageDelivery(required, secondary),
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    public ValueTask RefuseInventoryFullAsync(
        SelectedSessionIdentity requester,
        CancellationToken cancellationToken) =>
        sender.SendExplicitAsync(
            requester.ConnectionId,
            new GameExplicitMessageDelivery([new GetItemResponse(Accepted: false)], []),
            cancellationToken);
}

public sealed class GetItemHandler(
    ISelectedSessionDomainService selectedSessions,
    IDungeonRunDomainService runs,
    ICommandBus commandBus,
    DungeonPickupProjectionDelivery projection,
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

        var run = runs.Find(session.CharacterId);
        if (selectedSessions.FindByCharacter(session.CharacterId) != session)
            return;

        var result = await commandBus.SendAsync(
            new PickUpGroundDropCommand(
                session.AccountId,
                session.CharacterId,
                message.GroundDropNumber,
                run?.RunId ?? default,
                run?.RoomVersion),
            cancellationToken).ConfigureAwait(false);

        if (result.Status is GroundPickupStatus.Banked)
        {
            logger.LogInformation(
                "Get item {ConnectionId} user={WireUserId}: drop {Number} is {Amount} gold, "
                    + "character now carries {Total}",
                context.Connection.ConnectionId,
                session.WireUserId,
                message.GroundDropNumber,
                result.Amount,
                result.Total);

            await projection.DeliverAsync(session, result, cancellationToken).ConfigureAwait(false);
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

            await projection.DeliverAsync(session, result, cancellationToken).ConfigureAwait(false);
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

        await projection.RefuseInventoryFullAsync(session, cancellationToken).ConfigureAwait(false);
    }
}
