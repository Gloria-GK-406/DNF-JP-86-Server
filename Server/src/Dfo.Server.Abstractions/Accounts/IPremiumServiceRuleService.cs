namespace Dfo.Server.Abstractions.Accounts;

public interface IPremiumServiceRuleService
{
    int FreeWeaknessDailyLimit { get; }
    int LevelAllowance(PremiumBenefit benefit);
}
