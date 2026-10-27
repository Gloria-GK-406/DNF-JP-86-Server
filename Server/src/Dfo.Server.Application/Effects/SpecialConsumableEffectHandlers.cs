using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Effects;

namespace Dfo.Server.Application.Effects;

internal sealed class SetFatigueStateEffectHandler(IConsumableStateDomainService states) : EffectHandler<SetFatigueStateEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext c, SetFatigueStateEffect e, CancellationToken ct)
    {
        if (e.Value.Used < 0 || e.Value.ConsumedToday < 0) throw new InvalidOperationException("Invalid fatigue state.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext c, SetFatigueStateEffect e, CancellationToken ct)
    {
        c.Footprint.DemandEffects(c.Owner); await states.SetFatigueAsync(c.CharacterId, e, ct).ConfigureAwait(false); return new FatigueStateChange(e.Value); }
}
internal sealed class SetItemUsageStateEffectHandler(IConsumableStateDomainService states) : EffectHandler<SetItemUsageStateEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext c, SetItemUsageStateEffect e, CancellationToken ct)
    {
        if (e.ItemId <= 0 || e.Value.UsedCount < 0 || e.Value.LastUsedAt is null) throw new InvalidOperationException("Invalid usage state.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext c, SetItemUsageStateEffect e, CancellationToken ct)
    {
        c.Footprint.DemandEffects(c.Owner); await states.SetUsageAsync(c.CharacterId, e, ct).ConfigureAwait(false); return new ItemUsageStateChange(e.ItemId, e.Value); }
}
internal sealed class SetCarriedQuestEffectHandler(IConsumableStateDomainService states) : EffectHandler<SetCarriedQuestEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext c, SetCarriedQuestEffect e, CancellationToken ct)
    {
        if (e.Expected.Slot < 0 || e.Expected.QuestId <= 0 || e.Value is { } value &&
            (value.Slot != e.Expected.Slot || value.QuestId != e.Expected.QuestId)) throw new InvalidOperationException("Invalid quest row.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext c, SetCarriedQuestEffect e, CancellationToken ct)
    {
        c.Footprint.DemandEffects(c.Owner); await states.SetCarriedAsync(c.AccountId, c.CharacterId, e, ct).ConfigureAwait(false); return new QuestRowChange(e.Expected.QuestId); }
}
internal sealed class RemoveTrackedQuestEffectHandler(IConsumableStateDomainService states) : EffectHandler<RemoveTrackedQuestEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext c, RemoveTrackedQuestEffect e, CancellationToken ct)
    {
        if (e.Expected.SlotIndex < 0 || e.Expected.QuestId <= 0) throw new InvalidOperationException("Invalid tracking row.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext c, RemoveTrackedQuestEffect e, CancellationToken ct)
    {
        c.Footprint.DemandEffects(c.Owner); await states.RemoveTrackedAsync(c.AccountId, c.CharacterId, e, ct).ConfigureAwait(false); return new QuestRowChange(e.Expected.QuestId); }
}
internal sealed class SetClearedQuestEffectHandler(IConsumableStateDomainService states) : EffectHandler<SetClearedQuestEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext c, SetClearedQuestEffect e, CancellationToken ct)
    {
        if (e.QuestId <= 0 || e.Mark == 0) throw new InvalidOperationException("Invalid cleared row.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext c, SetClearedQuestEffect e, CancellationToken ct)
    {
        c.Footprint.DemandEffects(c.Owner); await states.SetClearedAsync(c.AccountId, c.CharacterId, e, ct).ConfigureAwait(false); return new QuestRowChange(e.QuestId); }
}
internal sealed class SetLearnedSkillEffectHandler(IConsumableStateDomainService states) : EffectHandler<SetLearnedSkillEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext c, SetLearnedSkillEffect e, CancellationToken ct)
    {
        if (e.Page < 0 || e.Slot is < 0 or >= byte.MaxValue || e.SkillId <= 0 || e.Level == 0
            || e.ExpectedLevel is null && e.Level is null) throw new InvalidOperationException("Invalid skill row.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext c, SetLearnedSkillEffect e, CancellationToken ct)
    {
        c.Footprint.DemandEffects(c.Owner); await states.SetSkillAsync(c.CharacterId, e, ct).ConfigureAwait(false); return new LearnedSkillChange(e.Page, e.Slot, e.SkillId, e.Level); }
}
internal sealed class RenamePetEffectHandler(ICreatureInstanceDomainService creatures) : EffectHandler<RenamePetEffect>
{
    public override ValueTask ValidateAsync(EffectExecutionContext c, RenamePetEffect e, CancellationToken ct)
    {
        if (e.ItemUid <= 0 || e.ExpectedTemplateId <= 0 || e.Name.IsDefault || e.Name.Length > 12
            || e.Name.Any(x => x is 0 or < 0x20 or 0x7F)) throw new InvalidOperationException("Invalid pet name.");
        return ValueTask.CompletedTask;
    }
    public override async ValueTask<EffectChange> ApplyAsync(EffectExecutionContext c, RenamePetEffect e, CancellationToken ct)
    {
        c.Footprint.DemandEffects(c.Owner);
        var collection = creatures.Of(c.CharacterId);
        var current = await collection.CurrentPetAsync(ct).ConfigureAwait(false);
        if (current is null || current.Owner != c.CharacterId || current.ItemUid != e.ItemUid
            || (await current.ReadAsync(ct).ConfigureAwait(false)).ItemTemplateId != e.ExpectedTemplateId)
            throw new InvalidOperationException("Equipped pet changed since the name was decided.");
        c.Footprint.DemandEffects(c.Owner);
        return new PetNameChange(await current.RenameAsync(e.Name.ToArray(), ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Pet rename target is no longer live."));
    }
}
