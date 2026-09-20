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

        for (var index = 0; index < QuestTrigger.ChannelCount; index++)
        {
            var mask = 0x10 << index;
            if ((selector & mask) == 0)
                continue;
            if (index >= definition.HuntEnemyRequirements.Count)
                return false;

            var target = definition.HuntEnemyRequirements[index];
            if (target.EnemyType != HuntEnemyTargetTypes.PassiveObject
                || (target.DungeonId > 0 && target.DungeonId != run.Layout.DungeonId)
                || (target.Difficulty >= 0 && target.Difficulty != (int)run.Difficulty))
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
