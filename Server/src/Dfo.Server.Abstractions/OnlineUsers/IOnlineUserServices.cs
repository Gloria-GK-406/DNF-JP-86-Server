using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Abstractions.OnlineUsers;

public interface IOnlineUserQueryDomainService
{
    OnlineUserSnapshot GetRequired(OnlineUserId onlineUserId);

    bool TryGetByConnection(
        NetworkConnectionId connectionId,
        out OnlineUserSnapshot? onlineUser);
}

public interface IOnlineUserDomainService : IOnlineUserQueryDomainService
{
    OnlineUserSnapshot RegisterConnection(NetworkConnectionContext connection);

    bool TryDisconnect(
        OnlineUserId onlineUserId,
        NetworkConnectionId connectionId,
        long expectedGeneration);

    bool TryDisconnectConnection(NetworkConnectionId connectionId);
}

/// <summary>Owns the UDP endpoint attached to one exact game-connection session.</summary>
/// <remarks>
/// Character selection is intentionally absent from this boundary. Party and dungeon projections
/// reach an endpoint only by first resolving the character's current session.
/// </remarks>
public interface IGameSessionEndpointDomainService
{
    bool TryObserve(GameSessionIdentity session, SessionEndpoint endpoint);

    SessionEndpoint? Find(GameSessionIdentity session);
}
