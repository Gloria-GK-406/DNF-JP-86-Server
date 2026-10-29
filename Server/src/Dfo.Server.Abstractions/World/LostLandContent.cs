using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Abstractions.World;

/// <summary>Null Source preserves the archive's (-1,-1) wildcard; it is not a movement grant.</summary>
public sealed record LostLandWarp(MazeCell? Source, MazeCell Target);

/// <summary>Content operands, without inventing meanings for loop and encounter flags.</summary>
public sealed record LostLandPatrol(int MoveTermMilliseconds, int LoopFlag, string DayCondition,
    int MeetCondition, ImmutableArray<MazeCell> Route);

/// <summary>The exploration content alongside the existing owner of maze declarations.</summary>
public sealed record LostLandDefinition(DungeonDefinition Dungeon, int FirstPeriodMilliseconds,
    int SecondPeriodMilliseconds, ImmutableArray<int> LimitTimeParameters, int BaseEntryCount,
    ImmutableArray<LostLandWarp> WarpConditions, ImmutableArray<LostLandPatrol> Patrols)
{
    public ImmutableArray<int> FeverParameters { get; init; } = [];
}

public interface ILostLandContentSource
{
    ValueTask<LostLandDefinition> ReadAsync(CancellationToken cancellationToken);
}
