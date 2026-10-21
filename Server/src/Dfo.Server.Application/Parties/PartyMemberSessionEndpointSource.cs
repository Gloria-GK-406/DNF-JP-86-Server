using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Application.Parties;

/// <summary>Bridges a character to a UDP endpoint exclusively through its current session.</summary>
internal sealed class PartyMemberSessionEndpointSource(
    ISelectedSessionDomainService selectedSessions,
    IOnlineUserQueryDomainService onlineUsers,
    IGameSessionEndpointDomainService endpoints) : IPartyMemberSessionEndpointSource
{
    public SessionEndpoint? FindThroughCurrentSession(CharacterId characterId)
    {
        if (!selectedSessions.TryGetInWorld(characterId, out var selected)
            || !onlineUsers.TryGetByConnection(selected.ConnectionId, out var onlineUser)
            || onlineUser is null)
        {
            return null;
        }

        return endpoints.Find(onlineUser.SessionIdentity);
    }
}
