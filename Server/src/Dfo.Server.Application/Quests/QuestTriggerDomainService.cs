using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Quests;

/// <summary>
/// Applies client-counted progress and binds passive-object hunt reports to the exact story run
/// that made them possible.
/// </summary>
internal sealed class QuestTriggerDomainService(
    ICharacterQuestLogDomainService logs,
    IDungeonRunDomainService runs) : IQuestTriggerDomainService
{
    public ValueTask<QuestAdvance> ApplyClientReportAsync(
        AccountId accountId,
        CharacterId characterId,
        QuestDefinition definition,
        byte selector,
        bool increment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var authority = QuestObjectives.AuthorityOver(definition, selector);
        if (authority == QuestObjectiveAuthority.Client
            && IsHuntEnemy(definition)
            && !MatchesCurrentStoryRun(characterId, definition, selector))
        {
            return ValueTask.FromResult(QuestAdvance.Refused);
        }

        return logs.LogOf(accountId, characterId)
            .AdvanceAsync(definition, selector, increment, cancellationToken);
    }

    private bool MatchesCurrentStoryRun(
        CharacterId characterId,
        QuestDefinition definition,
        byte selector)
    {
        var run = runs.Find(characterId);
        if (run is null
            || run.Layout.Purpose.Kind != DungeonRunPurposeKind.QuestLinked
            || run.Layout.Purpose.QuestId != definition.QuestId
            || run.Layout.Purpose.QuestState != DungeonQuestConnectionState.Active)
        {
            return false;
        }

        var targets = QuestKillObjectiveRules.Select(definition.KillObjectives, selector);
        if (targets is null)
            return false;
        foreach (var target in targets)
        {
            if (target.Target.EnemyType != HuntEnemyTargetTypes.PassiveObject
                || (target.Scope.Kind == QuestKillScopeKind.Dungeon && target.Scope.Id != run.Layout.DungeonId)
                || target.Scope.Kind is not (QuestKillScopeKind.Dungeon or QuestKillScopeKind.AnyDungeon)
                || !QuestKillObjectiveRules.MatchesDifficulty(target, (int)run.Difficulty))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsHuntEnemy(QuestDefinition definition) =>
        string.Equals(
            definition.ObjectiveTypeCode.Trim(),
            "hunt enemy",
            StringComparison.OrdinalIgnoreCase);
}
