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
