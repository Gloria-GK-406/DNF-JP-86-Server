using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Application.Accounts;

internal sealed class PremiumServiceUsageDomainService(IPremiumServiceDomainService premiums,
    IPremiumServiceUsageTable counters, ICharacterDailyCounterValueTable legacyCounters,
    ICharacterTable characters, IPremiumServiceRuleService rules, IDailyResetRuleService days,
    IDomainTransactionAccessor transactions, TimeProvider clock) : IPremiumServiceUsageDomainService
{
    public DateTimeOffset CurrentPeriodStart => days.PeriodStart(clock.GetUtcNow());
    private const string AutoRepair = "premium_auto_repair_used";
    private const string FreeWeakness = "premium_free_weakness_used";
    public async ValueTask<IReadOnlyList<CharacterPremiumUsageSlotState>> ReadAsync(CharacterId id, CancellationToken token)
    {
        var lottery = await legacyCounters.FindValueAsync(id, "lottery_double_reward_used", token).ConfigureAwait(false) ?? 0;
        var now = clock.GetUtcNow();
        var row = await counters.ReadAsync(id, FreeWeakness, token).ConfigureAwait(false);
        var weakness = CurrentUsage(row, now);
        if (lottery is < 0 or > 255 || weakness is < 0 or > 255)
            throw new InvalidDataException("Premium usage is outside the one-byte projection range.");
        var repair = CurrentUsage(await counters.ReadAsync(id, AutoRepair, token).ConfigureAwait(false), now);
        return [new(1, lottery), new(5, weakness), new(6, repair)];
    }
    public ValueTask<bool> TryUseFreeWeaknessRecoveryAsync(AccountId accountId, CharacterId id, CancellationToken token) =>
        TryUseAsync(accountId, id, FreeWeakness, PremiumBenefit.FreeWeaknessRecovery, rules.FreeWeaknessDailyLimit, token);
    public ValueTask<bool> TryUseAutoRepairAsync(AccountId accountId, CharacterId id, CancellationToken token) =>
        TryUseAsync(accountId, id, AutoRepair, PremiumBenefit.AutoRepair, rules.AutoRepairDailyLimit, token);
    private async ValueTask<bool> TryUseAsync(AccountId accountId, CharacterId id, string key, PremiumBenefit benefit, int limit, CancellationToken token)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Premium consumption requires a transaction.");
        if (!await characters.BelongsToAsync(id, accountId, token).ConfigureAwait(false)
            || !await premiums.For(accountId).HasBenefitAsync(benefit, token).ConfigureAwait(false))
            return false;
        var now = clock.GetUtcNow();
        var row = await counters.ReadAsync(id, key, token).ConfigureAwait(false);
        var used = CurrentUsage(row, now);
        if (used >= limit) return false;
        return await counters.TryWriteAsync(id, key, row, used + 1, now, token).ConfigureAwait(false) == 1;
    }
    private int CurrentUsage(PremiumServiceUsageRow? row, DateTimeOffset now)
    {
        if (row?.LastConsumedAt is not { } recorded || !days.IsCurrentPeriod(recorded, now)) return 0;
        if (row.UsedCount is < 0 or > 255) throw new InvalidDataException("Invalid premium usage.");
        return row.UsedCount;
    }

}
