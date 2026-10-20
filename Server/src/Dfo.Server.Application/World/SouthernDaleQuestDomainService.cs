using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

internal sealed class SouthernDaleQuestDomainService(ISouthernDaleDomainStore store,
    ISouthernDaleRuleService rules, IEventManagementDomainService activities,
    IDailyResetRuleService reset, TimeProvider clock, IDomainTransactionAccessor transactions) : ISouthernDaleQuestDomainService
{
    private DateOnly Period => DateOnly.FromDateTime(reset.PeriodStart(clock.GetUtcNow()).ToOffset(TimeSpan.FromHours(8)).DateTime);

    public async ValueTask<bool> CanAcceptAsync(CharacterId character, CancellationToken ct) =>
        (await activities.ListAsync(clock.GetUtcNow(), ct).ConfigureAwait(false)).Entries
            .Any(x => x.EventId == rules.EventId && x.Status == "active")
        && !await store.HasCompletedDailyQuestAsync(character, Period, ct).ConfigureAwait(false);

    public async ValueTask<bool> TryCompleteAsync(CharacterId character, CancellationToken ct)
    {
        if (transactions.Current is not { IsReadOnly: false })
            throw new InvalidOperationException("Southern Dale daily quest requires a writable transaction.");
        return await store.TryCompleteDailyQuestAsync(character, Period, ct).ConfigureAwait(false);
    }
}
