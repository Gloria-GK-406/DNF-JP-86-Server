using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class HerasDungeonDomainService(IHerasRuleService rules, IDungeonAdmissionDomainService admission,
    IDungeonStructureRuleService layouts, IDungeonVisitDomainService visits, IDungeonRunDomainService runs,
    ICharacterDomainService characters, IEventManagementDomainService activities, IHerasQuotaDomainService quotas,
    TimeProvider clock) : IHerasDungeonDomainService
{
    public bool Supports(int dungeonId) => rules.Supports(dungeonId);
    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct)
    {
        if (!Supports(context.DungeonId) || context.Participants.Count != 1 || context.RequestMode != 0
            || context.PracticeMode != 0 || context.MirrorQuestId is not null || context.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        var player = context.Participants[0];
        if (runs.Find(player.CharacterId) is not null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.AlreadyInDungeon);
        if (!await activities.IsOpenAsync(rules.EventId, clock.GetUtcNow(), ct).ConfigureAwait(false))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.OutsideOpeningHours);
        HerasDefinition definition;
        try { definition = await rules.DefinitionAsync(ct).ConfigureAwait(false); }
        catch (InvalidDataException e) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, e.Message); }
        if (definition.DailyEntries != rules.DailyEntries) return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid);
        var progress = await characters.Get(player.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
        if (progress is null || progress.Level < definition.Dungeon.MinimumRequiredLevel) return DungeonEntryAttempt.Refused(DungeonEntryFailure.LevelTooLow);
        var quota = await quotas.ReadQuotaAsync(player.CharacterId, ct).ConfigureAwait(false);
        var facts = context.Facts with { Difficulty = (DungeonDifficulty)1 };
        return await admission.ExecuteAsync(context with { Facts = facts }, new()
        {
            RequireMinimumLevel = true, ConsumesFatigue = false, RequiredItemsOverride = definition.Dungeon.RequiredItems,
            Quota = new(UsageQuotaFamily.HerasDungeon, quota.Period, checked(quota.Maximum + quota.Granted), context.DungeonId),
        }, async (receipt, token) =>
        {
            DungeonLayout? layout;
            try { layout = await layouts.ResolveAsync(context.DungeonId, facts, Random.Shared.Next(), token).ConfigureAwait(false); }
            catch (InvalidDataException e) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, e.Message); }
            if (layout is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound);
            layout = layout with { Mode = new DungeonRunMode.Plain(DungeonRunKind.Heras), FixedMonsterLevel = progress.Level, BasisLevel = progress.Level, ConsumesFatigue = false };
            return await visits.EnterGrantedPartyAsync(context.Participants, layout, facts, receipt, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }
}

internal sealed class HerasQuotaDomainService(IHerasRuleService rules, IUsageQuotaDomainService quotas,
    IDailyResetRuleService daily, IEventService events, TimeProvider clock) : IHerasQuotaDomainService
{
    private DateOnly Period(DateTimeOffset at) => daily.PeriodDay(at);
    public ValueTask<HerasQuotaSnapshot> ReadQuotaAsync(CharacterId character, CancellationToken ct) => ReadQuotaAtAsync(character, clock.GetUtcNow(), ct);
    public async ValueTask<HerasQuotaSnapshot> ReadQuotaAtAsync(CharacterId character, DateTimeOffset at, CancellationToken ct)
    {
        var period = Period(at);
        var maximum = rules.DailyEntries;
        var entries = await quotas.ReadAsync(character, UsageQuotaKey.Heras("entries", period), ct).ConfigureAwait(false);
        var bonus = await quotas.ReadAsync(character, UsageQuotaKey.Heras("bonus", period), ct).ConfigureAwait(false);
        if (entries?.UsedCount < 0 || bonus?.UsedCount < 0) throw new InvalidDataException("Invalid Heras counters.");
        return new(rules.DungeonId, period, maximum, entries, bonus);
    }
    public async ValueTask ValidateAllowanceAsync(EffectExecutionContext context, AddDungeonEntryAllowanceEffect effect, CancellationToken ct)
    {
        if (!rules.Supports(effect.DungeonId) || effect.Increase != 1 || effect.Period != Period(context.At))
            throw new InventoryEffectException(InventoryEffectRejection.UnsupportedItem);
        var snapshot = await ReadQuotaAtAsync(context.CharacterId, context.At, ct).ConfigureAwait(false);
        if (snapshot.Bonus != effect.ExpectedBonus || snapshot.Used != effect.ExpectedUsed
            || snapshot.Remaining >= byte.MaxValue || snapshot.Granted == int.MaxValue)
            throw new InventoryEffectException(InventoryEffectRejection.InvalidTarget);
    }
    public async ValueTask ApplyAllowanceAsync(EffectExecutionContext context, AddDungeonEntryAllowanceEffect effect, CancellationToken ct)
    {
        context.Footprint.DemandEffects(context.Owner);
        await ValidateAllowanceAsync(context, effect, ct).ConfigureAwait(false);
        context.Footprint.DemandEffects(context.Owner);
        await events.DirectAsync(new HerasQuotaRecorded(context.CharacterId, effect.Period, "bonus", effect.ExpectedBonus,
            checked((effect.ExpectedBonus?.UsedCount ?? 0) + effect.Increase)), ct).ConfigureAwait(false);
    }
}
