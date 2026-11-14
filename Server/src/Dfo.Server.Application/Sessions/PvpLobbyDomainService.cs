using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Sessions;

/// <summary>Keeps the duelling lobby's readiness in memory.</summary>
internal sealed class PvpLobbyDomainService : IPvpLobbyDomainService
{
    private readonly ConcurrentDictionary<CharacterId, PvpLobbyParticipant> _ready = new();

    public void MarkReady(PvpLobbyParticipant participant)
    {
        GameplayWriteAssertion.Writing(nameof(PvpLobbyDomainService) + "." + nameof(MarkReady));
        ArgumentNullException.ThrowIfNull(participant);
        _ready[participant.CharacterId] = participant;
    }

    public bool ClearReady(CharacterId characterId)
    {
        GameplayWriteAssertion.Writing(nameof(PvpLobbyDomainService) + "." + nameof(ClearReady));
        return _ready.TryRemove(characterId, out _);
    }

    public bool ClearReadyExact(SelectedSessionIdentity session)
    {
        GameplayWriteAssertion.Writing(nameof(PvpLobbyDomainService) + "." + nameof(ClearReadyExact));
        return _ready.TryGetValue(session.CharacterId, out var current)
            && current.Session == session
            && _ready.TryRemove(new KeyValuePair<CharacterId, PvpLobbyParticipant>(session.CharacterId, current));
    }

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
}
