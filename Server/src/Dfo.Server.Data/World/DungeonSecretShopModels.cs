using System.Collections.Immutable;

namespace Dfo.Server.Data.World;

public sealed record DungeonSecretShopItem(int ItemId, int UnitPrice, int CostItemId, int RemainingCount);

public sealed record DungeonSecretShopOffer(int NpcId, ImmutableArray<DungeonSecretShopItem> Items);
