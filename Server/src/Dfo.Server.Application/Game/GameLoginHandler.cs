using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Game;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Game;

public sealed class GameLoginHandler(
    IOnlineUserQueryService onlineUsers,
    INetworkMessageSender sender,
    ICommandBus commandBus) : ClientMessageHandler<GameLoginRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        GameLoginRequest message,
        CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            || onlineUser is null)
        {
            throw new InvalidOperationException(
                $"Game connection '{context.Connection.ConnectionId}' has no online-user registration.");
        }

        await commandBus.SendAsync(
            new LoginGameUserCommand(
                onlineUser.OnlineUserId,
                message.LoginId,
                message.PasswordHash),
            cancellationToken).ConfigureAwait(false);
        await sender.SendAsync(
            context.Connection.ConnectionId,
            new GameLoginResponse(true),
            cancellationToken).ConfigureAwait(false);
    }
}
