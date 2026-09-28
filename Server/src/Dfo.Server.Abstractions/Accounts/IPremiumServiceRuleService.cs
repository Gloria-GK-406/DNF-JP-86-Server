namespace Dfo.Server.Abstractions.Accounts;

public interface IPremiumServiceRuleService
{
    IReadOnlyList<int> QuestHelperMinimumDropRates { get; }
    int DrawQuestHelperBonusCount(ulong seed);
    int DoublePotDailyLimit { get; }
    int DoublePotTotalDailyLimit { get; }
    int DoublePotRenewedUsage(int totalUsed);
    bool SupportsDoublePot(int itemId);
    int FreeWeaknessDailyLimit { get; }
    int DungeonBuffDailyLimit { get; }
    int AutoRepairDailyLimit { get; }
    int FreeGoldCardDailyLimit { get; }
    double GrowthExperienceBonusRate { get; }
    int LevelAllowance(PremiumBenefit benefit);
}
