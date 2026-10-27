using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

public sealed record GiantDragonOptions
{
    public bool Enabled { get; init; } = true;
    public bool RestoredVariantsEnabled { get; init; }
}

public sealed record GiantDragonEntryRule(DungeonDefinition Dungeon, int TownId, int AreaId,
    int? QuestId, bool AllowActiveQuest);

public interface IGiantDragonRuleService
{
    bool Supports(int dungeonId);
    bool IsOpen(int dungeonId);
    ValueTask<GiantDragonEntryRule> EntryAsync(int dungeonId, CancellationToken ct);
}

public interface IGiantDragonDomainService
{
    bool Supports(int dungeonId);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
}
