using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Protocol.Game.CardReward;

/// <summary>Why a flip handed nothing over.</summary>
/// <remarks>
/// Separate values rather than one refusal, because only one of them is the player doing something
/// twice. The rest are ways for the two sides to disagree about whether there are cards at all, and
/// the log has to be able to say which happened.
/// </remarks>
public enum SelectCardStatus
{
    Flipped = 0,

    /// <summary>The character is not in a dungeon run.</summary>
    NotInARun,

    /// <summary>The run has not settled, so it has no cards yet.</summary>
    NoCardsFixed,

    /// <summary>That face has no position under that number.</summary>
    NoSuchPosition,

    /// <summary>That position has already been turned over.</summary>
    AlreadyFlipped,

    /// <summary>
    /// A paid flip whose price this server will not name.
    /// </summary>
    /// <remarks>
    /// <b>Two clears reach this, and the settlement cannot tell them apart by the time it gets
    /// here.</b> The archive prices a paid flip by level and answers nothing for a level it does
    /// not list -- see <see cref="IClearRewardRuleService.PaidFlipCost"/>, whose remarks say why
    /// that is not the same as pricing it at nothing. And a dungeon may switch its own paid card
    /// off, which five of the archive's dungeons do
    /// (<see cref="DungeonDefinition.OffersPaidCard"/>); such a clear has no flip for a price to
    /// be about, so its settlement names none either.
    /// <para>
    /// Both are the same refusal and both cost the player nothing, which is why one value carries
    /// them. Which of the two happened is not on this path to know: what reaches it is the
    /// settlement's <see cref="ClearRewardSettlementResult.PaidFlipCost"/>, an <c>int?</c> whose
    /// absence is the whole of the answer. Separating them would mean widening that seam, and
    /// nothing on the wire or in the player's hands turns on the difference.
    /// </para>
    /// <para>
    /// A flip nobody can price is refused rather than given away, which is what the result screen
    /// already told the client about such a clear: the reward frame writes the largest price the
    /// field can carry, and no total can meet that.
    /// </para>
    /// <para>
    /// A price below zero is refused here too. The archive's table is read verbatim, and a
    /// negative entry in it would be the file asking this server to pay the player for flipping.
    /// </para>
    /// </remarks>
    NotPriced,

    /// <summary>The account could not pay for that flip, and was not charged.</summary>
    NotEnoughGold,
}

/// <summary>What a flip turned up, or why it turned up nothing.</summary>
/// <param name="Slots">
/// Every position of the flipped face, not only the one that was turned. Empty for a refusal.
/// </param>
/// <param name="Delivered">
/// What the bag now holds because of this flip, or null when nothing arrived in it.
/// </param>
/// <param name="GoldCharged">
/// What the account paid to make this flip. Zero for a free flip and for every refusal, which is
/// the whole of what a refusal is allowed to have cost.
/// </param>
/// <param name="GoldPaid">
/// What the account was given for what the flip turned up. Zero for a position that held nothing
/// and for every refusal.
/// </param>
/// <remarks>
/// <paramref name="Delivered"/> is null for more than a refusal: a position that held nothing has
/// nothing to hand over, and a bag that could not take the piece -- no container bound, no room,
/// an item the archive does not declare -- hands over nothing either. The face still goes back in
/// every one of those cases, because the face is what the flip revealed and it was revealed
/// whatever the bag did with it.
/// <para>
/// The two gold figures are reported rather than left to the store to remember, because a flip
/// is the only thing on this path that both spends and pays, and a log that said which of the two
/// happened is the only way to reconstruct one afterwards. They are what moved, not what the
/// account holds.
/// </para>
/// </remarks>
public sealed record SelectCardResult(
    SelectCardStatus Status,
    ClearRewardCardKind Kind,
    int Index,
    IReadOnlyList<ClearRewardCardSlot> Slots,
    CommittedInventoryUpdateState? Delivered = null,
    int GoldCharged = 0,
    int GoldPaid = 0,
    int GoldCarried = 0,
    DungeonCardSelectionChange? StaticChange = null,
    DungeonCardSelectionCommit? SelectionCommit = null,
    InventoryRuntimeValueState? AccountMaterial = null)
{
    public CommittedInventoryUpdateState? BlackDiamondDelivered { get; init; }
    public InventoryRuntimeValueState? BlackDiamondAccountMaterial { get; init; }
    public AccountEntitlementEntryState Entitlements { get; init; } = new([]);
    public IReadOnlyList<CharacterPremiumUsageSlotState> Usage { get; init; } = [];
    public DungeonCardSelectionChange? Change => this.SelectionCommit?.Change ?? this.StaticChange;

    public static SelectCardResult Refused(
        SelectCardStatus status, ClearRewardCardKind kind, int index) =>
        new(status, kind, index, []);
}

/// <summary>One player turning over one position of one of this clear's two faces.</summary>
/// <param name="AccountId">
/// Which account the flipping character belongs to. A flip's gold does not go here -- it is paid
/// into and charged from the gold the character carries -- but the account is what the delivery of
/// a piece is checked against.
/// </param>
public sealed record SelectCardCommand(
    AccountId AccountId,
    CharacterId CharacterId,
    ClearRewardCardKind Kind,
    int Index,
    DungeonRunId ExpectedRunId = default) : ICommand<SelectCardResult>;

/// <summary>
/// Claims the chosen position, hands what was behind it to the bag, and reads back the whole face.
/// </summary>
/// <remarks>
/// The claim is what makes a second flip of the same position hand nothing over, and it is the
/// business service's to make -- nothing here decides whether a position may be taken, and nothing
/// here asks the rules what a card is worth.
/// <para>
/// <b>The face is read before the claim, not after.</b> A face is fixed once and never changes
/// afterwards, so reading it first costs nothing and means that a claim which succeeded is never
/// followed by an answer this could not build. The run is read for it because the card seam hands
/// back the one position it claimed and this frame has to describe all of them; that is a read of
/// world state through the service that owns it, not a second owner of it.
/// </para>
/// <para>
/// <b>The claim and the row in the bag are one change.</b> This command reaches a persistent write
/// the moment a piece lands, so it is registered transactional: a position marked taken whose piece
/// never arrived anywhere is the thing to avoid, and it is the same reasoning the ground pick-up
/// was given. The character's gold is on that same boundary now, which is what makes a charge and a claim one
/// change rather than two.
/// </para>
/// <para>
/// <b>A card is reserved before any persistent write and consumed after all writes succeed.</b>
/// The command transaction rolls the gold and inventory rows back on an exception; releasing the
/// process-local reservation in the same catch keeps the two state stores aligned for a retry.
/// </para>
/// </remarks>
public sealed class SelectCardCommandHandler(
    ICardRewardDomainService cards,
    IDungeonRunDomainService runs,
    ICharacterContainerDomainService containers,
    ICharacterDomainService characters,
    TimeProvider clock,
    IItemGrantDomainService itemGrants,
    ICardFlipCostDomainService flipCosts) : ICommandHandler<SelectCardCommand, SelectCardResult>
{
    public async ValueTask<SelectCardResult> HandleAsync(
        CommandExecutionContext context,
        SelectCardCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Kind is not (ClearRewardCardKind.Free or ClearRewardCardKind.Paid))
            return SelectCardResult.Refused(SelectCardStatus.NoSuchPosition, command.Kind, command.Index);
        var run = runs.Find(command.CharacterId);
        if (run is null
            || (command.ExpectedRunId != default && run.RunId != command.ExpectedRunId))
            return SelectCardResult.Refused(SelectCardStatus.NotInARun, command.Kind, command.Index);

        if (run.ClearRewardCards is not { } fixedCards)
        {
            return SelectCardResult.Refused(
                SelectCardStatus.NoCardsFixed, command.Kind, command.Index);
        }

        if (fixedCards.SlotAt(command.Kind, command.Index) is null)
        {
            return SelectCardResult.Refused(
                SelectCardStatus.NoSuchPosition, command.Kind, command.Index);
        }

        // Asked before anything is charged. The claim below is what actually refuses a repeat, but
        // it refuses by taking the position, and by then the gold would be gone.
        if (fixedCards.IsClaimed(command.Kind, command.Index))
        {
            return SelectCardResult.Refused(
                SelectCardStatus.AlreadyFlipped, command.Kind, command.Index);
        }

        var claim = runs.TryReserveClearRewardCard(
            command.CharacterId, run.RunId, command.Kind, command.Index);
        if (claim is null)
            return SelectCardResult.Refused(
                SelectCardStatus.AlreadyFlipped, command.Kind, command.Index);

        try
        {
            var charge = await this.ChargeAsync(command, run.RunId, cancellationToken)
                .ConfigureAwait(false);
            if (charge.Refusal is { } refused)
            {
                runs.ReleaseClearRewardCard(claim);
                return SelectCardResult.Refused(refused, command.Kind, command.Index);
            }

            // Paid whatever the position held, and paid for turning the card over rather than for
            // what came out of it. A flipped free card is a floor: the archive gives every level a
            // gold base and every difficulty a multiplier, and the settlement frame announced that
            // figure on the card long before any position was claimed or any bag was asked.
            var paid = fixedCards.FaceOf(command.Kind).Gold;
            var carried = paid > 0
                ? await characters.Get(command.CharacterId).AddGoldAsync(paid, cancellationToken)
                    .ConfigureAwait(false)
                : charge.Carried;
            var delivery = claim.Slot.Item is { } piece
                ? await this.DeliverAsync(
                    command.AccountId, command.CharacterId, piece, cancellationToken)
                    .ConfigureAwait(false)
                : null;

            var bonus = command.Kind is ClearRewardCardKind.Free
                ? fixedCards.BlackDiamond?.Slots.FirstOrDefault(slot => slot.Index == 0)?.Item : null;
            var bonusDelivery = bonus is { } bonusPiece
                ? await this.DeliverAsync(command.AccountId, command.CharacterId, bonusPiece, cancellationToken)
                    .ConfigureAwait(false) : null;
            if (bonus is not null && (bonusDelivery is null || (claim.Slot.Item is not null && delivery is null)))
                throw new InvalidOperationException("The ordinary and black-diamond rewards could not both be delivered.");

            var commit = runs.CommitClearRewardCard(claim);
            if (!commit.IsDeferred && commit.Change is null)
            {
                throw new InvalidOperationException(
                    "The clear-card claim was no longer current after its durable award.");
            }

            return new SelectCardResult(
                SelectCardStatus.Flipped,
                command.Kind,
                command.Index,
                fixedCards.FaceOf(command.Kind).Slots,
                delivery?.CharacterItem,
                charge.Price,
                paid,
                carried,
                StaticChange: null,
                SelectionCommit: commit,
                AccountMaterial: delivery?.AccountMaterial)
            {
                Entitlements = charge.Settlement!.Entitlements, Usage = charge.Settlement.Usage,
                BlackDiamondDelivered = bonusDelivery?.CharacterItem,
                BlackDiamondAccountMaterial = bonusDelivery?.AccountMaterial,
            };
        }
        catch
        {
            runs.ReleaseClearRewardCard(claim);
            throw;
        }
    }

    /// <summary>Uses the clear's fixed base price and settles the actor's discount in the award transaction.</summary>
    private async ValueTask<(SelectCardStatus? Refusal, int Price, int Carried, CardFlipCostSettlement? Settlement)> ChargeAsync(
        SelectCardCommand command, DungeonRunId runId, CancellationToken cancellationToken)
    {
        int? basePrice = 0;
        if (command.Kind == ClearRewardCardKind.Paid)
        {
            var current = await characters.Get(command.CharacterId).ProgressAsync(cancellationToken).ConfigureAwait(false);
            if (current is null) return (SelectCardStatus.NotPriced, 0, 0, null);
            var settled = await cards.SettleAsync(
                new ClearRewardSettlement(command.CharacterId, current.Level, RunId: runId), cancellationToken).ConfigureAwait(false);
            basePrice = settled?.PaidFlipCost;
        }
        var cost = await flipCosts.SettleAsync(command.AccountId, command.CharacterId, command.Kind,
            basePrice, cancellationToken).ConfigureAwait(false);
        if (cost is null) return (SelectCardStatus.NotPriced, 0, 0, null);
        int? left = cost.Gold == 0
            ? await characters.Get(command.CharacterId).GoldAsync(cancellationToken).ConfigureAwait(false)
            : await characters.Get(command.CharacterId).TrySpendGoldAsync(cost.Gold, cancellationToken).ConfigureAwait(false);
        return left is null ? (SelectCardStatus.NotEnoughGold, 0, 0, null) : (null, cost.Gold, left.Value, cost);
    }

    /// <summary>
    /// Puts the piece the position held into the character's own bag.
    /// </summary>
    /// <remarks>
    /// Through the same arrival path a floor pick-up and a tutorial reward use, so a card's piece
    /// lands where every other arriving piece lands and by the same rules. Nothing is invented here
    /// about where it goes.
    /// <para>
    /// A bag that cannot take it answers null rather than throwing. The position is already claimed
    /// by then and giving the claim back is not on offer -- the same trade the ground pick-up makes,
    /// and for the same reason: the face has been revealed and cannot be un-revealed.
    /// </para>
    /// </remarks>
    private async ValueTask<CardItemDelivery?> DeliverAsync(
        AccountId accountId,
        CharacterId characterId,
        ClearRewardCardItem piece,
        CancellationToken cancellationToken)
    {
        if (containers.FindContainer(characterId, InventoryContainerType.Main) is null)
            return null;
        var granted = await itemGrants.GrantAsync(
                accountId, characterId, containers, piece.ItemId, piece.Quantity, cancellationToken)
            .ConfigureAwait(false);
        int slotIndex;
        BusinessItemAdditionResult? delivered = null;
        var opensASlot = false;
        switch (granted)
        {
            case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.AddedToPile topped } character:
                slotIndex = topped.SlotIndex;
                delivered = character;
                break;

            case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.PutInEmptySlot opened } character:
                slotIndex = opened.SlotIndex;
                delivered = character;
                opensASlot = true;
                break;

            case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.NoRoom }:
            case BusinessItemAdditionResult { Placement: ItemPlacementOutcome.UnknownItem }:
                return null;

            default:
                // Listed rather than defaulted: an outcome nobody read would become a piece the
                // player was shown and never given, with nothing said about it.
                throw new ArgumentOutOfRangeException(
                    nameof(piece), granted, "No reading was decided for this outcome.");
        }

        var landed = await delivered!.Container.ReadSlotAsync(slotIndex, cancellationToken)
            .ConfigureAwait(false);
        if (landed is null)
            return null;

        if (await ItemGrantWireProjection.SharedAsync(granted, cancellationToken).ConfigureAwait(false) is { } shared)
            return new CardItemDelivery(null, shared);
        var core = landed.Core;
        if (opensASlot)
        {
            // A slot the bag opens carries no durability, and a piece created at zero durability is
            // a broken one. What it should carry was decided when the face was fixed and travels on
            // the card for exactly this moment; a pile it joined already carries one of its own.
            core = core with { Durability = piece.Durability };
            await delivered.Container.PutAsync(slotIndex, core, cancellationToken).ConfigureAwait(false);
        }

        // Built from what was just written rather than re-read through the entry projection: that
        // projection reads the character's rows and this change has not settled yet, so it would
        // answer the piece as the bag opened it and not as it now stands. No name is carried
        // because this frame has no field for one, and because the name of what was just written
        // is settlement's to hand out rather than this caller's to guess at.
        return new CardItemDelivery(
            new CommittedInventoryUpdateState(
                ItemGrantWireProjection.Slot(delivered, slotIndex).Space,
                [new InventoryItemState(null, ItemGrantWireProjection.Index(delivered, slotIndex), core)],
                clock.GetUtcNow()),
            null);
    }

    private sealed record CardItemDelivery(
        CommittedInventoryUpdateState? CharacterItem,
        InventoryRuntimeValueState? AccountMaterial);
}
