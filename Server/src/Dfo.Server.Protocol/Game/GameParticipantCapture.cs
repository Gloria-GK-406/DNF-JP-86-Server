using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game;

internal static class GameParticipantCapture
{
    internal static SelectedSessionRecipient Capture(IOnlineUserQueryDomainService users, SelectedSessionIdentity session)
    {
        NetworkConnectionContext? connection = null;
        if (users.TryGetByConnection(session.ConnectionId, out var user) && user is not null)
        {
            if (user.AccountId != session.AccountId || user.CurrentCharacterId != session.CharacterId)
                throw new InvalidOperationException("The tower participant connection no longer owns the captured character.");
            connection = user.Connection;
        }
        return new(session, connection);
    }
}
