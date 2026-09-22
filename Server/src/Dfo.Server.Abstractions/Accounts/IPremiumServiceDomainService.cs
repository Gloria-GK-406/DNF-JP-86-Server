using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Abstractions.Accounts;

public enum PremiumServiceType { OverEquipmentContract = 22, ExpertSkillContract = 27,
    // Legacy storage keys for independent services; never wire premium types.
    FreeGoldCard = 580, DoublePot = 581, QuestHelper = 582, DungeonBuff = 583,
    AutoHpMpRecovery = 584, FreeWeaknessRecovery = 585, AutoRepair = 586, FastPot = 587 }
public enum PremiumBenefit { LearnSkillsEarly, EquipItemsEarly, FreeWeaknessRecovery, AutoRepair }

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
