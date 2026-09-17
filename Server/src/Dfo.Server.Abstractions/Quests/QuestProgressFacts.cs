using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Quests;

/// <summary>A cause of quest progress, reported by the service that owns the underlying fact.</summary>
public abstract record QuestProgressFact;

/// <summary>An untrusted client request. Only client-counted objectives may change from it.</summary>
public sealed record ClientQuestTriggerReport(int QuestId, byte Selector, bool Increment)
    : QuestProgressFact;

/// <summary>An actor death already claimed and verified by the dungeon run.</summary>
public sealed record QuestEnemyDefeated(
    int DungeonId, byte Difficulty, int EnemyCode, int EnemyType) : QuestProgressFact;

/// <summary>The authoritative inventory or currency balance changed; item goals are re-read.</summary>
public sealed record QuestHoldingsChanged : QuestProgressFact;

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
