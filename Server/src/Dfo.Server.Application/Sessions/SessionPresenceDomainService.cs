using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
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
internal sealed class SessionPresenceDomainService : ISessionPresenceDomainService
{
    private readonly ConcurrentDictionary<CharacterId, SessionPresence> _byCharacter = new();

    public void Admit(SessionPresence presence)
    {
        ArgumentNullException.ThrowIfNull(presence);
        _byCharacter[presence.CharacterId] = presence;
    }

    public bool Withdraw(CharacterId characterId) => _byCharacter.TryRemove(characterId, out _);

    public bool WithdrawExact(SelectedSessionIdentity session) =>
        _byCharacter.TryGetValue(session.CharacterId, out var current)
        && current.Session == session
        && _byCharacter.TryRemove(new KeyValuePair<CharacterId, SessionPresence>(session.CharacterId, current));

    public bool RelocateExact(
        SelectedSessionIdentity session,
        byte townId,
        byte areaId,
        bool standingInTown,
        bool sharesTownPresence = true)
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
                return true;
        }

        return false;
    }

    public SessionPresence? Find(CharacterId characterId) =>
        _byCharacter.TryGetValue(characterId, out var presence) ? presence : null;

    public IReadOnlyList<SessionPresence> InArea(
        byte townId,
        byte areaId,
        CharacterId excluding,
        int? listenerPort = null)
    {
        var occupants = new List<SessionPresence>();
        foreach (var presence in _byCharacter.Values)
        {
            if (presence.CharacterId == excluding)
                continue;
            // A record whose owner is no longer standing there still holds their last
            // coordinates. Reporting them would place someone in a dungeon next to the
            // arriving character.
            if (!presence.StandingInTown)
                continue;
            // A gate is a per-character Seria room. Equal town/area bytes do not make
            // two such records co-occupants of one visible space.
            if (!presence.SharesTownPresence)
                continue;
            if (listenerPort is { } port && presence.ListenerPort != port)
                continue;
            if (presence.TownId != townId || presence.AreaId != areaId)
                continue;

            occupants.Add(presence);
        }

        // Ordered so a projection that must preserve a stable sequence has one to preserve;
        // the dictionary's own order is not stable across enumerations.
        occupants.Sort(static (left, right) => left.CharacterId.Value.CompareTo(right.CharacterId.Value));
        return occupants;
    }
}
