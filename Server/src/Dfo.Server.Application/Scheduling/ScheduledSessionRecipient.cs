using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
namespace Dfo.Server.Application.Scheduling;
internal static class ScheduledSessionRecipient
{
    internal static SelectedSessionRecipient Capture(SelectedSessionIdentity session, IOnlineUserQueryDomainService users)
    {
        if (!users.TryGetByConnection(session.ConnectionId, out var user) || user is null) return new(session, null);
        if (user.AccountId != session.AccountId || user.CurrentCharacterId != session.CharacterId)
            throw new InvalidOperationException("The scheduled session no longer owns its exact connection.");
        return new(session, user.Connection);
    }
}
