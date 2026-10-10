using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed class DimensionDungeonDomainService(IDimensionDungeonRuleService rules, IDungeonStructureRuleService layouts,
    IDungeonVisitDomainService visits, IUsageQuotaDomainService quotas, IEventService events,
    IDailyResetRuleService daily, TimeProvider clock, IDomainTransactionAccessor transactions) : IDimensionDungeonDomainService
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
        if (transactions.Current is not { IsReadOnly: false })
            throw new InvalidOperationException("Dimension entry requires the shared writable entry transaction.");
        if (context.RequestMode != 0 || context.PracticeMode != 0 || context.Participants.Count is < 1 or > 4)
            return DungeonEntryAttempt.Refused(DungeonEntryFailure.MissingPrerequisite);
        var period = Period();
        var updates = new List<DimensionEntryQuotaUpdate>();
        foreach (var participant in context.Participants)
        {
            var expected = await quotas.ReadAsync(participant.CharacterId, UsageQuotaKey.Dimension(period), ct).ConfigureAwait(false);
            var state = rules.Snapshot(period, expected?.UsedCount ?? 0);
            if (state.Remaining + (long)state.ExtraRemaining <= 0)
                return new(null, context.Participants.Count == 1 ? DungeonEntryFailure.DailyEntryLimitReached
                    : DungeonEntryFailure.MemberEntryLimitReached, participant.Slot);
            updates.Add(new(participant.CharacterId, period, expected, checked(state.Used + 1)));
        }
        DungeonLayout? layout;
        try { layout = await layouts.ResolveAsync(context.DungeonId, context.Facts, Random.Shared.Next(), ct).ConfigureAwait(false); }
        catch (InvalidDataException error) { return DungeonEntryAttempt.Refused(DungeonEntryFailure.ConfigurationInvalid, error.Message); }
        if (layout is null) return DungeonEntryAttempt.Refused(DungeonEntryFailure.DungeonNotFound);
        var result = await visits.TryEnterPreparedPartyAsync(context.Participants, layout, context.Facts, ct).ConfigureAwait(false);
        if (result.Entry is not null)
            await events.DirectAsync(new DimensionEntryRecorded(updates.ToImmutableArray()), ct).ConfigureAwait(false);
        return result;
    }
}
