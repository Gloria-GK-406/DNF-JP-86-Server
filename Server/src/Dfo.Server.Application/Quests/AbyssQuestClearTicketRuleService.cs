using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Quests;

internal sealed class AbyssQuestClearTicketRuleService(IStackableActionDefinitionSource items,
    IQuestContentRuleService quests, IQuestAvailabilityRuleService availability)
{
    internal const int ItemId = 10005122;

    public async ValueTask<IReadOnlyList<int>> SelectAsync(
        int itemId, WorldContentSubject subject, DateTimeOffset now, CancellationToken ct)
    {
        if (itemId != ItemId || await items.GetAsync(itemId, ct).ConfigureAwait(false) is not
            { Action: "[any quest clear]", IsReadable: true, ListedQuestClear: { Count: > 0 } questIds } item)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Not a supported abyss quest-clear ticket.");
        if (subject.Level < item.MinimumLevel || subject.Level > item.MaximumLevel
            || item.ExpiresAtUnixSeconds != 0 && now.ToUnixTimeSeconds() >= item.ExpiresAtUnixSeconds)
            throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Abyss ticket level or expiration limit.");

        var selected = new List<int>();
        foreach (var id in questIds)
        {
            var quest = await quests.FindAsync(id, ct).ConfigureAwait(false);
            if (quest is not { Grade: QuestGrade.Achievement }
                || !quest.Rewards.Any(reward => reward.Kind == QuestRewardKind.HellChallenge))
                throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Invalid abyss quest-clear target.");
            if (!subject.ClearedQuestFlags.ContainsKey(id)
                && (subject.CarriedQuestIds.Contains(id)
                    || await availability.MeetsRequirementsAsync(quest, subject, ct).ConfigureAwait(false)))
                selected.Add(id);
        }
        return selected;
    }
}
