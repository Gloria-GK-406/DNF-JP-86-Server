using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Application.Accounts;

internal sealed class PremiumServiceUsageDomainService(IPremiumServiceDomainService premiums,
    IPremiumServiceUsageTable counters,
    ICharacterTable characters, IPremiumServiceRuleService rules, IDailyResetRuleService days,
    IDomainTransactionAccessor transactions, TimeProvider clock) : IPremiumServiceUsageDomainService
{
    public DateTimeOffset CurrentPeriodStart => days.PeriodStart(clock.GetUtcNow());
    private const string DoublePotTotal = "lottery_double_reward_total_used";
    private const string DoublePot = "lottery_double_reward_used";
    private const string FreeGoldCard = "premium_free_gold_card_used";
    private const string DungeonBuff = "premium_dungeon_buff_used";
    private const string AutoRepair = "premium_auto_repair_used";
    private const string FreeWeakness = "premium_free_weakness_used";
    public async ValueTask<IReadOnlyList<CharacterPremiumUsageSlotState>> ReadAsync(CharacterId id, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var lottery = CurrentUsage(await counters.ReadAsync(id, DoublePot, token).ConfigureAwait(false), now);
        var row = await counters.ReadAsync(id, FreeWeakness, token).ConfigureAwait(false);
        var weakness = CurrentUsage(row, now);
        if (lottery is < 0 or > 255 || weakness is < 0 or > 255)
            throw new InvalidDataException("Premium usage is outside the one-byte projection range.");
        var repair = CurrentUsage(await counters.ReadAsync(id, AutoRepair, token).ConfigureAwait(false), now);
        var goldCard = CurrentUsage(await counters.ReadAsync(id, FreeGoldCard, token).ConfigureAwait(false), now);
        var buff = CurrentUsage(await counters.ReadAsync(id, DungeonBuff, token).ConfigureAwait(false), now);
        return [new(0, goldCard), new(1, lottery), new(3, buff), new(5, weakness), new(6, repair)];
    }
    public async ValueTask<bool> TryUseDoublePotAsync(AccountId accountId, CharacterId id, CancellationToken token)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Premium consumption requires a transaction.");
        if (!await characters.BelongsToAsync(id, accountId, token).ConfigureAwait(false)
            || !await premiums.For(accountId).HasBenefitAsync(PremiumBenefit.DoublePot, token).ConfigureAwait(false))
            return false;
        var now = clock.GetUtcNow();
        var round = await counters.ReadAsync(id, DoublePot, token).ConfigureAwait(false);
        var total = await counters.ReadAsync(id, DoublePotTotal, token).ConfigureAwait(false);
        var used = CurrentUsage(round, now);
        // Before the cumulative counter existed, the round count was the day's actual usage.
        var totalUsed = CurrentUsage(total ?? round, now);
        if (used >= rules.DoublePotDailyLimit || totalUsed >= rules.DoublePotTotalDailyLimit) return false;
        await WriteRequiredAsync(id, DoublePot, round, used + 1, now, token).ConfigureAwait(false);
        await WriteRequiredAsync(id, DoublePotTotal, total, totalUsed + 1, now, token).ConfigureAwait(false);
        return true;
    }

    public async ValueTask RenewDoublePotAsync(AccountId accountId, CharacterId id, CancellationToken token)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Premium renewal requires a transaction.");
        if (!await characters.BelongsToAsync(id, accountId, token).ConfigureAwait(false))
            throw new InvalidOperationException("Premium renewal character does not belong to the account.");
        var now = clock.GetUtcNow();
        var round = await counters.ReadAsync(id, DoublePot, token).ConfigureAwait(false);
        var total = await counters.ReadAsync(id, DoublePotTotal, token).ConfigureAwait(false);
        var totalUsed = CurrentUsage(total ?? round, now);
        // Persist the legacy baseline before replacing the round with a quota projection.
        await WriteRequiredAsync(id, DoublePotTotal, total, totalUsed, now, token).ConfigureAwait(false);
        await WriteRequiredAsync(id, DoublePot, round, rules.DoublePotRenewedUsage(totalUsed), now, token).ConfigureAwait(false);
    }

    private async ValueTask WriteRequiredAsync(CharacterId id, string key, PremiumServiceUsageRow? expected,
        int used, DateTimeOffset now, CancellationToken token)
    {
        if (await counters.TryWriteAsync(id, key, expected, used, now, token).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Premium usage changed during settlement.");
    }
    public ValueTask<bool> TryUseFreeWeaknessRecoveryAsync(AccountId accountId, CharacterId id, CancellationToken token) =>
        TryUseAsync(accountId, id, FreeWeakness, PremiumBenefit.FreeWeaknessRecovery, rules.FreeWeaknessDailyLimit, token);
    public ValueTask<bool> TryUseFreeGoldCardAsync(AccountId accountId, CharacterId id, CancellationToken token) =>
        TryUseAsync(accountId, id, FreeGoldCard, PremiumBenefit.FreeGoldCard, rules.FreeGoldCardDailyLimit, token);
    public ValueTask<bool> TryUseDungeonBuffAsync(AccountId accountId, CharacterId id, CancellationToken token) =>
        TryUseAsync(accountId, id, DungeonBuff, PremiumBenefit.DungeonBuff, rules.DungeonBuffDailyLimit, token);
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
