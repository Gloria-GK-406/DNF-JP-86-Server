using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Abstractions.OnlineUsers;

public interface IOnlineUserQueryService
{
    OnlineUserSnapshot GetRequired(OnlineUserId onlineUserId);

    bool TryGetByConnection(
        NetworkConnectionId connectionId,
        out OnlineUserSnapshot? onlineUser);
}

public interface IOnlineUserService : IOnlineUserQueryService
{
    OnlineUserSnapshot RegisterConnection(NetworkConnectionContext connection);

    OnlineUserSnapshot Authenticate(OnlineUserId onlineUserId, AccountId accountId);

    bool TryDisconnect(
        OnlineUserId onlineUserId,
        NetworkConnectionId connectionId,
        long expectedGeneration);
}
