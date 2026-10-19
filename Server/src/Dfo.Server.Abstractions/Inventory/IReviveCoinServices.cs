using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record DailyReviveCoinResult(bool Granted, bool Claimed, OpeningInventoryReceipt? Receipt = null,
    InventoryItemCoreState? Wallet = null, DateTimeOffset? PreparedAt = null);
public interface IReviveCoinRuleService
{
    bool SupportsConsumable(int itemId);
    DateOnly Period(DateTimeOffset at);
    GrantOpeningVirtualEffect Grant();
    bool CanClaim(int balance, int used);
}
public interface IReviveCoinQueryDomainService
{
    ValueTask<bool> ClaimedAsync(CharacterId character, CancellationToken ct);
}
public interface IReviveCoinDomainService : IReviveCoinQueryDomainService
{
    ValueTask<ItemUseApplication?> TryUseAsync(SelectedSessionIdentity session, BusinessSlot source, ItemReference item, CancellationToken ct);
    ValueTask<DailyReviveCoinResult> ClaimAsync(SelectedSessionIdentity session, CancellationToken ct);
}
