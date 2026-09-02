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

    public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, byte difficulty)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var state = new DungeonRunState(
            layout,
            layout.StartCell,
            layout.StartRoomMapId,
            ImmutableHashSet.Create(layout.StartCell),
            ImmutableHashSet<GroundDropId>.Empty,
            ImmutableHashSet<RoomActorId>.Empty,
            // The wall clock, deliberately, and only here. A run's length is a fact about how long
            // the player took, so it is measured rather than derived from anything the request
            // carries -- and this service is already the one thing in the dungeon path that keeps
            // state for as long as a run lasts.
            DateTimeOffset.UtcNow,
            difficulty);
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

    /// <remarks>The same compare-and-swap the move and the pick-up use, and for the same reason.</remarks>
    public DungeonRunState? TryReportActorDead(CharacterId characterId, ushort actorSequence)
    {
        while (true)
        {
            if (!_runs.TryGetValue(characterId, out var current))
                return null;

            var dead = (ImmutableHashSet<RoomActorId>)current.DeadActors;
            var id = new RoomActorId(current.CurrentCell, actorSequence);
            if (dead.Contains(id))
                return null;

            var reported = current with { DeadActors = dead.Add(id) };
            if (_runs.TryUpdate(characterId, reported, current))
                return reported;
        }
    }

    public DungeonRunState? TrySettle(CharacterId characterId)
    {
        while (true)
        {
            if (!_runs.TryGetValue(characterId, out var current))
                return null;

            if (current.IsSettled)
                return null;

            var settled = current with { IsSettled = true };
            if (_runs.TryUpdate(characterId, settled, current))
                return settled;
        }
    }

    /// <remarks>
    /// The same compare-and-swap as everything else here. What differs is the answer to a second
    /// arrival: a claim and a settle refuse it, and this one hands back the faces the run already
    /// has. Two result reports racing must leave one pair of faces standing, not two, and the
    /// loser of the race is answered with the winner's rather than with nothing.
    /// <para>
    /// The price goes in the same swap as the faces, which is what makes it impossible for a run
    /// to hold one clear's cards and another clear's price. The loser of a race is answered with
    /// the winner's price for the same reason it is answered with the winner's faces: the player
    /// was shown one pair and quoted one figure, and both belong to whichever settlement got there
    /// first.
    /// </para>
    /// </remarks>
    public DungeonRunState? FixClearRewardCards(
        CharacterId characterId,
        ClearRewardCardFace free,
        ClearRewardCardFace paid,
        int? paidFlipCost)
    {
        ArgumentNullException.ThrowIfNull(free);
        ArgumentNullException.ThrowIfNull(paid);

        while (true)
        {
            if (!_runs.TryGetValue(characterId, out var current))
                return null;

            if (current.ClearRewardCards is not null)
                return current;

            var carded = current with
            {
                ClearRewardCards = new ClearRewardCardSet(
                    free, paid, ImmutableHashSet<ClearRewardCardId>.Empty, paidFlipCost),
            };
            if (_runs.TryUpdate(characterId, carded, current))
                return carded;
        }
    }

    /// <remarks>
    /// The same compare-and-swap the pick-up uses, and for the same reason: two requests naming
    /// one card must not both come away with it. What is remembered is the position, not what was
    /// behind it -- the faces are fixed and say that themselves.
    /// </remarks>
    public ClearRewardCardSlot? TryClaimClearRewardCard(
        CharacterId characterId, ClearRewardCardKind kind, int index)
    {
        while (true)
        {
            if (!_runs.TryGetValue(characterId, out var current))
                return null;
            if (current.ClearRewardCards is not { } cards)
                return null;
            if (cards.SlotAt(kind, index) is not { } slot)
                return null;

            var claimed = cards.Claimed;
            var id = new ClearRewardCardId(kind, index);
            if (claimed.Contains(id))
                return null;

            var taken = current with
            {
                ClearRewardCards = cards with { Claimed = claimed.Add(id) },
            };
            if (_runs.TryUpdate(characterId, taken, current))
                return slot;
        }
    }

    /// <remarks>
    /// The same compare-and-swap as everything else here, and it matters more: two kills answered
    /// at once must not be handed the same number, and unlike a claim a number cannot be worked
    /// out again to notice the clash.
    /// </remarks>
    public IReadOnlyList<MonsterDrop> AllocateMonsterDrops(
        CharacterId characterId,
        int declaredDropCount,
        ushort actorSequence,
        IReadOnlyList<RolledDrop> rolled)
    {
        ArgumentNullException.ThrowIfNull(rolled);
        if (rolled.Count == 0)
            return [];

        while (true)
        {
            if (!_runs.TryGetValue(characterId, out var current))
                return [];

            var next = current.NextMonsterDropNumber(declaredDropCount);
            var numbered = new List<MonsterDrop>(rolled.Count);
            // Copied rather than cast to a builder: this list is replaced whole on every kill
            // like every other part of a run, and a run's drops are counted in ones.
            var allocated = new List<AllocatedMonsterDrop>(current.MonsterDrops);
            foreach (var piece in rolled)
            {
                var drop = new MonsterDrop(next++, piece);
                numbered.Add(drop);
                allocated.Add(new AllocatedMonsterDrop(
                    current.CurrentCell, actorSequence, drop));
            }

            var updated = current with { MonsterDrops = allocated };
            if (_runs.TryUpdate(characterId, updated, current))
                return numbered;
        }
    }

    public bool End(CharacterId characterId) => _runs.TryRemove(characterId, out _);
}
