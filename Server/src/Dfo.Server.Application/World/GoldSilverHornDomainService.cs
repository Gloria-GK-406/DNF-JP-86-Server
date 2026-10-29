using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

internal sealed class GoldSilverHornDomainService(IGoldSilverHornRuleService rules,
    IDungeonAdmissionDomainService admission, IDungeonStructureRuleService layouts,
    IDungeonVisitDomainService visits, IDungeonRunDomainService runs,
    IEventManagementDomainService activities, IUsageQuotaDomainService quotas,
    IDailyResetRuleService daily, TimeProvider clock) : IGoldSilverHornDomainService
{
    public bool Supports(int dungeonId) => rules.Supports(dungeonId);
    private DateOnly Period() => daily.PeriodDay(clock.GetUtcNow());

    public async ValueTask<GoldSilverHornQuotaSnapshot> ReadQuotaAsync(CharacterId character, CancellationToken ct)
    {
        var period = Period();
        var used = (await quotas.ReadAsync(character, UsageQuotaKey.GoldSilverHorn(period), ct).ConfigureAwait(false))?.UsedCount ?? 0;
        if (used < 0 || used > ushort.MaxValue) throw new InvalidDataException("Invalid gold/silver horn quota.");
        return new(period, used, rules.MaximumEntries);
    }

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct)
    {
        if (!Supports(context.DungeonId) || context.RequestMode != 0 || context.PracticeMode != 0
            || context.MirrorQuestId is not null || context.Facts.Intent.Kind == DungeonEntryIntentKind.Cycle
            || context.Participants.Count != 1)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        if (context.Participants.Any(x => runs.Find(x.CharacterId) is not null))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.AlreadyInDungeon);
        if (!(await activities.ListAsync(clock.GetUtcNow(), ct).ConfigureAwait(false)).Entries
            .Any(x => x.EventId == rules.EventId && x.Status == "active"))
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.OutsideOpeningHours);
        DungeonLayout? layout;
        int maximum;
        try
        {
            _ = await rules.DefinitionAsync(ct).ConfigureAwait(false);
            maximum = rules.MaximumEntries;
            layout = await layouts.ResolveAsync(context.DungeonId, context.Facts, Random.Shared.Next(), ct).ConfigureAwait(false);
        }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
        if (layout is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound);
        layout = layout with { Mode = new DungeonRunMode.Plain(DungeonRunKind.GoldSilverHorn) };
        return await admission.ExecuteAsync(context, new()
        {
            RequireMinimumLevel = true, ConsumesFatigue = false,
            Quota = new(UsageQuotaFamily.GoldSilverHorn, Period(), maximum, context.DungeonId),
        }, (receipt, token) => visits.EnterGrantedPartyAsync(context.Participants, layout, context.Facts, receipt, token), ct).ConfigureAwait(false);
    }
}
