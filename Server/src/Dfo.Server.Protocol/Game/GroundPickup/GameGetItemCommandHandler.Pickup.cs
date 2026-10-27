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
internal sealed partial class GameGetItemCommandHandler
{
    internal async ValueTask<GroundPickupResult> PickUpAsync(
        GroundPickupInput command,
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
                var goldProgress = await questProgress.ReportAsync(
                    command.AccountId, command.CharacterId,
                    [new QuestHoldingsChanged()], cancellationToken).ConfigureAwait(false);
                return new GroundPickupResult(
                    GroundPickupStatus.Banked,
                    RolledDrop.GoldItemId,
                    0,
                    piece.Count,
                    total,
                    Change: staged.Change,
                    QuestProgress: goldProgress.NotificationSnapshot,
                    PendingCommit: staged.IsDeferred ? staged : null);
            }
            catch
            {
                runs.ReleaseGroundDrop(claim);
                throw;
            }
        }

        if (epicBook?.FindPiece(piece.ItemId) is not null)
        {
            var claim = runs.TryClaimGroundDrop(command.CharacterId, run.RunId, run.RoomVersion, command.GroundDropNumber);
            if (claim is null) return GroundPickupResult.Refused(GroundPickupStatus.AlreadyTaken, piece.ItemId);
            try
            {
                var added = await epicBook.AddPiecesAsync(command.AccountId, piece.ItemId, piece.Count, cancellationToken).ConfigureAwait(false);
                var staged = runs.StageGroundDrop(claim, DungeonPickupKind.EpicPiece);
                if (!staged.IsDeferred && staged.Change is null)
                    throw new InvalidOperationException("The epic piece ground claim is no longer current.");
                return new(GroundPickupStatus.EpicPieceCollected, piece.ItemId, 0,
                    Change: staged.Change, PendingCommit: staged.IsDeferred ? staged : null, EpicPiece: added);
            }
            catch { runs.ReleaseGroundDrop(claim); throw; }
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
            var granted = await itemGrants.GrantAsync(
                    command.AccountId, command.CharacterId, containers, piece.ItemId, piece.Count,
                    cancellationToken)
                .ConfigureAwait(false);
            int slotIndex;
            InventoryRuntimeValueState? accountMaterial = null;
            switch (granted)
            {
                case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.AddedToPile topped } character:
                    slotIndex = ItemGrantWireProjection.Index(character, topped.SlotIndex);
                    break;

                case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.PutInEmptySlot opened } character:
                    slotIndex = ItemGrantWireProjection.Index(character, opened.SlotIndex);
                    if (character.Container.Space != BusinessInventorySpace.SharedMaterial)
                        await CarryOverAsync(character.Container, piece, opened.SlotIndex, cancellationToken).ConfigureAwait(false);
                    break;

                case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.NoRoom }:
                    runs.ReleaseGroundDrop(itemClaim);
                    return GroundPickupResult.Refused(
                        GroundPickupStatus.InventoryFull, piece.ItemId);

                case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.UnknownItem }:
                    runs.ReleaseGroundDrop(itemClaim);
                    return GroundPickupResult.Refused(
                        GroundPickupStatus.UnknownItem, piece.ItemId);

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(command), granted, "No reading was decided for this outcome.");
            }

            accountMaterial = await ItemGrantWireProjection.SharedAsync(granted, cancellationToken).ConfigureAwait(false);
            var reported = await questProgress.ReportAsync(
                command.AccountId, command.CharacterId,
                [new QuestHoldingsChanged()], cancellationToken).ConfigureAwait(false);
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
                QuestProgress: reported.NotificationSnapshot,
                PendingCommit: staged.IsDeferred ? staged : null,
                AccountMaterial: accountMaterial);
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

    /// <summary>Copies the floor durability and compatible legacy instance value to the received item.</summary>
    /// <remarks>
    /// Avatar and creature instance values identify their existing details and remain unchanged.
    /// Ordinary equipment retains the value announced for the floor drop; its item UID is separate.
    /// </remarks>
    private async ValueTask CarryOverAsync(
        IBusinessItemContainer container,
        FloorPickup found,
        int slotIndex,
        CancellationToken cancellationToken)
    {
        if (!found.IsEquipment)
            return;

        var landed = await container.GetAsync(slotIndex, cancellationToken)
            .ConfigureAwait(false);
        if (landed is null)
            return;

        await container.UpdateAsync(landed, current => current with
        {
            InstanceValue = current.Type is ItemTraits.Avatar or ItemTraits.Creature ? current.InstanceValue : checked((int)found.Instance),
            Durability = found.Durability,
        }, cancellationToken).ConfigureAwait(false);
    }


    internal async ValueTask<bool> DeliverPickupAsync(
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
        var partyMembers = FindRecipients(requester, change.RunId);
        var frozenPicker = partyMembers
            .SingleOrDefault(frozen => frozen.CharacterId == change.Picker);
        if (!recipients.Contains(change.Picker)
            || frozenPicker is null
            || frozenPicker != requester
            || !sessions.IsInWorld(frozenPicker))
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
                var goldMessages = new List<IGameServerMessage>
                {
                    new GetItemResponse(Accepted: true),
                    notification,
                    GameInventoryDelivery.CarriedGoldNotification(
                        result.Total, clock.GetUtcNow()),
                };
                if (result.QuestProgress is { } goldProgress)
                    goldMessages.Add(QuestProgressMessage(goldProgress));
                required = goldMessages;
                break;

            case GroundPickupStatus.EpicPieceCollected when change.Kind is DungeonPickupKind.EpicPiece:
                notification = new GroundPickupNotification(change.Drop.Number, requester.WireUserId, 0);
                var epicMessages = new List<IGameServerMessage> { new GetItemResponse(Accepted: true), notification };
                if (result.EpicPiece is { Added: > 0 } added)
                    epicMessages.Add(new EpicBookStateMessage(1, [new(added.PieceId, added.Total)]));
                required = epicMessages;
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
                if (result.AccountMaterial is { } accountMaterial)
                {
                    itemMessages.Add(GameInventoryDelivery.VirtualResourceNotification(
                        [new InventoryRuntimeValueState(
                            accountMaterial.SlotIndex,
                            accountMaterial.ItemId,
                            accountMaterial.Value)],
                        clock.GetUtcNow()));
                }
                if (result.QuestProgress is { } questProgress)
                    itemMessages.Add(QuestProgressMessage(questProgress));
                required = itemMessages;
                break;

            default:
                return false;
        }

        static AcceptedQuestProgressMessage QuestProgressMessage(
            QuestProgressSnapshot progress) =>
            new([.. progress.Quests
                .OrderBy(quest => quest.Slot)
                .Select(quest => new AcceptedQuestProgressState(
                    checked((ushort)quest.QuestId),
                    unchecked((uint)quest.TriggerValue)))]);

        var secondary = partyMembers
            .Where(frozen => frozen.CharacterId != change.Picker)
            .Where(frozen => recipients.Contains(frozen.CharacterId))
            .Where(frozen => sessions.IsInWorld(frozen))
            .Select(frozen => new GameExplicitMessageTarget(
                frozen.ConnectionId,
                [notification]))
            .ToArray();

        await outbox.RegisterExplicitAsync(
            requester.ConnectionId,
            new GameExplicitMessageDelivery(required, secondary),
            cancellationToken).ConfigureAwait(false);

        return true;
    }

    internal ValueTask RefuseInventoryFullAsync(
        SelectedSessionIdentity requester,
        CancellationToken cancellationToken) =>
        outbox.RegisterExplicitAsync(
            requester.ConnectionId,
            new GameExplicitMessageDelivery([new GetItemResponse(Accepted: false)], []),
            cancellationToken);


    internal static ValueTask<DungeonClearChange?> EvaluateClearAsync(IDungeonClearDomainService clearEvaluator,
        SelectedSessionIdentity requester, GroundPickupResult result)
    {
        var change = result.CommittedChange;
        return result.Status is GroundPickupStatus.PickedUp && result.QuestProgress is not null
            && change is not null && change.Picker == requester.CharacterId
                ? clearEvaluator.EvaluateAsync(requester.AccountId, requester.CharacterId,
                    change.RunId, change.RoomVersion, CancellationToken.None)
                : ValueTask.FromResult<DungeonClearChange?>(null);
    }

}
