using Dfo.Server.Application.Commands;
namespace Dfo.Server.Protocol.Game;
internal static class TimerRecipientCapture
{
    internal static IReadOnlyList<SelectedSessionRecipient> Capture(IReadOnlyList<Dfo.Server.Data.World.SelectedSessionIdentity> cohort,
        Dfo.Server.Abstractions.OnlineUsers.IOnlineUserQueryDomainService users) => cohort.Select((member, index) =>
            new SelectedSessionRecipient(member, index == 0 && users.TryGetByConnection(member.ConnectionId, out var user)
                && user is not null && user.AccountId == member.AccountId && user.CurrentCharacterId == member.CharacterId ? user.Connection : null)).ToArray();
}
