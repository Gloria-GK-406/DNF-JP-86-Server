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
