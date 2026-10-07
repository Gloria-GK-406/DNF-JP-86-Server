using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

public sealed record DungeonEntryMaterialPreparation(IDungeonEntryMaterialPlan? Plan, byte? BlockedSlot = null);

/// <summary>A validated, one-use plan consumed only after the complete party has been admitted.</summary>
public interface IDungeonEntryMaterialPlan
{
    ValueTask<IReadOnlyList<DungeonEntryInventoryChange>> ConsumeAsync(CancellationToken cancellationToken);
}

public interface IDungeonEntryMaterialsDomainService
{
    ValueTask<DungeonEntryMaterialPreparation> PrepareAsync(IReadOnlyList<DungeonRunParticipant> participants,
        IReadOnlyList<DungeonRequiredItem> requirements, CancellationToken cancellationToken);
}
