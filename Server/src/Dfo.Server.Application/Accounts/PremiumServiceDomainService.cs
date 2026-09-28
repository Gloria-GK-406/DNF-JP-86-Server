using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Accounts;

internal sealed class PremiumServiceDomainService(IAccountPremiumTable table,
    IDomainTransactionAccessor transactions, TimeProvider clock, IPremiumServiceRuleService rules,
    IServerConfigService settings) : IPremiumServiceDomainService
{
    private readonly bool blackDiamondEnabled = settings.GetConfig<bool>("Premium:BlackDiamondEnabled");
    public async ValueTask<int> GetBossEquipmentDropBonusPercentAsync(IReadOnlyList<AccountId> participants,
        CancellationToken cancellationToken)
    {
        if (participants.Count is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(participants));
        var count = 0;
        foreach (var account in participants)
            if (await For(account).IsActiveAsync(PremiumServiceType.GrowthContract, cancellationToken).ConfigureAwait(false))
                count++;
        return rules.GrowthBossEquipmentBonusPercent(count);
    }

    public IAccountPremiumServices For(AccountId accountId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(accountId.Value);
        return new AccountServices(accountId, table, transactions, clock, rules, blackDiamondEnabled);
    }

    private sealed class AccountServices(AccountId accountId, IAccountPremiumTable table,
        IDomainTransactionAccessor transactions, TimeProvider clock, IPremiumServiceRuleService rules,
        bool blackDiamondEnabled) : IAccountPremiumServices
    {
        public async ValueTask<IReadOnlyList<AccountPremiumEntryState>> ReadAsync(CancellationToken cancellationToken)
        {
            var rows = await table.ReadAsync(accountId, cancellationToken).ConfigureAwait(false);
            var entries = rows.Where(row => row.PremiumType != (int)PremiumServiceType.BlackDiamond)
                .Select(row => new AccountPremiumEntryState(row.PremiumType, row.ExpiresAtUnixSeconds)).ToList();
            // Rolling display period; eligibility is controlled by configuration, not this expiry.
            if (blackDiamondEnabled)
                entries.Add(new((int)PremiumServiceType.BlackDiamond, clock.GetUtcNow().AddDays(365).ToUnixTimeSeconds()));
            return entries;
        }

        public async ValueTask<double> GetExperienceBonusRateAsync(CancellationToken cancellationToken) =>
            await IsActiveAsync(PremiumServiceType.GrowthContract, cancellationToken).ConfigureAwait(false)
                ? rules.GrowthExperienceBonusRate : 0;

        public async ValueTask<bool> IsActiveAsync(PremiumServiceType type, CancellationToken cancellationToken)
        {
            if (type == PremiumServiceType.BlackDiamond) return blackDiamondEnabled;
            var rows = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var now = clock.GetUtcNow().ToUnixTimeSeconds();
            return rows.Any(row => row.PremiumType == (int)type && row.ExpiresAtUnixSeconds > now);
        }

        public ValueTask<bool> HasBenefitAsync(PremiumBenefit benefit, CancellationToken cancellationToken) =>
            IsActiveAsync(benefit switch
            {
                PremiumBenefit.LearnSkillsEarly => PremiumServiceType.ExpertSkillContract,
                PremiumBenefit.EquipItemsEarly => PremiumServiceType.OverEquipmentContract,
                PremiumBenefit.FreeWeaknessRecovery => PremiumServiceType.FreeWeaknessRecovery,
                PremiumBenefit.DoublePot => PremiumServiceType.DoublePot,
                PremiumBenefit.FreeGoldCard => PremiumServiceType.FreeGoldCard,
                PremiumBenefit.DungeonBuff => PremiumServiceType.DungeonBuff,
                PremiumBenefit.AutoRepair => PremiumServiceType.AutoRepair,
                PremiumBenefit.QuestItemDropBonus => PremiumServiceType.QuestHelper,
                _ => throw new ArgumentOutOfRangeException(nameof(benefit)),
            }, cancellationToken);

        public async ValueTask<AccountPremiumEntryState> ExtendAsync(PremiumServiceType type, TimeSpan duration,
            CancellationToken cancellationToken)
        {
            if (type == PremiumServiceType.BlackDiamond)
                throw new InvalidOperationException("Black diamond is controlled by server configuration, not purchases.");
            if (transactions.Current is null) throw new InvalidOperationException("Premium extension requires a transaction.");
            // Unknown legacy types remain round-trippable while their effects are migrated separately.
            if (((int)type is <= 0 or > 127) && !Enum.IsDefined(type)) throw new ArgumentOutOfRangeException(nameof(type));
            if (duration.Ticks <= 0 || duration.Ticks % TimeSpan.TicksPerSecond != 0)
                throw new ArgumentOutOfRangeException(nameof(duration));
            var rows = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var old = rows.SingleOrDefault(row => row.PremiumType == (int)type)?.ExpiresAtUnixSeconds ?? 0;
            var expiry = checked(Math.Max(clock.GetUtcNow().ToUnixTimeSeconds(), old)
                + duration.Ticks / TimeSpan.TicksPerSecond);
            if (await table.UpsertExpiryAsync(accountId, (int)type, expiry, cancellationToken).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("Premium extension affected no row.");
            return new((int)type, expiry);
        }
    }
}
