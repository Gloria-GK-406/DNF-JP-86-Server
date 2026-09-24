namespace Dfo.Server.Abstractions.World;

/// <summary>Applies one party-wide quest-material bonus before the cell's loot is published.</summary>
public interface IQuestHelperGroundBonusDomainService
{
    ValueTask<DungeonCellSeed> ApplyAsync(DungeonCellCreationContext context,
        DungeonCellSeed cell, CancellationToken cancellationToken);
}
