using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Pvp;

namespace Dfo.Server.Application.Pvp;

/// <summary>Keeps the duel rooms on offer in memory.</summary>
internal sealed class PvpRoomDomainService : IPvpRoomDomainService
{
    private readonly ConcurrentDictionary<ushort, PvpRoom> _rooms = new();

    public void Offer(PvpRoom room)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(Offer));
        ArgumentNullException.ThrowIfNull(room);
        _rooms[room.RoomId] = room;
    }

    public bool Withdraw(ushort roomId)
    {
        GameplayWriteAssertion.Writing(nameof(PvpRoomDomainService) + "." + nameof(Withdraw));
        return _rooms.TryRemove(roomId, out _);
    }

    public IReadOnlyList<PvpRoom> OfferedOn(int listenerPort)
    {
        var rooms = new List<PvpRoom>();
        foreach (var room in _rooms.Values)
        {
            // A room still being announced is withheld: showing it would invite a join that
            // races the announcement it has not finished making.
            if (!room.Published || room.ListenerPort != listenerPort)
                continue;

            rooms.Add(room);
        }

        rooms.Sort(static (left, right) => left.RoomId.CompareTo(right.RoomId));
        return rooms;
    }
}
