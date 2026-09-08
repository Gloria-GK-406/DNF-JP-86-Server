namespace Dfo.Server.Abstractions.Content;

public sealed record NpcShopPage(int? Job, IReadOnlyList<int> ItemIds);

public sealed record NpcShopDefinition(int NpcId, int ShopId, IReadOnlyList<NpcShopPage> Pages);

public sealed record NpcShopMaterialCost(int ItemId, int Count);

public enum NpcShopLimitScope { Account, Character }

public sealed record NpcShopPurchaseLimit(NpcShopLimitScope Scope, int Count, bool ResetsDaily);

public sealed record NpcShopItemDefinition(
    int ItemId,
    int? GoldPrice,
    int AddPrice,
    IReadOnlyList<NpcShopMaterialCost> Materials,
    NpcShopPurchaseLimit? Limit,
    ushort Durability,
    bool Sealed,
    bool IsReadable);

public interface INpcShopDefinitionSource
{
    ValueTask<NpcShopDefinition?> GetShopAsync(int npcId, CancellationToken cancellationToken);
    ValueTask<NpcShopItemDefinition?> GetItemAsync(int itemId, CancellationToken cancellationToken);
}
