using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

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

/// <summary>What turning one clear-reward card over did, or why it did not.</summary>
/// <param name="Slots">The face the card belongs to, as fixed at settlement.</param>
/// <param name="Delivered">The position's piece as it landed in the inventory; mailed overflow is not listed.</param>
/// <param name="BlackDiamondDelivered">The black-diamond bonus a free flip also hands over.</param>
public sealed record CardFlipOutcome(
    SelectCardStatus Status,
    ClearRewardCardKind Kind,
    int Index,
    IReadOnlyList<ClearRewardCardSlot> Slots,
    IReadOnlyList<ItemDeliveryAddition> Delivered,
    IReadOnlyList<ItemDeliveryAddition> BlackDiamondDelivered,
    int GoldCharged = 0,
    int GoldPaid = 0,
    int GoldCarried = 0,
    DungeonCardSelectionCommit? SelectionCommit = null,
    CardFlipCostSettlement? Cost = null)
{
    public static CardFlipOutcome Refused(SelectCardStatus status, ClearRewardCardKind kind, int index) =>
        new(status, kind, index, [], [], []);
}

/// <summary>Turns over one position of the clear's fixed cards.</summary>
public interface ICardFlipDomainService
{
    /// <summary>
    /// Reserves the position, charges the flip, pays the face's gold, delivers the position's piece
    /// (and a free flip's black-diamond bonus) and commits the reservation. Runs inside the
    /// caller's write unit; a refusal or failure releases the reservation.
    /// </summary>
    ValueTask<CardFlipOutcome> FlipAsync(AccountId accountId, CharacterId characterId,
        ClearRewardCardKind kind, int index, DungeonRunId expectedRunId, CancellationToken cancellationToken);
}
