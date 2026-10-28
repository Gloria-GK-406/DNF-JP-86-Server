using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

public sealed record GiantDragonOptions
{
    public bool Enabled { get; init; } = true;
    public bool RestoredVariantsEnabled { get; init; }
}

public enum GiantDragonQuestRequirement : byte { None, Cleared, Active, ActiveOrCleared }

public sealed record GiantDragonEntryRule(DungeonDefinition Dungeon, int TownId, int AreaId,
    int? QuestId, GiantDragonQuestRequirement QuestRequirement)
{
    public GiantDragonTimeRewardPlan? TimeReward { get; init; }
    public DungeonEntryIntent? IntentOverride { get; init; }

    public bool AcceptsQuest(bool active, bool cleared) => QuestRequirement switch
    {
        GiantDragonQuestRequirement.None => true,
        GiantDragonQuestRequirement.Cleared => cleared,
        GiantDragonQuestRequirement.Active => active,
        GiantDragonQuestRequirement.ActiveOrCleared => active || cleared,
        _ => throw new InvalidOperationException("Unknown giant dragon quest requirement."),
    };
}

/// <summary>Immutable local reward policy and material metadata frozen by ordinary dragon admission.</summary>
public sealed record GiantDragonTimeRewardPlan(RolledDrop Material, int MaximumCount, int MinimumCount,
    TimeSpan FirstDecreaseAt, TimeSpan StepPeriod);

public interface IGiantDragonRuleService
{
    bool Supports(int dungeonId);
    bool IsOpen(int dungeonId);
    ValueTask<GiantDragonEntryRule> EntryAsync(int dungeonId, CancellationToken ct);
    IReadOnlyList<RolledDrop> ResolveTimeReward(GiantDragonTimeRewardPlan plan, TimeSpan elapsed,
        IReadOnlyList<RolledDrop> pending);
}

public interface IGiantDragonDomainService
{
    bool Supports(int dungeonId);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
}
