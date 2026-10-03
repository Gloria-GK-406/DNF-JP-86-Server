using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Quests;

internal sealed class QuestCompletionDomainService(ICharacterDomainService characters, ICharacterTable rows,
    ICharacterContainerDomainService containers, IConsumableStateDomainService state,
    ICharacterSkillStateDomainService skills, IQuestCompletionEffectRuleService rules,
    IInventoryEffectPreflightDomainService preview, IApplyEffectsDomainService apply,
    ICharacterCarriedGoldTable gold) : IQuestCompletionDomainService
{
    public async ValueTask<QuestCompletionReceipt> FinishAsync(AccountId account, CharacterId id, ushort quest,
        ushort selection, ushort count, DateTimeOffset at, CancellationToken ct, SelectedSessionIdentity? session = null)
    {
        QuestCompletionReceipt Refuse(QuestProgressSnapshot? projection = null) => new(false, quest, null, null, null, projection, null);
        var definition = await rules.DefinitionAsync(quest, ct).ConfigureAwait(false);
        if (definition is null || await rows.FindOwnerAsync(id, ct).ConfigureAwait(false) != account) return Refuse();
        var character = characters.Get(id);
        var identity = await character.IdentityAsync(ct).ConfigureAwait(false);
        var progress = await character.ProgressAsync(ct).ConfigureAwait(false);
        var bag = containers.FindContainer(id, InventoryContainerType.Main);
        var heldGold = await gold.FindAsync(id, ct).ConfigureAwait(false);
        if (identity is null || progress is null || bag is null || heldGold is null) return Refuse();
        var quests = await state.QuestsAsync(account, id, ct).ConfigureAwait(false);
        var main = await bag.ReadAllAsync(ct).ConfigureAwait(false);
        var facts = new QuestCompletionFacts(identity, progress, quests, main, heldGold, null, null);
        var effective = await rules.EffectiveAsync(quests, main, heldGold.Carried, ct).ConfigureAwait(false);
        var projected = effective.Any(x => quests.Carried.Single(y => y.Slot == x.Slot).TriggerValue != x.TriggerValue)
            ? new QuestProgressSnapshot(effective) : null;
        if (effective.FirstOrDefault(x => x.QuestId == quest) is not { TriggerValue: 0 }) return Refuse(projected);
        var reward = rules.Rewards(definition, facts, selection, count);
        if (!reward.IsValid) return Refuse(projected);
        if (reward.Plan!.GrowType is not null)
            facts = facts with { Skills = await skills.ReadAsync(id, ct).ConfigureAwait(false), Builds = await skills.BuildsAsync(id, ct).ConfigureAwait(false) };
        var rewards = await rules.RewardsAsync(reward.Plan, facts, ct).ConfigureAwait(false);
        var context = new EffectExecutionContext(account, id, at, session);
        var inventory = await preview.PreviewWithHoldingsAsync(context, rewards, ct).ConfigureAwait(false);
        var goldEntry = rewards.OfType<SetQuestRewardGoldEffect>().Any(x => x.Value > x.Expected) ? 1 : 0;
        if (inventory.Rejection != InventoryEffectRejection.None
            || inventory.RewardEntries + goldEntry > byte.MaxValue)
            return Refuse(projected);
        var plan = await rules.CompleteAsync(quest, reward.Plan, facts, rewards, inventory.Holdings!, ct).ConfigureAwait(false);
        var applied = await apply.ApplyAsync(context, plan.Effects, ct).ConfigureAwait(false);
        return new(true, quest, definition.ObjectiveTypeCode, plan.Award, applied, plan.Projection, plan.GrowType);
    }
}
