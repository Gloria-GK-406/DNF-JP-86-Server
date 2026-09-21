using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Application.Accounts;

internal sealed class PremiumServiceUsageDomainService(IPremiumServiceDomainService premiums,
    IPremiumServiceUsageTable counters, ICharacterDailyCounterValueTable legacyCounters,
    ICharacterTable characters, IPremiumServiceRuleService rules, IDailyRefillRuleService days,
    IDomainTransactionAccessor transactions, TimeProvider clock) : IPremiumServiceUsageDomainService
{
    public string CurrentPeriod => days.PeriodOf(clock.GetUtcNow());
    private const string FreeWeakness = "premium_free_weakness_used";
    public async ValueTask<IReadOnlyList<CharacterPremiumUsageSlotState>> ReadAsync(CharacterId id, CancellationToken token)
    {
        var lottery = await legacyCounters.FindValueAsync(id, "lottery_double_reward_used", token).ConfigureAwait(false) ?? 0;
        var weakness = await counters.ReadAsync(id, FreeWeakness, days.PeriodOf(clock.GetUtcNow()), token).ConfigureAwait(false);
        if (lottery is < 0 or > 255 || weakness is < 0 or > 255)
            throw new InvalidDataException("Premium usage is outside the one-byte projection range.");
        return [new(1, lottery), new(5, weakness)];
    }
    public async ValueTask<bool> TryUseFreeWeaknessRecoveryAsync(AccountId accountId, CharacterId id, CancellationToken token)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Premium consumption requires a transaction.");
        if (!await characters.BelongsToAsync(id, accountId, token).ConfigureAwait(false)
            || !await premiums.For(accountId).HasBenefitAsync(PremiumBenefit.FreeWeaknessRecovery, token).ConfigureAwait(false))
            return false;
        return await counters.TryIncrementAsync(id, FreeWeakness, days.PeriodOf(clock.GetUtcNow()),
            rules.FreeWeaknessDailyLimit, token).ConfigureAwait(false) == 1;
    }
}
