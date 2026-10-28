using System.Globalization;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Quests;

internal sealed class GiantDragonCrystalExchangeDomainService(ICharacterDailyCounterTable counters,
    IDailyResetRuleService reset, TimeProvider clock, IDomainTransactionAccessor transactions)
    : IGiantDragonCrystalExchangeDomainService
{
    // This quota is deliberately limited to 8557; other daily quests retain their existing lifecycle.
    private const string CounterKey = "giant-dragon.quest.8557";
    private string Period => reset.PeriodStart(clock.GetUtcNow()).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    public async ValueTask<bool> CanAcceptAsync(CharacterId character, CancellationToken ct) =>
        await counters.FindClaimedPeriodAsync(character, CounterKey, ct).ConfigureAwait(false) != Period;

    public async ValueTask<bool> TryCompleteAsync(CharacterId character, CancellationToken ct)
    {
        if (transactions.Current is not { IsReadOnly: false })
            throw new InvalidOperationException("Crystal exchange quota requires a writable transaction.");
        if (!await CanAcceptAsync(character, ct).ConfigureAwait(false)) return false;
        await counters.ClaimAsync(character, CounterKey, Period, ct).ConfigureAwait(false);
        return true;
    }
}
