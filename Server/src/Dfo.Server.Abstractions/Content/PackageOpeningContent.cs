using System.Collections.Immutable;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Content;

public enum OpeningPackageKind { Unsupported, MagicBox, Package, AvatarPackage, Selection, Lottery }
public sealed record OpeningWeightedReward(OpeningReward Reward, int Weight);
public sealed record OpeningRewardPool(int Draws, ImmutableArray<OpeningWeightedReward> Choices)
{
    /// <summary>A [charactor] pool's job, in the client's job numbering; null draws for every job.</summary>
    public int? Job { get; init; }
    /// <summary>The two numbers after a [charactor] job name. The client stores them without a known reader;
    /// samples look like a level range (1 70, 1 99). Recorded, not enforced.</summary>
    public (int First, int Second)? JobHeader { get; init; }
}
/// <summary>One self-selection category: every row a [booster select category] block declares for its coordinates.</summary>
public sealed record OpeningSelectionCategory(OpeningCategory Key, ImmutableArray<OpeningReward> Rewards);
public sealed record OpeningPackageDefinition(int ItemId, OpeningPackageKind Kind,
    ImmutableArray<OpeningReward> FixedRewards, ImmutableArray<OpeningRewardPool> Pools,
    int SelectionCount, int MaterialItemId, int MaterialCount, int SourceCount,
    bool UsesSeriaLuck, ImmutableArray<string> Diagnostics)
{
    public int GoldCost { get; init; }
    public ImmutableArray<long> CumulativeWeights { get; init; } = [];
    /// <summary>Self-selection categories; a selection package's choices come from exactly one of them.</summary>
    public ImmutableArray<OpeningSelectionCategory> Categories { get; init; } = [];
    /// <summary>[cera package] / [cera booster]: unpacked wherever it is granted, never held (70 ProcessSpecialItem).</summary>
    public bool ExpandsWhenGranted { get; init; }
    /// <summary>[package data selection] groups. Only a shop purchase carries a choice for them; an inventory
    /// opening cannot, and a grant-time expansion leaves them out as 70 does.</summary>
    public int PurchaseSelectionGroups { get; init; }
}
public sealed record OpeningRewardItemDefinition(int ItemId, ItemPlacement Placement, ushort Durability,
    bool Sealed, ImmutableArray<int> Abilities, int? CreatureStartLevel, bool Supported,
    int DefaultPeriodDays = 0, int AbsoluteExpiration = 0, int PremiumType = 0, TimeSpan PremiumDuration = default)
{
    /// <summary>Period days of each [avatar type select] row, in declaration order; 0 is permanent.</summary>
    public ImmutableArray<int> AvatarTypePeriods { get; init; } = [];
    /// <summary>The shop sells this item's effect as a service; whether a reward may carry it is a rule decision.</summary>
    public bool IsService { get; init; }
    /// <summary>A [cera package] / [cera booster]: granted as its contents, not as an item.</summary>
    public bool ExpandsWhenGranted { get; init; }
}
public interface IPackageOpeningDefinitionSource
{
    ValueTask<OpeningPackageDefinition?> GetAsync(int itemId, CancellationToken cancellationToken);
    ValueTask<OpeningRewardItemDefinition?> GetRewardAsync(int itemId, CancellationToken cancellationToken);
}

/// <summary>Prepares all ordinary-pot definitions before accepting game traffic.</summary>
public interface ILotteryDefinitionCache
{
    ValueTask<int> InitializeAsync(CancellationToken cancellationToken = default);
    OpeningPackageDefinition GetRequired(int itemId);
}
