using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record OpeningSlotConsumption(BusinessSlot Slot, int ItemId, int Count);
public sealed record OpeningPreparedGrant(OpeningReward Reward, OpeningRewardItemDefinition Item, int Expiration);
public interface IPackageOpeningRuleService
{
    ValueTask<OpeningPackageDefinition> DefinitionAsync(int itemId, OpeningPackageKind kind, CancellationToken cancellationToken);
    ImmutableArray<OpeningReward> Roll(OpeningPackageDefinition definition, OpeningDraw draw, int seed);
    /// <summary>Replaces every [cera package] / [cera booster] among drawn rewards with its own contents, recursively.</summary>
    ValueTask<ImmutableArray<OpeningReward>> ExpandAsync(IReadOnlyList<OpeningReward> rewards, int job, int seed, CancellationToken cancellationToken);
    ValueTask<ImmutableArray<OpeningPreparedGrant>> PrepareAsync(IReadOnlyList<OpeningReward> rewards,
        DateTimeOffset now, CancellationToken cancellationToken);
    int AdvanceLuck(int value);
    bool LuckBonus(int value);
    /// <summary>Empty slots required before drawing; decided from facts known before any draw.</summary>
    ImmutableArray<OpeningRoomRequirement> RoomBeforeDrawing(OpeningPackageDefinition definition);
}

/// <summary>
/// Whether a service item (one the shop sells as an effect) may reach a player as an ordinary item when a
/// package or a shop bundle contains it. It may when a client entry uses it from the inventory: the cargo and
/// inventory expansion kits (0x0198, 0x01AF). Other service items stay refused until their effect is applied
/// at grant time. Shared by package opening and shop bundle expansion.
/// </summary>
public interface IServiceItemRewardRuleService
{
    ValueTask<bool> GrantsAsItemAsync(int itemId, CancellationToken cancellationToken);
}

/// <summary>What the opener supplies to a draw: the selection window's category and IDs, and the character's job.</summary>
public sealed record OpeningDraw(IReadOnlyList<int> Choices, OpeningCategory Category, int Job)
{
    /// <summary>A draw with no selection and no job-specific pool, as magic boxes and seria pools make.</summary>
    public static OpeningDraw Plain { get; } = new([], default, -1);
}

/// <summary>A carried space that must hold at least this many empty slots before an opening draws.</summary>
public sealed record OpeningRoomRequirement(BusinessInventorySpace Space, int EmptySlots);
