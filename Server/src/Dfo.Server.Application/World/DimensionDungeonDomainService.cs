using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed class DimensionDungeonDomainService(IDimensionDungeonRuleService rules, IDungeonStructureRuleService layouts,
    IDungeonVisitDomainService visits, IUsageQuotaDomainService quotas, IDungeonAdmissionDomainService admission,
    IDailyResetRuleService daily, TimeProvider clock) : IDimensionDungeonDomainService
{
    private DateOnly Period() => DateOnly.FromDateTime(daily.PeriodStart(clock.GetUtcNow()).ToOffset(TimeSpan.FromHours(8)).DateTime);
    public async ValueTask<DimensionQuotaSnapshot> ReadQuotaAsync(CharacterId character, CancellationToken ct)
    {
        var period = Period();
        var row = await quotas.ReadAsync(character, UsageQuotaKey.Dimension(period), ct).ConfigureAwait(false);
        return rules.Snapshot(period, row?.UsedCount ?? 0);
    }

    public async ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct)
    {
        if (!rules.IsDimension(context.DungeonId))
            return await visits.TryEnterPartyAsync(context.Participants, context.DungeonId, context.Facts, ct).ConfigureAwait(false);
        if (context.RequestMode != 0 || context.PracticeMode != 0 || context.Participants.Count is < 1 or > 4)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        var period = Period();
        var initial = rules.Snapshot(period, 0);
        return await admission.ExecuteAsync(context, new()
        {
            Quota = new(UsageQuotaFamily.DimensionDungeon, period, checked(initial.Remaining + initial.ExtraRemaining)),
        }, async (receipt, token) =>
        {
            DungeonLayout? layout;
            try { layout = await layouts.ResolveAsync(context.DungeonId, context.Facts, Random.Shared.Next(), token).ConfigureAwait(false); }
            catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
            return layout is null ? DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound)
                : await visits.EnterGrantedPartyAsync(context.Participants, layout, context.Facts, receipt, token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }
}
