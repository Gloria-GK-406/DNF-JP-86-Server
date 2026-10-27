using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EntryAllowanceItemRuleService(IStackableActionDefinitionSource actions) : IEntryAllowanceItemRuleService
{
    public bool Supports(int itemId) => itemId == 490002847;
    public ValueTask<StackableActionDefinition?> DefinitionAsync(int itemId, CancellationToken ct) =>
        Supports(itemId) ? actions.GetAsync(itemId, ct) : ValueTask.FromResult<StackableActionDefinition?>(null);
}
internal sealed class EntryAllowanceItemDomainService(IEntryAllowanceItemRuleService definitions,
    IItemUseEffectRuleService uses, ICharacterDomainService characters, IHerasDungeonDomainService heras,
    IOpeningInventoryDomainService inventory, TimeProvider clock) : IEntryAllowanceItemDomainService
{
    public bool Supports(int itemId) => definitions.Supports(itemId);

    public async ValueTask<ItemUseApplication?> TryUseAsync(SelectedSessionIdentity session, BusinessSlot source, ItemReference item, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        if (!definitions.Supports(item.ItemId)) return null;
        ItemUseApplication Refuse(ItemUsability status) => new(status, item.ItemId, item.Count);
        var definition = await definitions.DefinitionAsync(item.ItemId, ct).ConfigureAwait(false);
        if (definition?.Action != "[add enter limit dungeonincount]") return Refuse(ItemUsability.NotAUsableItem);
        if (!definition.IsReadable || !heras.Supports(definition.EntryDungeonId) || definition.EntryIncrease != 1)
            return Refuse(ItemUsability.NotAUsableItem);
        var rule = await uses.RuleForAsync(item.ItemId, ct).ConfigureAwait(false);
        var at = clock.GetUtcNow();
        var facts = rule is null || !rule.IsReadable ? null : await characters.Get(session.CharacterId)
            .ItemUseFactsAsync(uses.CooldownKeyFor(item.ItemId, rule), ct).ConfigureAwait(false);
        var plan = uses.Build(rule, source, item, facts, null, at);
        if (plan.Status != ItemUsability.Allowed) return Refuse(plan.Status);
        if (plan.Effects.OfType<ConsumeFromContainerEffect>().Count(x => x.Slot == source && x.ExpectedItemId == item.ItemId && x.Count == 1) != 1)
            return Refuse(ItemUsability.NotAUsableItem);
        var progress = await characters.Get(session.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
        if (progress is null) return Refuse(ItemUsability.CharacterStateUnavailable);
        if (progress.Level < definition.MinimumLevel) return Refuse(ItemUsability.LevelTooLow);
        if (progress.Level > definition.MaximumLevel || definition.ExpiresAtUnixSeconds > 0 && definition.ExpiresAtUnixSeconds <= at.ToUnixTimeSeconds())
            return Refuse(ItemUsability.NotAUsableItem);
        var quota = await heras.ReadQuotaAtAsync(session.CharacterId, at, ct).ConfigureAwait(false);
        if (quota.Remaining >= byte.MaxValue) return Refuse(ItemUsability.NotAUsableItem);
        var settlement = await inventory.ApplyAsync(session,
            plan.Effects.Append(new AddDungeonEntryAllowanceEffect(definition.EntryDungeonId, 1, quota.Period, quota.Bonus, quota.Used)).ToArray(), at, footprint, ct).ConfigureAwait(false);
        var applied = settlement.Applied;
        var remaining = applied.Changes<ContainerConsumptionChange>().LastOrDefault(x => x.Slot == source)?.RemainingCount ?? item.Count;
        return new(ItemUsability.Allowed, item.ItemId, remaining, applied) { OpeningReceipt = settlement.Inventory };
    }
}
