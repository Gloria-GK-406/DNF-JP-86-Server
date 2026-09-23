using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Quests;

/// <summary>Catch-up policy for the verified no-reward story coupon, not an official-server algorithm.</summary>
internal sealed class QuestClearCouponRuleService(
    IStackableActionDefinitionSource items, IQuestContentRuleService quests) : IQuestClearCouponRuleService
{
    public async ValueTask<IReadOnlyList<int>> SelectAsync(int itemId, byte level, byte job,
        byte growType, IReadOnlyList<ClearedQuest> cleared, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Other archives use the same action for different grades and reward policies.
        var item = await items.GetAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (itemId != 10_006_007 || item is not { IsReadable: true, Action: "[quest clear]" })
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Unsupported quest-clear action or coupon.");
        if (level < Math.Max(1, item.MinimumLevel) || level > item.MaximumLevel
            || item.ExpiresAtUnixSeconds != 0 && now.ToUnixTimeSeconds() >= item.ExpiresAtUnixSeconds)
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Coupon level or expiration limit.");
        return Select(await quests.AllAsync(cancellationToken).ConfigureAwait(false), level, job, growType, cleared);
    }

    internal static IReadOnlyList<int> Select(IReadOnlyList<QuestDefinition> definitions,
        byte level, byte job, byte growType, IReadOnlyList<ClearedQuest> cleared)
    {
        var catalog = definitions.ToDictionary(q => q.QuestId);
        var marks = cleared.Where(q => q.Mark != 0).ToDictionary(q => q.QuestId, q => q.Mark);
        var family = CharacterJobFamilies.Resolve(job);
        var selected = new HashSet<int>();
        foreach (var root in definitions.Where(q => q.Grade is QuestGrade.Epic or QuestGrade.Normal)
                     .OrderBy(q => q.QuestId))
        {
            var candidate = new HashSet<int>(selected);
            if (Include(root.QuestId, candidate, [])) selected = candidate;
        }
        return selected.Order().ToArray();

        bool Include(int id, HashSet<int> candidate, HashSet<int> visiting)
        {
            if (marks.ContainsKey(id) || candidate.Contains(id)) return true;
            if (!catalog.TryGetValue(id, out var quest) || !Safe(quest) || !visiting.Add(id)) return false;
            // OR groups are alternatives; never union unrelated or mutually exclusive branches.
            if (quest.CompletedQuestPrerequisiteGroups.Count > 0)
            {
                HashSet<int>? viable = null;
                foreach (var group in quest.CompletedQuestPrerequisiteGroups
                             .OrderBy(g => g.Count(q => !marks.ContainsKey(q) && !candidate.Contains(q))))
                {
                    if (group.Count == 0) continue;
                    var branch = new HashSet<int>(candidate);
                    if (!group.All(q => Include(q, branch, new HashSet<int>(visiting)))) continue;
                    viable = branch;
                    break;
                }
                if (viable is null) return false;
                candidate.UnionWith(viable);
            }
            candidate.Add(id);
            return true;
        }

        bool Safe(QuestDefinition quest) => quest.QuestId is > 0 and < 30_000
            && quest.Grade is (QuestGrade.Epic or QuestGrade.Normal or QuestGrade.Side or QuestGrade.CommonUnique)
            && quest.RequiredLevel is { Minimum: >= 0 } range && range.Minimum <= level
            // Historical prerequisites remain eligible after their old offer-level ceiling.
            && range.Maximum >= range.Minimum
            && quest.IsExposedByNpc != false && quest.IsEventOnly != true
            && quest.JobChangeQuestCode is null or 0 && !quest.HasSpecialRewardStatus
            && quest.Rewards.All(r => r.Kind == QuestRewardKind.Item)
            && quest.ExpertJobRequirement is null && quest.CreatureKindRequirement is null
            && quest.CreatureLevelRequirement is null
            && quest.ObjectiveTypeCode is not ("question" or "[question]")
            && quest.AccountCollisionQuestIds.Count == 0 && quest.CollisionQuestIds.Count == 0
            && quest.RequiredQuestAnswers.All(a => marks.TryGetValue(a.QuestId, out var mark) && mark == a.AnswerIndex + 1)
            && (quest.JobRestriction is null || quest.JobRestriction.IsValid
                && (quest.JobRestriction.IsExactAll || quest.JobRestriction.JobFamilies.Contains(family)))
            && (quest.TargetCharacterRestriction is null || quest.TargetCharacterRestriction.IsValid
                && quest.TargetCharacterRestriction.JobFamilies.Contains(family))
            && (quest.GrowTypeRequirement is null || quest.GrowTypeRequirement == growType);
    }
}
