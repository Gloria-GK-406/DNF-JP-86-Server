namespace Dfo.Server.Abstractions.Content;

public sealed record DoublePotRules(int DailyLimit, IReadOnlySet<int> PotIds);
public sealed record PremiumContractLevels(int ExpertSkill, int OverEquipment);
public interface IPremiumServiceDefinitionSource
{
    DoublePotRules ReadDoublePotRules();
    PremiumContractLevels ReadContractLevels();
    int ReadFreeWeaknessDailyLimit();
    int ReadDungeonBuffDailyLimit();
    int ReadAutoRepairDailyLimit();
    int ReadFreeGoldCardDailyLimit();
}
