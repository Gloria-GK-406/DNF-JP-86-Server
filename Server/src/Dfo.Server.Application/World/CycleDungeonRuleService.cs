using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class CycleDungeonRuleService(ICycleDungeonDefinitionSource definitions,
    IQuestContentRuleService quests, IWorldContentEntryRuleService offers) : ICycleDungeonRuleService
{
    public async ValueTask<CycleDungeonRegion?> EligibleAsync(int dungeonId, int questId, WorldContentSubject subject,
        CancellationToken cancellationToken)
    {
        var region = definitions.Load().SingleOrDefault(x => x.Candidates.Contains(new(dungeonId, questId)));
        if (region is null || !subject.ClearedQuestFlags.ContainsKey(region.OpenQuestId)) return null;
        var quest = await quests.FindAsync(questId, cancellationToken).ConfigureAwait(false);
        if (quest?.Grade != QuestGrade.Circle || !quest.Rewards.Any(x => x.Kind == QuestRewardKind.CircleDungeon)) return null;
        // Circle quests are not normal NPC offers. A retry may already carry the same quest;
        // its declared prerequisites still apply, independently of the recommendation list.
        return await offers.MeetsQuestRequirementsAsync(quest, subject, cancellationToken).ConfigureAwait(false) ? region : null;
    }

    public IReadOnlyList<QuestRewardItemGrant> RewardsForQuest(int questId) =>
        definitions.Load().SingleOrDefault(x => x.Candidates.Any(pair => pair.QuestId == questId))?.Rewards ?? [];
}
