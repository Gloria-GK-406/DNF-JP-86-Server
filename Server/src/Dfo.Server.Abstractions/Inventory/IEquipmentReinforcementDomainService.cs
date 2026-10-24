using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface IEquipmentReinforcementDomainService
{
    ValueTask<EquipmentReinforcementResult> ReinforceAsync(
        SelectedSessionIdentity session,
        BusinessSlot targetSlot,
        int expectedItemId,
        BusinessSlot? materialSlot,
        BusinessSlot? protectionSlot,
        IEffectFootprintRecorder footprint, CancellationToken cancellationToken,
        EquipmentUpgradeKind kind = EquipmentUpgradeKind.Reinforcement);
}

/// <summary>Provides a bounded draw so every outcome can be verified deterministically.</summary>
public interface IEquipmentReinforcementRollSource
{
    int Next(int exclusiveUpperBound);
}
