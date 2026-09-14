using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Sessions;

/// <summary>
/// Keeps the server's record of who is present, in memory, for its lifetime.
/// </summary>
/// <remarks>
/// Placed alongside the in-memory runtime state this layer already holds rather than in
/// Infrastructure: it holds no external technology, and the accepted online-user and
/// active-character state already lives here.
/// </remarks>
internal sealed class SessionPresenceDomainService(IPartyDirectoryPresenceSink? parties = null)
    : ISessionPresenceDomainService
{
    private readonly ConcurrentDictionary<CharacterId, SessionPresence> _byCharacter = new();
    private readonly ConcurrentDictionary<CharacterId, object> _ordering = new();

    public void Admit(SessionPresence presence)
    {
        ArgumentNullException.ThrowIfNull(presence);
        lock (OrderingGate(presence.CharacterId))
        {
            _byCharacter[presence.CharacterId] = presence;
            parties?.UpdateDirectoryPresence(presence.CharacterId, DirectoryScopeOf(presence));
        }
    }

    public bool Withdraw(CharacterId characterId)
    {
        lock (OrderingGate(characterId))
        {
            if (!_byCharacter.TryRemove(characterId, out _))
                return false;
            parties?.UpdateDirectoryPresence(characterId, null);
            return true;
        }
    }

    public bool WithdrawExact(SelectedSessionIdentity session)
    {
        lock (OrderingGate(session.CharacterId))
        {
            if (!_byCharacter.TryGetValue(session.CharacterId, out var current)
                || current.Session != session
                || !_byCharacter.TryRemove(
                    new KeyValuePair<CharacterId, SessionPresence>(session.CharacterId, current)))
            {
                return false;
            }
            parties?.UpdateDirectoryPresence(session.CharacterId, null);
            return true;
        }
    }

    public bool RelocateExact(
        SelectedSessionIdentity session,
        byte townId,
        byte areaId,
        bool standingInTown,
        bool sharesTownPresence = true)
    {
        lock (OrderingGate(session.CharacterId))
        {
            while (_byCharacter.TryGetValue(session.CharacterId, out var current))
            {
                if (current.Session != session)
                    return false;
                var moved = current with
                {
                    TownId = townId,
                    AreaId = areaId,
                    StandingInTown = standingInTown,
                    SharesTownPresence = sharesTownPresence,
                };
                if (_byCharacter.TryUpdate(session.CharacterId, moved, current))
                {
                    parties?.UpdateDirectoryPresence(session.CharacterId, DirectoryScopeOf(moved));
                    return true;
                }
            }

            return false;
        }
    }

    public SessionPresence? Find(CharacterId characterId)
    {
        lock (OrderingGate(characterId))
            return _byCharacter.TryGetValue(characterId, out var presence) ? presence : null;
    }

    public IReadOnlyList<SessionPresence> Query(SessionPresenceScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var flags = scope.Flags;
        var occupants = new List<SessionPresence>();
        foreach (var characterId in _byCharacter.Keys)
        {
            SessionPresence presence;
            lock (OrderingGate(characterId))
            {
                if (!_byCharacter.TryGetValue(characterId, out presence!))
                    continue;
            }
            if (presence.CharacterId == scope.ExcludingCharacterId)
                continue;
            if ((flags & SessionPresenceScopeFlags.SameChannel) != 0
                && presence.ListenerPort != scope.ListenerPort)
                continue;
            if ((flags & SessionPresenceScopeFlags.SameLogicalChannel) != 0
                && presence.ChannelId != scope.ChannelId)
                continue;
            if ((flags & SessionPresenceScopeFlags.SameTown) != 0
                && presence.TownId != scope.TownId)
                continue;
            if ((flags & SessionPresenceScopeFlags.SameArea) != 0
                && presence.AreaId != scope.AreaId)
                continue;
            if ((flags & SessionPresenceScopeFlags.StandingInTown) != 0
                && !presence.StandingInTown)
                continue;
            if ((flags & SessionPresenceScopeFlags.SharesTownPresence) != 0
                && !presence.SharesTownPresence)
                continue;
            occupants.Add(presence);
        }

        occupants.Sort(static (left, right) => left.CharacterId.Value.CompareTo(right.CharacterId.Value));
        return occupants;
    }

    private static PartyDirectoryScope? DirectoryScopeOf(SessionPresence presence) =>
        presence.StandingInTown && presence.SharesTownPresence && presence.ChannelId > 0
            ? new PartyDirectoryScope(presence.ChannelId, presence.TownId, presence.AreaId)
            : null;

    private object OrderingGate(CharacterId characterId) =>
        _ordering.GetOrAdd(characterId, static _ => new object());

}
