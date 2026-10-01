using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Abstractions.Accounts;

public enum PremiumServiceType { OverEquipmentContract = 22, ExpertSkillContract = 27, BlackDiamond = 56, GrowthContract = 84, CrystalContract = 97,
    // Legacy storage keys for independent services; never wire premium types.
    FreeGoldCard = 580, DoublePot = 581, QuestHelper = 582, DungeonBuff = 583,
    AutoHpMpRecovery = 584, FreeWeaknessRecovery = 585, AutoRepair = 586, FastPot = 587 }
public enum PremiumBenefit { LearnSkillsEarly, EquipItemsEarly, FreeWeaknessRecovery, AutoRepair, FreeGoldCard, DungeonBuff, DoublePot, QuestItemDropBonus }

public interface IPremiumServiceDomainService
{
    IAccountPremiumServices For(AccountId accountId);
    ValueTask<int> GetBossEquipmentDropBonusPercentAsync(IReadOnlyList<AccountId> participants,
        CancellationToken cancellationToken);
}

public interface IAccountPremiumServices
{
    ValueTask<IReadOnlyList<AccountPremiumEntryState>> ReadAtAsync(DateTimeOffset at, CancellationToken token) =>
        ValueTask.FromException<IReadOnlyList<AccountPremiumEntryState>>(new NotSupportedException("Explicit-time premium read is unavailable."));
    ValueTask SetExpiryAsync(PremiumServiceType type, long expectedExpiry, long nextExpiry, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<AccountPremiumEntryState>> ReadAsync(CancellationToken cancellationToken);
    ValueTask<ushort> GetExtraFatigueAsync(CancellationToken cancellationToken);
    ValueTask<double> GetExperienceBonusRateAsync(CancellationToken cancellationToken);
    ValueTask<double> GetBlackDiamondExperienceBonusRateAsync(CancellationToken cancellationToken);
    ValueTask<bool> IsActiveAsync(PremiumServiceType type, CancellationToken cancellationToken);
    ValueTask<bool> HasBenefitAsync(PremiumBenefit benefit, CancellationToken cancellationToken);
    ValueTask<AccountPremiumEntryState> ExtendAsync(PremiumServiceType type, TimeSpan duration,
        CancellationToken cancellationToken);
}
