using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public sealed record MirrorDungeonCandidate(int DungeonId, int QuestId);
public sealed record MirrorDungeonRewardBand(int MinimumLevel, int MaximumLevel,
    IReadOnlyList<QuestRewardItemGrant> Items);
public sealed record MirrorDailyRewardDefinition(int RequiredClears, int ItemId, int Count);
public sealed record MirrorDungeonCatalog(int WrapperDungeonId, int WorldMapId,
    byte EntryTownId, byte EntryAreaId, byte MinimumLevel, int DailyEntryLimit,
    int DailyClearLimit, MirrorDailyRewardDefinition DailyReward,
    IReadOnlyList<MirrorDungeonRewardBand> Rewards, IReadOnlyList<MirrorDungeonCandidate> Candidates)
{
    public IReadOnlyList<int> RequiredQuestIds { get; init; } = [];
}
public sealed record MirrorDungeonRunIdentity(int WrapperDungeonId, int WorldMapId, int QuestId);
public sealed record MirrorDungeonEntryRule(MirrorDungeonCatalog Catalog, DungeonDifficulty Difficulty);

public interface IMirrorDungeonDefinitionSource
{
    MirrorDungeonCatalog Load();
}

public interface IMirrorDungeonRuleService
{
    MirrorDungeonCatalog Catalog { get; }
    ValueTask<MirrorDungeonEntryRule?> ResolveAsync(int dungeonId, int questId, CancellationToken cancellationToken);
}

public interface IMirrorDungeonEntryDomainService
{
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken cancellationToken);
}

/// <summary>Reserve all participants' daily entries atomically in the current entry transaction.</summary>
public interface IMirrorDungeonQuotaDomainService
{
    ValueTask<byte?> FindBlockedSlotAsync(IReadOnlyList<DungeonRunParticipant> participants,
        int dailyLimit, CancellationToken cancellationToken);
    ValueTask<byte?> TryReserveEntryAsync(IReadOnlyList<DungeonRunParticipant> participants,
        int dailyLimit, CancellationToken cancellationToken);
}
