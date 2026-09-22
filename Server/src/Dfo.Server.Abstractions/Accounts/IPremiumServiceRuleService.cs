namespace Dfo.Server.Abstractions.Accounts;

public interface IPremiumServiceRuleService
{
    int FreeWeaknessDailyLimit { get; }
    int AutoRepairDailyLimit { get; }
    int FreeGoldCardDailyLimit { get; }
    int LevelAllowance(PremiumBenefit benefit);
}
