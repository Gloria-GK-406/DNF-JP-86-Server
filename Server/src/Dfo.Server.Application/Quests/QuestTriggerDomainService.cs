using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Quests;

/// <summary>
/// Applies client-counted progress and binds passive-object hunt reports to the exact quest-bound run
/// that made them possible.
/// </summary>
internal sealed class QuestTriggerDomainService(
    ICharacterQuestLogDomainService logs,
    IDungeonRunQueryDomainService runs,
    IDungeonStructureRuleService structures) : IQuestTriggerDomainService
{
    public async ValueTask<QuestAdvance> ApplyClientReportAsync(
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
            && !await MatchesCurrentQuestRunAsync(characterId, definition, selector, cancellationToken).ConfigureAwait(false))
        {
            return QuestAdvance.Refused;
        }

        return await logs.LogOf(accountId, characterId)
            .AdvanceAsync(definition, selector, increment, cancellationToken).ConfigureAwait(false);
    }

    /// <remarks>
    /// The run that makes a passive-object report possible is the quest's own maze when the dungeon
    /// connects one to the quest -- quest 1776's object exists only in its story maze of 144, so an
    /// ordinary 144 run reporting it is refused. A dungeon connecting no maze to the quest holds the
    /// objects in its ordinary play (13503 rescues its residents in 2010's only maze), so standing in
    /// that named dungeon is the binding there.
    /// </remarks>
    private async ValueTask<bool> MatchesCurrentQuestRunAsync(
        CharacterId characterId,
        QuestDefinition definition,
        byte selector,
        CancellationToken cancellationToken)
    {
        var run = runs.Find(characterId);
        if (run is null)
            return false;
        var questBound = run.Layout.Purpose.QuestId == definition.QuestId
            && run.Layout.Purpose.QuestState == DungeonQuestConnectionState.Active;
        if (!questBound)
        {
            var dungeon = await structures.GetDefinitionAsync(run.Layout.DungeonId, cancellationToken).ConfigureAwait(false);
            if (dungeon is null || dungeon.QuestConnection?.QuestId == definition.QuestId
                || dungeon.Mazes.Any(maze => maze.QuestConnection?.QuestId == definition.QuestId))
                return false;
        }

        var targets = QuestKillObjectiveRules.Select(definition.KillObjectives, selector);
        if (targets is null)
            return false;
        foreach (var target in targets)
        {
            var inScope = target.Scope.Kind switch
            {
                QuestKillScopeKind.Dungeon => target.Scope.Id == run.Layout.DungeonId,
                // Valid in any dungeon: only the quest's own maze binds it.
                QuestKillScopeKind.AnyDungeon => questBound,
                _ => false,
            };
            if (target.Target.EnemyType != HuntEnemyTargetTypes.PassiveObject
                || !inScope
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
