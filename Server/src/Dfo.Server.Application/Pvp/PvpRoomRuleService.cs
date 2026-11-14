using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Application.Pvp;

internal sealed class PvpRoomRuleService(IPvpMapDefinitionSource maps) : IPvpRoomRuleService
{
    public bool AcceptsMap(int index) => index >= 0 && index < maps.Load().Order.Count;

    public byte StartMap(int index, int seed)
    {
        if (!AcceptsMap(index))
            throw new ArgumentOutOfRangeException(nameof(index), index, "Map index is outside the archive's map order.");
        return (byte)(index != 0 ? index : new Random(seed).Next(1, maps.Load().Order.Count));
    }
}
