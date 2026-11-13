using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record OpeningSlotConsumption(BusinessSlot Slot, int ItemId, int Count);
public sealed record OpeningPreparedGrant(OpeningReward Reward, OpeningRewardItemDefinition Item, int Expiration);
public interface IPackageOpeningRuleService
{
    ValueTask<OpeningPackageDefinition> DefinitionAsync(int itemId, OpeningPackageKind kind, CancellationToken cancellationToken);
    ImmutableArray<OpeningReward> Roll(OpeningPackageDefinition definition, IReadOnlyList<int> choices, int seed);
    ValueTask<ImmutableArray<OpeningPreparedGrant>> PrepareAsync(IReadOnlyList<OpeningReward> rewards,
        DateTimeOffset now, CancellationToken cancellationToken);
    int AdvanceLuck(int value);
    bool LuckBonus(int value);
    /// <summary>Empty slots required before drawing; decided from facts known before any draw.</summary>
    ImmutableArray<OpeningRoomRequirement> RoomBeforeDrawing(OpeningPackageDefinition definition);
}

/// <summary>A carried space that must hold at least this many empty slots before an opening draws.</summary>
public sealed record OpeningRoomRequirement(BusinessInventorySpace Space, int EmptySlots);
