using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface IEquipmentForgingDomainService
{
    ValueTask<EquipmentForgingResult> ForgeAsync(SelectedSessionIdentity session,
        BusinessSlot targetSlot, int expectedTargetItemId, BusinessSlot materialSlot,
        CancellationToken ct);
}

public interface IEquipmentForgingRuleService
{
    ValueTask<EquipmentForgingDecision> DecideAsync(int equipmentItemId,
        ushort durability, byte oldLevel, int materialItemId, int roll,
        CancellationToken ct);
}

/// <summary>Draws a value in 0..9999 for one admitted forging attempt.</summary>
public interface IEquipmentForgingRollSource
{
    int Next();
}
