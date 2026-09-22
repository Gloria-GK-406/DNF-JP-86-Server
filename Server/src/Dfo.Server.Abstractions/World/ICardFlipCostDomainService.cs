using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

public sealed record CardFlipCostSettlement(int Gold, AccountEntitlementEntryState Entitlements,
    IReadOnlyList<CharacterPremiumUsageSlotState> Usage);

public interface ICardFlipCostDomainService
{
    /// <summary>Settles a reserved flip's price and consumes any free entitlement in the caller's transaction.
    /// This is not a read-only quote. Roll back with the award on failure.</summary>
    ValueTask<CardFlipCostSettlement?> SettleAsync(AccountId accountId, CharacterId characterId,
        ClearRewardCardKind kind, int? basePrice, CancellationToken token);
}
