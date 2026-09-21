using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Application.Accounts;

internal sealed class PremiumServiceRuleService(IPremiumServiceDefinitionSource source) : IPremiumServiceRuleService
{
    private readonly Lazy<PremiumContractLevels> levels = new(source.ReadContractLevels);
    public int LevelAllowance(PremiumBenefit benefit) => benefit switch
    {
        PremiumBenefit.LearnSkillsEarly => levels.Value.ExpertSkill,
        PremiumBenefit.EquipItemsEarly => levels.Value.OverEquipment,
        _ => throw new ArgumentOutOfRangeException(nameof(benefit)),
    };
}
