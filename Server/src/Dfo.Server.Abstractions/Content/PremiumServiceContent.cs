namespace Dfo.Server.Abstractions.Content;

public sealed record PremiumContractLevels(int ExpertSkill, int OverEquipment);
public interface IPremiumServiceDefinitionSource
{
    PremiumContractLevels ReadContractLevels();
    int ReadFreeWeaknessDailyLimit();
    int ReadAutoRepairDailyLimit();
    int ReadFreeGoldCardDailyLimit();
}
