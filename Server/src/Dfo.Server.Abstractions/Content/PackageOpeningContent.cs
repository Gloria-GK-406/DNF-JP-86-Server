using System.Collections.Immutable;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Content;

public enum OpeningPackageKind { Unsupported, MagicBox, Package, AvatarPackage, Selection }
public sealed record OpeningWeightedReward(OpeningReward Reward, int Weight);
public sealed record OpeningRewardPool(int Draws, ImmutableArray<OpeningWeightedReward> Choices);
public sealed record OpeningPackageDefinition(int ItemId, OpeningPackageKind Kind,
    ImmutableArray<OpeningReward> FixedRewards, ImmutableArray<OpeningRewardPool> Pools,
    int SelectionCount, int MaterialItemId, int MaterialCount, int SourceCount,
    bool UsesSeriaLuck, ImmutableArray<string> Diagnostics);
public sealed record OpeningRewardItemDefinition(int ItemId, ItemPlacement Placement, ushort Durability,
    bool Sealed, ImmutableArray<int> Abilities, int? CreatureStartLevel, bool Supported,
    int DefaultPeriodDays = 0, int AbsoluteExpiration = 0, int PremiumType = 0, TimeSpan PremiumDuration = default);
public interface IPackageOpeningDefinitionSource
{
    ValueTask<OpeningPackageDefinition?> GetAsync(int itemId, CancellationToken cancellationToken);
    ValueTask<OpeningRewardItemDefinition?> GetRewardAsync(int itemId, CancellationToken cancellationToken);
}
