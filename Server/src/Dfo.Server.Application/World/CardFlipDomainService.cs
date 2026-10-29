using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class CardFlipDomainService(IDungeonRunCardDomainService runs, ICardRewardDomainService cards,
    ICharacterDomainService characters, IItemDeliveryDomainService itemDelivery,
    ICardFlipCostDomainService flipCosts) : ICardFlipDomainService
{
    public async ValueTask<CardFlipOutcome> FlipAsync(AccountId accountId, CharacterId characterId,
        ClearRewardCardKind kind, int index, DungeonRunId expectedRunId, CancellationToken cancellationToken)
    {
        if (kind is not (ClearRewardCardKind.Free or ClearRewardCardKind.Paid))
            return CardFlipOutcome.Refused(SelectCardStatus.NoSuchPosition, kind, index);
        var run = runs.Find(characterId);
        if (run is null
            || (expectedRunId != default && run.RunId != expectedRunId))
            return CardFlipOutcome.Refused(SelectCardStatus.NotInARun, kind, index);

        if (run.ClearRewardCards is not { } fixedCards)
        {
            return CardFlipOutcome.Refused(
                SelectCardStatus.NoCardsFixed, kind, index);
        }

        if (fixedCards.SlotAt(kind, index) is null)
        {
            return CardFlipOutcome.Refused(
                SelectCardStatus.NoSuchPosition, kind, index);
        }

        // Asked before anything is charged. The claim below is what actually refuses a repeat, but
        // it refuses by taking the position, and by then the gold would be gone.
        if (fixedCards.IsClaimed(kind, index))
        {
            return CardFlipOutcome.Refused(
                SelectCardStatus.AlreadyFlipped, kind, index);
        }

        var claim = runs.TryReserveClearRewardCard(
            characterId, run.RunId, kind, index);
        if (claim is null)
            return CardFlipOutcome.Refused(
                SelectCardStatus.AlreadyFlipped, kind, index);

        try
        {
            var charge = await this.ChargeAsync(accountId, characterId, kind, run.RunId, cancellationToken)
                .ConfigureAwait(false);
            if (charge.Refusal is { } refused)
            {
                runs.ReleaseClearRewardCard(claim);
                return CardFlipOutcome.Refused(refused, kind, index);
            }

            // Paid whatever the position held, and paid for turning the card over rather than for
            // what came out of it. A flipped free card is a floor: the archive gives every level a
            // gold base and every difficulty a multiplier, and the settlement frame announced that
            // figure on the card long before any position was claimed or any bag was asked.
            var paid = fixedCards.FaceOf(kind).Gold;
            var carried = paid > 0
                ? await characters.Get(characterId).AddGoldAsync(paid, cancellationToken)
                    .ConfigureAwait(false)
                : charge.Carried;
            var delivery = claim.Slot.Item is { } piece
                ? await this.DeliverAsync(
                    accountId, characterId, piece, cancellationToken)
                    .ConfigureAwait(false)
                : [];

            var bonus = kind is ClearRewardCardKind.Free
                ? fixedCards.BlackDiamond?.Slots.FirstOrDefault(slot => slot.Index == 0)?.Item : null;
            var bonusDelivery = bonus is { } bonusPiece
                ? await this.DeliverAsync(accountId, characterId, bonusPiece, cancellationToken)
                    .ConfigureAwait(false) : [];


            var commit = runs.CommitClearRewardCard(claim);
            if (!commit.IsDeferred && commit.Change is null)
            {
                throw new InvalidOperationException(
                    "The clear-card claim was no longer current after its durable award.");
            }

            return new CardFlipOutcome(
                SelectCardStatus.Flipped,
                kind,
                index,
                fixedCards.FaceOf(kind).Slots,
                delivery,
                bonusDelivery,
                charge.Price,
                paid,
                carried,
                commit,
                charge.Settlement);
        }
        catch
        {
            runs.ReleaseClearRewardCard(claim);
            throw;
        }
    }


    /// <summary>Uses the clear's fixed base price and settles the actor's discount in the award transaction.</summary>
    private async ValueTask<(SelectCardStatus? Refusal, int Price, int Carried, CardFlipCostSettlement? Settlement)> ChargeAsync(
        AccountId accountId, CharacterId characterId, ClearRewardCardKind kind, DungeonRunId runId, CancellationToken cancellationToken)
    {
        int? basePrice = 0;
        if (kind == ClearRewardCardKind.Paid)
        {
            var current = await characters.Get(characterId).ProgressAsync(cancellationToken).ConfigureAwait(false);
            if (current is null) return (SelectCardStatus.NotPriced, 0, 0, null);
            var settled = await cards.SettleAsync(
                new ClearRewardSettlement(characterId, current.Level, RunId: runId), cancellationToken).ConfigureAwait(false);
            basePrice = settled?.PaidFlipCost;
        }
        var cost = await flipCosts.SettleAsync(accountId, characterId, kind,
            basePrice, cancellationToken).ConfigureAwait(false);
        if (cost is null) return (SelectCardStatus.NotPriced, 0, 0, null);
        int? left = cost.Gold == 0
            ? await characters.Get(characterId).GoldAsync(cancellationToken).ConfigureAwait(false)
            : await characters.Get(characterId).TrySpendGoldAsync(cost.Gold, cancellationToken).ConfigureAwait(false);
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
    /// Overflow goes to the mailbox rather than refusing. The position is already claimed
    /// by then and giving the claim back is not on offer -- the same trade the ground pick-up makes,
    /// and for the same reason: the face has been revealed and cannot be un-revealed.
    /// </para>
    /// </remarks>
    private async ValueTask<IReadOnlyList<ItemDeliveryAddition>> DeliverAsync(
        AccountId accountId,
        CharacterId characterId,
        ClearRewardCardItem piece,
        CancellationToken cancellationToken)
    {
        var receipt = await itemDelivery.DeliverAsync(accountId, characterId,
            [new(piece.ItemId, piece.Quantity) { Durability = piece.Durability }], "地下城翻牌奖励", cancellationToken, ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
        return receipt.Inventory;
    }
}
