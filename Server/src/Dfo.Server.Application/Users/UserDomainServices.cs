using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Users;

/// <summary>Who is connected, and the lifecycle of those connections.</summary>
internal sealed class OnlineUserDomainService(OnlineStateRegistry state) : IOnlineUserDomainService
{
    public OnlineUserSnapshot RegisterConnection(NetworkConnectionContext connection) =>
        state.RegisterConnection(connection);

    public bool TryDisconnect(
        OnlineUserId onlineUserId,
        NetworkConnectionId connectionId,
        long expectedGeneration) =>
        state.TryDisconnect(onlineUserId, connectionId, expectedGeneration);

    public bool TryDisconnectConnection(NetworkConnectionId connectionId) =>
        state.TryDisconnectConnection(connectionId);

    public OnlineUserSnapshot GetRequired(OnlineUserId onlineUserId) =>
        state.GetRequiredUser(onlineUserId);

    public bool TryGetByConnection(
        NetworkConnectionId connectionId,
        out OnlineUserSnapshot? onlineUser) =>
        state.TryGetByConnection(connectionId, out onlineUser);
}

/// <summary>The character each account currently has in play, and the session that owns it.</summary>
internal sealed class ActiveCharacterDomainService(OnlineStateRegistry state)
    : IActiveCharacterDomainService
{
    public void Publish(SelectedSessionIdentity session) => state.Publish(session);

    public bool ClearExact(SelectedSessionIdentity session) => state.ClearExact(session);
}
