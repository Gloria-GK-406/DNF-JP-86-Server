using System.Collections.Concurrent;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Sessions;

/// <summary>
/// Keeps the server's record of who is present, in memory, for its lifetime.
/// </summary>
/// <remarks>
/// Placed alongside the other in-memory runtime registries in this layer rather than in
/// Infrastructure: it holds no external technology, and the accepted online-user and
/// active-character registries already live here.
/// </remarks>
internal sealed class SessionPresenceRegistry : ISessionPresenceRegistry
{
    private readonly ConcurrentDictionary<CharacterId, SessionPresence> _byCharacter = new();

    public void Admit(SessionPresence presence)
    {
        ArgumentNullException.ThrowIfNull(presence);
        _byCharacter[presence.CharacterId] = presence;
    }

    public bool Withdraw(CharacterId characterId) => _byCharacter.TryRemove(characterId, out _);

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
