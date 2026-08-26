using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.World;

/// <summary>Keeps the runs in progress in memory for the server's lifetime.</summary>
/// <remarks>
/// One entry per character, replaced whole on every transition, so a reader either sees the run
/// before a move or the run after it and never a half-applied one. That is enough while a run has
/// exactly one participant; the party-grained lease this will need arrives with parties, and is
/// not invented ahead of the thing it protects.
/// </remarks>
internal sealed class DungeonRunDomainService : IDungeonRunDomainService
{
    private readonly ConcurrentDictionary<CharacterId, DungeonRunState> _runs = new();

    public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var state = new DungeonRunState(
            layout,
            layout.StartCell,
            layout.StartRoomMapId,
            ImmutableHashSet.Create(layout.StartCell),
            ImmutableHashSet<GroundDropId>.Empty);
        _runs[characterId] = state;
        return state;
    }

    public DungeonRunState? Find(CharacterId characterId) =>
        _runs.TryGetValue(characterId, out var state) ? state : null;

    public DungeonRunState? TryMove(CharacterId characterId, MazeCell target)
    {
        while (true)
        {
            if (!_runs.TryGetValue(characterId, out var current))
                return null;
            if (!current.Layout.Rooms.TryGetValue(target, out var mapId))
                return null;

            var moved = current with
            {
                CurrentCell = target,
                CurrentRoomMapId = mapId,
                VisitedCells = ((ImmutableHashSet<MazeCell>)current.VisitedCells).Add(target),
            };
            if (_runs.TryUpdate(characterId, moved, current))
                return moved;
        }
    }

    /// <remarks>
    /// The same compare-and-swap the move uses, and for the same reason: two requests naming one
    /// piece must not both come away with it. What is remembered is the piece, not the item -- the
    /// room says what lies where, and this says only what is no longer there.
    /// </remarks>
    public DungeonRunState? TryTakeGroundDrop(CharacterId characterId, ushort groundDropNumber)
    {
        while (true)
        {
            if (!_runs.TryGetValue(characterId, out var current))
                return null;

            var taken = (ImmutableHashSet<GroundDropId>)current.TakenGroundDrops;
            var id = new GroundDropId(current.CurrentCell, groundDropNumber);
            if (taken.Contains(id))
                return null;

            var claimed = current with { TakenGroundDrops = taken.Add(id) };
            if (_runs.TryUpdate(characterId, claimed, current))
                return claimed;
        }
    }

    public bool End(CharacterId characterId) => _runs.TryRemove(characterId, out _);
}
