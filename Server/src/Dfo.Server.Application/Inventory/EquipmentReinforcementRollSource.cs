using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentReinforcementRollSource : IEquipmentReinforcementRollSource
{
    public int Next(int exclusiveUpperBound) => Random.Shared.Next(exclusiveUpperBound);
}
