using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// Moves a declared or dropped piece off the current Cell's floor and onto the character.
/// </summary>
/// <remarks>
/// The piece is claimed from the Cell before it is banked so a concurrent request cannot reserve
/// the same stable number. The claim is staged only after the transactional write succeeds; a
/// refusal or exception releases it.
/// </remarks>
internal sealed class GroundPickupDomainService(
    IDungeonRunGroundDropDomainService runs,
    ICharacterDomainService characters,
    IEpicBookDomainService epicBook,
    ICharacterContainerDomainService containers,
    IItemGrantDomainService itemGrants,
    IQuestProgressDomainService questProgress) : IGroundPickupDomainService
{
    public ValueTask<GroundPiece?> FindOnFloorAsync(
        DungeonRunState run, ushort groundDropNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            run.CurrentCellState.FindGroundItem(groundDropNumber)?.ToGroundPiece());
    }

    public async ValueTask<GroundPickupOutcome> PickUpAsync(
        GroundPickupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var run = runs.Find(request.CharacterId);
        if (run is null
            || (request.ExpectedRunId != default && run.RunId != request.ExpectedRunId)
            || (request.ExpectedRoomVersion is { } roomVersion
                && run.RoomVersion != roomVersion))
            return GroundPickupOutcome.Refused(GroundPickupStatus.NotInARun);

        var piece = await FindOnFloorAsync(run, request.GroundDropNumber, cancellationToken)
            .ConfigureAwait(false);
        if (piece is null)
            return GroundPickupOutcome.Refused(GroundPickupStatus.NothingThere);
        if (piece.OwnerCharacterId is { } owner && owner != request.CharacterId)
            return GroundPickupOutcome.Refused(GroundPickupStatus.NotOwner, piece.ItemId);

        // Gold goes onto the character and never touches a bag, so it is claimed and credited
        // before a container is even looked for -- a character with no bag bound can still pick up
        // coins.
        if (piece.ItemId == RolledDrop.GoldItemId)
        {
            var claim = runs.TryClaimGroundDrop(
                request.CharacterId, run.RunId, run.RoomVersion, request.GroundDropNumber);
            if (claim is null)
                return GroundPickupOutcome.Refused(GroundPickupStatus.AlreadyTaken);

            try
            {
                var total = await characters.Get(request.CharacterId)
                    .AddGoldAsync(piece.Count, cancellationToken).ConfigureAwait(false);
                var staged = runs.StageGroundDrop(claim, DungeonPickupKind.Gold);
                if (!staged.IsDeferred && staged.Change is null)
                {
                    throw new InvalidOperationException(
                        "The ground-drop claim was no longer current after its durable gold award.");
                }
                var goldProgress = await questProgress.ReportAsync(
                    request.AccountId, request.CharacterId,
                    [new QuestHoldingsChanged()], cancellationToken).ConfigureAwait(false);
                return new GroundPickupOutcome(
                    GroundPickupStatus.Banked,
                    RolledDrop.GoldItemId,
                    Amount: piece.Count,
                    Total: total,
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

        if (epicBook.FindPiece(piece.ItemId) is not null)
        {
            var claim = runs.TryClaimGroundDrop(request.CharacterId, run.RunId, run.RoomVersion, request.GroundDropNumber);
            if (claim is null) return GroundPickupOutcome.Refused(GroundPickupStatus.AlreadyTaken, piece.ItemId);
            try
            {
                var added = await epicBook.AddPiecesAsync(request.AccountId, piece.ItemId, piece.Count, cancellationToken).ConfigureAwait(false);
                var staged = runs.StageGroundDrop(claim, DungeonPickupKind.EpicPiece);
                if (!staged.IsDeferred && staged.Change is null)
                    throw new InvalidOperationException("The epic piece ground claim is no longer current.");
                return new(GroundPickupStatus.EpicPieceCollected, piece.ItemId,
                    Change: staged.Change, PendingCommit: staged.IsDeferred ? staged : null, EpicPiece: added);
            }
            catch { runs.ReleaseGroundDrop(claim); throw; }
        }

        var container = containers.FindContainer(request.CharacterId, InventoryContainerType.Main);
        if (container is null)
            return GroundPickupOutcome.Refused(GroundPickupStatus.NoBoundContainer, piece.ItemId);

        var itemClaim = runs.TryClaimGroundDrop(
            request.CharacterId, run.RunId, run.RoomVersion, request.GroundDropNumber);
        if (itemClaim is null)
            return GroundPickupOutcome.Refused(GroundPickupStatus.AlreadyTaken, piece.ItemId);

        try
        {
            var granted = await itemGrants.GrantAsync(
                    request.CharacterId, containers, piece.ItemId, piece.Count,
                    cancellationToken)
                .ConfigureAwait(false);
            int slotIndex;
            switch (granted)
            {
                case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.AddedToPile topped }:
                    slotIndex = topped.SlotIndex;
                    break;

                case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.PutInEmptySlot opened } character:
                    slotIndex = opened.SlotIndex;
                    if (character.Container.Space != BusinessInventorySpace.SharedMaterial)
                        await CarryOverAsync(character.Container, run, piece, opened.SlotIndex, cancellationToken).ConfigureAwait(false);
                    break;

                case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.NoRoom }:
                    runs.ReleaseGroundDrop(itemClaim);
                    return GroundPickupOutcome.Refused(
                        GroundPickupStatus.InventoryFull, piece.ItemId);

                case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.UnknownItem }:
                    runs.ReleaseGroundDrop(itemClaim);
                    return GroundPickupOutcome.Refused(
                        GroundPickupStatus.UnknownItem, piece.ItemId);

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(request), granted, "No reading was decided for this outcome.");
            }

            var reported = await questProgress.ReportAsync(
                request.AccountId, request.CharacterId,
                [new QuestHoldingsChanged()], cancellationToken).ConfigureAwait(false);
            var staged = runs.StageGroundDrop(itemClaim, DungeonPickupKind.Item);
            if (!staged.IsDeferred && staged.Change is null)
            {
                throw new InvalidOperationException(
                    "The ground-drop claim was no longer current after its durable item award.");
            }
            return new GroundPickupOutcome(
                GroundPickupStatus.PickedUp,
                piece.ItemId,
                granted,
                slotIndex,
                Change: staged.Change,
                QuestProgress: reported.NotificationSnapshot,
                PendingCommit: staged.IsDeferred ? staged : null);
        }
        catch
        {
            runs.ReleaseGroundDrop(itemClaim);
            throw;
        }
    }

    /// <summary>Copies the floor durability and announced instance value to the received item.</summary>
    /// <remarks>
    /// Avatar and creature instance values identify their existing details and remain unchanged.
    /// Ordinary equipment retains the value announced for the floor drop; its item UID is separate.
    /// </remarks>
    private static async ValueTask CarryOverAsync(
        IBusinessItemContainer container,
        DungeonRunState run,
        GroundPiece piece,
        int slotIndex,
        CancellationToken cancellationToken)
    {
        if (!piece.IsEquipment)
            return;

        var landed = await container.GetAsync(slotIndex, cancellationToken)
            .ConfigureAwait(false);
        if (landed is null)
            return;

        var instance = GroundDropInstance.For(run, piece);
        await container.UpdateAsync(landed, current => current with
        {
            InstanceValue = current.Type is ItemTraits.Avatar or ItemTraits.Creature ? current.InstanceValue : checked((int)instance),
            Durability = piece.Durability,
        }, cancellationToken).ConfigureAwait(false);
    }
}
