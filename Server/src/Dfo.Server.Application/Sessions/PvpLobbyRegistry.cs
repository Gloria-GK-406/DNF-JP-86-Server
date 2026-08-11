using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Sessions;

/// <summary>Keeps the duelling lobby's readiness and room offers in memory.</summary>
internal sealed class PvpLobbyRegistry : IPvpLobbyRegistry
{
    private readonly ConcurrentDictionary<CharacterId, PvpLobbyParticipant> _ready = new();
    private readonly ConcurrentDictionary<ushort, PvpRoom> _rooms = new();

    public void MarkReady(PvpLobbyParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        _ready[participant.CharacterId] = participant;
    }

    public bool ClearReady(CharacterId characterId) => _ready.TryRemove(characterId, out _);

    public void Offer(PvpRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);
        _rooms[room.RoomId] = room;
    }

    public bool Withdraw(ushort roomId) => _rooms.TryRemove(roomId, out _);

    public IReadOnlyList<PvpLobbyParticipant> ReadyOn(int listenerPort, CharacterId excluding)
    {
        var participants = new List<PvpLobbyParticipant>();
        foreach (var participant in _ready.Values)
        {
            if (participant.CharacterId == excluding || participant.ListenerPort != listenerPort)
                continue;

            participants.Add(participant);
        }

        participants.Sort(static (left, right) =>
            left.CharacterId.Value.CompareTo(right.CharacterId.Value));
        return participants;
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
