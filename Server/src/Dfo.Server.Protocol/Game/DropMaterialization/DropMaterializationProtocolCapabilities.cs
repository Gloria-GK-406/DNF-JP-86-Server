using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Protocol.Game.DropMaterialization;

/// <summary>Maps A21 delivery layouts to domain-neutral materialization limits.</summary>
public static class DropMaterializationProtocolCapabilities
{
    // 001D room drops and 0026 death drops have u8 counts. Independent generation keeps
    // the existing conservative death-batch limit, including the tower and hell callers.
    public static DropMaterializationLimits Limits { get; } = new(
        InitialGroundItems: byte.MaxValue,
        DeathGroundItems: byte.MaxValue,
        IndependentDrops: byte.MaxValue,
        LastGroundItemNumber: ushort.MaxValue);
}
