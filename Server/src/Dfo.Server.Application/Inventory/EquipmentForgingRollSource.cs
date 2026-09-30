using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentForgingRollSource : IEquipmentForgingRollSource
{
    public int Next() => Random.Shared.Next(10000);
}
