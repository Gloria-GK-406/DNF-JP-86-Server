using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class MaterialConsumptionRuleService : IMaterialConsumptionRuleService
{
    public bool IsValidRequest(IReadOnlyList<MaterialCost> costs) =>
        costs.Count > 0 && costs.All(x => x.Count > 0 && x.ItemId > 0)
        && costs.Select(x => x.Slot).Distinct().Count() == costs.Count;

    public bool IsConsumable(ItemReference item, int expectedItemId) =>
        item.ItemId == expectedItemId && item.IsPile
        && item.IsPile && item.Snapshot.EquipmentLockId == 0;
}
