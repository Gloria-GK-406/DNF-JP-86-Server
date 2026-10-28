using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Application.Accounts;

internal sealed class PremiumServiceUsageDomainService(IPremiumServiceDomainService premiums,
    IUsageQuotaDomainService counters,
    IPremiumServiceRuleService rules, IDailyResetRuleService days,
    IDomainTransactionAccessor transactions, TimeProvider clock, IEventService events) : IPremiumServiceUsageDomainService
{
    public DateTimeOffset CurrentPeriodStart => days.PeriodStart(clock.GetUtcNow());
    private const string DoublePotTotal = "lottery_double_reward_total_used";
    private const string DoublePot = "lottery_double_reward_used";
    private const string FreeGoldCard = "premium_free_gold_card_used";
    private const string DungeonBuff = "premium_dungeon_buff_used";
    private const string AutoRepair = "premium_auto_repair_used";
    private const string FreeWeakness = "premium_free_weakness_used";
    public ValueTask<IReadOnlyList<CharacterPremiumUsageSlotState>> ReadAsync(CharacterId id, CancellationToken token) =>
        ReadAtAsync(id, clock.GetUtcNow(), token);
    public async ValueTask<IReadOnlyList<CharacterPremiumUsageSlotState>> ReadAtAsync(CharacterId id, DateTimeOffset now, CancellationToken token)
    {
        var lottery = CurrentUsage((await counters.ReadAsync(id, UsageQuotaKey.Premium(DoublePot), token).ConfigureAwait(false)).Premium(), now);
        var row = (await counters.ReadAsync(id, UsageQuotaKey.Premium(FreeWeakness), token).ConfigureAwait(false)).Premium();
        var weakness = CurrentUsage(row, now);
        if (lottery is < 0 or > 255 || weakness is < 0 or > 255)
            throw new InvalidDataException("Premium usage is outside the one-byte projection range.");
        var repair = CurrentUsage((await counters.ReadAsync(id, UsageQuotaKey.Premium(AutoRepair), token).ConfigureAwait(false)).Premium(), now);
        var goldCard = CurrentUsage((await counters.ReadAsync(id, UsageQuotaKey.Premium(FreeGoldCard), token).ConfigureAwait(false)).Premium(), now);
        var buff = CurrentUsage((await counters.ReadAsync(id, UsageQuotaKey.Premium(DungeonBuff), token).ConfigureAwait(false)).Premium(), now);
        return [new(0, goldCard), new(1, lottery), new(3, buff), new(5, weakness), new(6, repair)];
    }
    public async ValueTask<DoublePotUsageFacts> DoublePotFactsAsync(AccountId accountId, CharacterId id,
        DateTimeOffset at, CancellationToken token)
    {
        var eligible = (await premiums.For(accountId).ReadAsync(token).ConfigureAwait(false)).Any(x => x.PremiumType == (int)PremiumServiceType.DoublePot && x.ExpiresAtUnixSeconds > at.ToUnixTimeSeconds());
        var round = (await counters.ReadAsync(id, UsageQuotaKey.Premium(DoublePot), token).ConfigureAwait(false)).Premium();
        var total = (await counters.ReadAsync(id, UsageQuotaKey.Premium(DoublePotTotal), token).ConfigureAwait(false)).Premium();
        return new(eligible, new(round, total), CurrentUsage(round, at), CurrentUsage(total ?? round, at),
            rules.DoublePotDailyLimit, rules.DoublePotTotalDailyLimit, at);
    }

    public async ValueTask ApplyDoublePotAsync(CharacterId id,
        DoublePotUsageDecision decision, CancellationToken token)
    {
        if (transactions.Current is null || decision.NextRound is < 0 or > 255 || decision.NextTotal is < 0 or > 255)
            throw new InvalidOperationException("Invalid decided double-pot counter change.");
        await events.DirectAsync(new PremiumUsageRecorded(id, [
            new(DoublePot, decision.Expected.Round, decision.NextRound, decision.At),
            new(DoublePotTotal, decision.Expected.Total, decision.NextTotal, decision.At)]), token).ConfigureAwait(false);
    }

    public async ValueTask RenewDoublePotAsync(CharacterId id, CancellationToken token)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Premium renewal requires a transaction.");
        var now = clock.GetUtcNow();
        var round = (await counters.ReadAsync(id, UsageQuotaKey.Premium(DoublePot), token).ConfigureAwait(false)).Premium();
        var total = (await counters.ReadAsync(id, UsageQuotaKey.Premium(DoublePotTotal), token).ConfigureAwait(false)).Premium();
        var totalUsed = CurrentUsage(total ?? round, now);
        // Persist the legacy baseline before replacing the round with a quota projection.
        await events.DirectAsync(new PremiumUsageRecorded(id, [
            new(DoublePotTotal, total, totalUsed, now),
            new(DoublePot, round, rules.DoublePotRenewedUsage(totalUsed), now)]), token).ConfigureAwait(false);
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
        if (!await premiums.For(accountId).HasBenefitAsync(benefit, token).ConfigureAwait(false))
            return false;
        var now = clock.GetUtcNow();
        var row = (await counters.ReadAsync(id, UsageQuotaKey.Premium(key), token).ConfigureAwait(false)).Premium();
        var used = CurrentUsage(row, now);
        if (used >= limit) return false;
        await events.DirectAsync(new PremiumUsageRecorded(id, [new(key, row, checked(used + 1), now)]), token).ConfigureAwait(false);
        return true;
    }
    private int CurrentUsage(PremiumServiceUsageRow? row, DateTimeOffset now)
    {
        if (row?.LastConsumedAt is not { } recorded || !days.IsCurrentPeriod(recorded, now)) return 0;
        if (row.UsedCount is < 0 or > 255) throw new InvalidDataException("Invalid premium usage.");
        return row.UsedCount;
    }

}
