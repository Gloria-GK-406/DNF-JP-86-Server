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
/// <paramref name="Delivered"/> describes inventory changes only. Overflow is delivered through
/// system mail in the same claim transaction. Unknown templates and storage errors abort the claim.
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
