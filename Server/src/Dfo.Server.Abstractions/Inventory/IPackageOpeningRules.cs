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
}
