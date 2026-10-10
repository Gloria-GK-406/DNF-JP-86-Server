using Dfo.Server.Data.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Abstractions.Quests;

/// <summary>A cause of quest progress, reported by the service that owns the underlying fact.</summary>
public abstract record QuestProgressFact;

/// <summary>An untrusted client request. Only client-counted objectives may change from it.</summary>
public sealed record ClientQuestTriggerReport(int QuestId, byte Selector, bool Increment)
    : QuestProgressFact;

/// <summary>An actor death already claimed and verified by the dungeon run.</summary>
public sealed record QuestEnemyDefeated(
    int DungeonId, DungeonDifficulty Difficulty, int EnemyCode, int EnemyType,
    MonsterRank? Rank = null,
    int? WorldMapId = null,
    bool? IsRecommendedDungeon = null) : QuestProgressFact;

/// <summary>The run opened its clear after the boss room was verified empty.</summary>
public sealed record QuestDungeonCleared(int DungeonId, DungeonDifficulty Difficulty) : QuestProgressFact;

/// <summary>The authoritative inventory or currency balance changed; item goals are re-read.</summary>
public sealed record QuestHoldingsChanged : QuestProgressFact;

/// <summary>A quest hand-in was committed in the character's quest log.</summary>
public sealed record QuestCleared(int QuestId) : QuestProgressFact;

public sealed record QuestProgressChange(int QuestId, uint Before, uint After);

/// <summary>
/// A client report receives its CMD result; server facts yield one post-change notification
/// snapshot. These projections are mutually exclusive for one report call.
/// </summary>
public sealed record QuestProgressReport(
    QuestAdvance? ClientResponse,
    IReadOnlyList<QuestProgressChange> Changes,
    QuestProgressSnapshot? NotificationSnapshot);

/// <summary>The single entry point used by business flows to report quest-relevant facts.</summary>
public interface IQuestProgressDomainService
{
    ValueTask<QuestProgressReport> ReportAsync(
        AccountId accountId,
        CharacterId characterId,
        IReadOnlyList<QuestProgressFact> facts,
        CancellationToken cancellationToken);
}
