using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record MonsterCardUpgradeDefinition(int RateMultiplier, int GoldCost);
public sealed record MonsterCardUpgradeItem(int Rarity, IReadOnlyList<byte> Ranks);

public interface IMonsterCardUpgradeDefinitionSource
{
    ValueTask<MonsterCardUpgradeDefinition?> GetUpgradeDefinitionAsync(CancellationToken ct);
    ValueTask<MonsterCardUpgradeItem?> GetCardAsync(int itemId, CancellationToken ct);
}

public enum MonsterCardUpgradeRejection { None, InvalidRequest, InsufficientGold, InventoryFull }

public sealed record MonsterCardUpgradeReceipt(int ItemId, byte Rank, bool Succeeded, DateTimeOffset PreparedAt);
public sealed record MonsterCardUpgradeResult(MonsterCardUpgradeRejection Rejection, MonsterCardUpgradeReceipt? Receipt = null);

public interface IMonsterCardUpgradeDomainService
{
    ValueTask<MonsterCardUpgradeResult> UpgradeAsync(SelectedSessionIdentity session, BusinessSlot target,
        BusinessSlot material, CancellationToken ct);
}
