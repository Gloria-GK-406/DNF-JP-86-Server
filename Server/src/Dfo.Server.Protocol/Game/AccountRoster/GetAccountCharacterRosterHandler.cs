using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.AccountRoster;

public sealed class GetAccountCharacterRosterHandler(
    IOnlineUserQueryService onlineUsers,
    INetworkMessageSender sender,
    ICommandBus commandBus) : ClientMessageHandler<GetAccountCharacterRosterRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        GetAccountCharacterRosterRequest message,
        CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            || onlineUser?.AccountId is not { } accountId)
        {
            throw new InvalidOperationException(
                $"Game connection '{context.Connection.ConnectionId}' is not authenticated.");
        }

        var roster = await commandBus.SendAsync(
            new GetAccountCharacterRosterCommand(accountId),
            cancellationToken).ConfigureAwait(false);
        await sender.SendAsync(
            context.Connection.ConnectionId,
            new AccountCharacterRosterResponse(roster),
            cancellationToken).ConfigureAwait(false);
    }
}
