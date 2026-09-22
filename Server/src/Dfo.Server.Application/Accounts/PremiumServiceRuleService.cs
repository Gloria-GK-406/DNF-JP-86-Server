using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Accounts;

internal sealed class PremiumServiceRuleService(IPremiumServiceDefinitionSource source) : IPremiumServiceRuleService
{
    private readonly Lazy<DoublePotRules> doublePot = new(source.ReadDoublePotRules);
    public int DoublePotDailyLimit => doublePot.Value.DailyLimit;
    public bool SupportsDoublePot(int itemId) => doublePot.Value.PotIds.Contains(itemId);
    private readonly Lazy<int> freeGoldCardLimit = new(source.ReadFreeGoldCardDailyLimit);
    public int FreeGoldCardDailyLimit => freeGoldCardLimit.Value;
    private readonly Lazy<int> dungeonBuffLimit = new(source.ReadDungeonBuffDailyLimit);
    public int DungeonBuffDailyLimit => dungeonBuffLimit.Value;
    private readonly Lazy<int> autoRepairLimit = new(source.ReadAutoRepairDailyLimit);
    public int AutoRepairDailyLimit => autoRepairLimit.Value;
    private readonly Lazy<int> freeWeaknessLimit = new(source.ReadFreeWeaknessDailyLimit);
    public int FreeWeaknessDailyLimit => freeWeaknessLimit.Value;
    private readonly Lazy<PremiumContractLevels> levels = new(source.ReadContractLevels);
    public int LevelAllowance(PremiumBenefit benefit) => benefit switch
    {
        PremiumBenefit.LearnSkillsEarly => levels.Value.ExpertSkill,
        PremiumBenefit.EquipItemsEarly => levels.Value.OverEquipment,
        _ => throw new ArgumentOutOfRangeException(nameof(benefit)),
    };
}
