using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Effects;

internal sealed class CharacterExperienceEffectHandler(ICharacterDomainService characters) : EffectHandler<CharacterExperienceEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, CharacterExperienceEffect effect, CancellationToken ct)
    {
        if (effect.Decision.Before.Level == 0 || effect.Decision.Award.Level == 0) throw new ArgumentException("Invalid progress decision.");
        _ = effect.Decision.Award.Breakdown.Total;
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, CharacterExperienceEffect effect, CancellationToken ct)
    {
        var d = effect.Decision;
        if (await characters.Get(context.CharacterId).ProgressAsync(ct).ConfigureAwait(false) != d.Before)
            throw new InvalidOperationException("Decided experience target changed.");
        var award = await characters.Get(context.CharacterId).AddExpAsync(d.Award.Breakdown, d.Before, ct).ConfigureAwait(false);
        return new CharacterExperienceChange(award);
    }
}
internal sealed class SetQuestRewardGoldEffectHandler(ICharacterCarriedGoldTable gold, ICharacterDomainService characters) : EffectHandler<SetQuestRewardGoldEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, SetQuestRewardGoldEffect effect, CancellationToken ct)
    {
        if (effect.Expected < 0 || effect.Value < effect.Expected) throw new ArgumentException("Invalid reward gold decision.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, SetQuestRewardGoldEffect effect, CancellationToken ct)
    {
        if ((await gold.FindAsync(context.CharacterId, ct).ConfigureAwait(false))?.Carried != effect.Expected)
            throw new InvalidOperationException("Decided gold target changed.");
        if (effect.Value != effect.Expected
            && await characters.Get(context.CharacterId).AddGoldAsync(effect.Value - effect.Expected, ct).ConfigureAwait(false) != effect.Value)
            throw new InvalidOperationException("Gold target refused the write.");
        return new QuestRewardGoldChange(effect.Expected, effect.Value);
    }
}
internal sealed class SetCharacterGrowTypeEffectHandler(ICharacterTable characters) : EffectHandler<SetCharacterGrowTypeEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, SetCharacterGrowTypeEffect effect, CancellationToken ct)
    {
        if (effect.Value is < 1 or > 15 || effect.Expected != 0) throw new ArgumentException("Invalid first advancement.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, SetCharacterGrowTypeEffect effect, CancellationToken ct)
    {
        var row = await characters.FindStandingAsync(context.CharacterId, ct).ConfigureAwait(false);
        if (row is null || row.Job != effect.Job || row.GrowType != effect.Expected
            || await characters.SetGrowTypeAsync(context.CharacterId, effect.Expected, effect.Value, ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Decided career target changed.");
        return new CharacterGrowTypeChange(effect.Value);
    }
}
internal sealed class SetCharacterAwakeningEffectHandler(ICharacterTable characters) : EffectHandler<SetCharacterAwakeningEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, SetCharacterAwakeningEffect effect, CancellationToken ct)
    {
        if ((effect.Expected >> 4) != 0 || effect.Value != (effect.Expected | 0x10))
            throw new ArgumentException("Invalid first awakening.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, SetCharacterAwakeningEffect effect, CancellationToken ct)
    {
        var row = await characters.FindStandingAsync(context.CharacterId, ct).ConfigureAwait(false);
        if (row is null || row.Job != effect.Job || row.GrowType != effect.Expected
            || await characters.SetGrowTypeAsync(context.CharacterId, effect.Expected, effect.Value, ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Decided awakening target changed.");
        return new CharacterGrowTypeChange(effect.Value);
    }
}
internal sealed class MaterializeSkillBuildsEffectHandler(ICharacterSkillBuildTable builds) : EffectHandler<MaterializeSkillBuildsEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext context, MaterializeSkillBuildsEffect effect, CancellationToken ct)
    {
        if (effect.Expected.Select(x => x.Id).Distinct().Count() != effect.Expected.Length) throw new ArgumentException("Duplicate skill build.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, MaterializeSkillBuildsEffect effect, CancellationToken ct)
    {
        var current = await builds.ReadAsync(context.CharacterId, ct).ConfigureAwait(false);
        if (current.Active != effect.Active || !current.Builds.SequenceEqual(effect.Expected)) throw new InvalidOperationException("Decided builds changed.");
        await builds.EnsureAsync(context.CharacterId, ct).ConfigureAwait(false);
        return new SkillBuildsMaterializedChange();
    }
}
internal sealed class GrantBasicCharacterItemEffectHandler(IItemDeliveryDomainService delivery,
    IItemPlacementRuleService placements, IItemGrantDomainService resources) : EffectHandler<GrantBasicCharacterItemEffect>
{
    public override async ValueTask ValidateAsync(EffectExecutionContext context, GrantBasicCharacterItemEffect effect, CancellationToken ct)
    {
        if (effect.Count <= 0 || effect.ItemId <= 0
            || await placements.PlacementForAsync(effect.ItemId, ct).ConfigureAwait(false) != effect.Placement)
            throw new InventoryEffectException(InventoryEffectRejection.UnsupportedItem);
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext context, GrantBasicCharacterItemEffect effect, CancellationToken ct)
    {
        if (effect.Placement.Destination.Space == Dfo.Server.Data.Inventory.BusinessInventorySpace.VirtualResource)
            return new BasicCharacterItemChange([.. (await resources.GrantAllPersistentAsync(context.CharacterId, effect.ItemId, effect.Count, ct).ConfigureAwait(false)).Items]);
        var result = await delivery.DeliverAsync(context.AccountId, context.CharacterId,
            [new(effect.ItemId, effect.Count)], "任务奖励", ct, ItemDeliveryMode.InventoryThenMail).ConfigureAwait(false);
        return new BasicCharacterItemChange([.. result.Inventory.Select(x => new ContainerItemAddition(x.Container.Id, x.Slot, x.Count))]);
    }
}
