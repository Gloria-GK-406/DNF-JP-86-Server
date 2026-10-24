using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public interface IEquipmentOutworldDomainService
{
    ValueTask<EquipmentOutworldResult> ApplyAsync(
        SelectedSessionIdentity session, EquipmentOutworldAction action,
        BusinessSlot targetSlot, int expectedTargetItemId,
        BusinessSlot materialSlot, int expectedMaterialItemId,
        byte selectedOption, IEffectFootprintRecorder footprint, CancellationToken cancellationToken);
}

public interface IEquipmentOutworldSeedSource
{
    int NextSeed();
}
