using Dfo.Server.Data.Characters;
namespace Dfo.Server.Abstractions.Inventory;
public interface IEquipmentWearDomainService
{
    ValueTask<bool> CanWearAsync(CharacterId characterId, int itemId, CancellationToken cancellationToken);
}
public interface IEquipmentWearRuleService
{
    ValueTask<int?> MinimumLevelAsync(int itemId, CancellationToken cancellationToken);
}
