namespace Dfo.Server.Abstractions.Accounts;

public interface IPremiumServiceRuleService
{
    int DoublePotDailyLimit { get; }
    bool SupportsDoublePot(int itemId);
    int FreeWeaknessDailyLimit { get; }
    int DungeonBuffDailyLimit { get; }
    int AutoRepairDailyLimit { get; }
    int FreeGoldCardDailyLimit { get; }
    int LevelAllowance(PremiumBenefit benefit);
}
