using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

/// <summary>Immutable PVF identities and metadata; policy probabilities belong to the rule service.</summary>
public sealed record LostLandDropContent(ImmutableDictionary<int, int> MapCrystalIds,
    ImmutableDictionary<int, RolledDrop> Items, ImmutableArray<RolledDrop> Equipment);

public interface ILostLandDropContentSource
{
    ValueTask<LostLandDropContent> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>A cell-frozen reward whose shared boss family can pay only once per run.</summary>
public sealed record LostLandKillBonus(ImmutableHashSet<int> SharedBossCodes, ImmutableArray<RolledDrop> Items);

public sealed record LostLandDropPlan(ImmutableArray<RolledDrop> Regular, LostLandKillBonus? Bonus = null)
{
    public static LostLandDropPlan Empty { get; } = new([]);
}

public interface ILostLandDropRuleService
{
    ValueTask<LostLandDropPlan> PrepareAsync(DungeonCellCreationContext context,
        RoomActorDescription actor, ushort sequence, CancellationToken cancellationToken);
}
