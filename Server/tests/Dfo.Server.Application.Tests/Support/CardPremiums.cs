using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Tests.Support;

internal sealed class CardPremiums(params long[] activeAccounts) : IPremiumServiceDomainService
{
    private readonly long[] active = activeAccounts;
    public bool Enabled { get; set; } = true;
    public int Queries { get; private set; }
    public IAccountPremiumServices For(AccountId accountId) => new Account(this, accountId);
    public ValueTask<int> GetBossEquipmentDropBonusPercentAsync(IReadOnlyList<AccountId> accounts, CancellationToken token) => ValueTask.FromResult(0);
    private sealed class Account(CardPremiums owner, AccountId id) : IAccountPremiumServices
    {
        public ValueTask<bool> IsActiveAsync(PremiumServiceType type, CancellationToken token)
        {
            owner.Queries++;
            return ValueTask.FromResult(owner.Enabled && type == PremiumServiceType.BlackDiamond && owner.active.Contains(id.Value));
        }
        public ValueTask<IReadOnlyList<AccountPremiumEntryState>> ReadAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<ushort> GetExtraFatigueAsync(CancellationToken token) => ValueTask.FromResult((ushort)0);
        public ValueTask<double> GetExperienceBonusRateAsync(CancellationToken token) => ValueTask.FromResult(0d);
        public ValueTask<bool> HasBenefitAsync(PremiumBenefit benefit, CancellationToken token) => ValueTask.FromResult(false);
        public ValueTask<AccountPremiumEntryState> ExtendAsync(PremiumServiceType type, TimeSpan duration, CancellationToken token) => throw new NotSupportedException();
    }
}
