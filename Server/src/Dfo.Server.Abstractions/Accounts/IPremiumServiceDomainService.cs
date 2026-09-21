using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Abstractions.Accounts;

public enum PremiumServiceType { OverEquipmentContract = 22, ExpertSkillContract = 27 }
public enum PremiumBenefit { LearnSkillsEarly, EquipItemsEarly }

public interface IPremiumServiceDomainService
{
    IAccountPremiumServices For(AccountId accountId);
}

public interface IAccountPremiumServices
{
    ValueTask<IReadOnlyList<AccountPremiumEntryState>> ReadAsync(CancellationToken cancellationToken);
    ValueTask<bool> IsActiveAsync(PremiumServiceType type, CancellationToken cancellationToken);
    ValueTask<bool> HasBenefitAsync(PremiumBenefit benefit, CancellationToken cancellationToken);
    ValueTask<AccountPremiumEntryState> ExtendAsync(PremiumServiceType type, TimeSpan duration,
        CancellationToken cancellationToken);
}
