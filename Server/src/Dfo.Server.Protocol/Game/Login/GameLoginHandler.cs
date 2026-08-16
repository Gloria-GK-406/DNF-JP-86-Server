using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Login;

public sealed class GameLoginHandler(
    IOnlineUserQueryService onlineUsers,
    IGameMessageSender sender,
    ICommandBus commandBus,
    IServerConfigService serverConfig) : ClientMessageHandler<GameLoginRequest>
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

        var response = new GameLoginResponse(
            Succeeded: true,
            serverConfig.GetConfig<byte>(GameLoginConfigKeys.Environment),
            serverConfig.GetConfig<int>(GameLoginConfigKeys.ConnectionPort),
            serverConfig.GetConfig<string>(GameLoginConfigKeys.EndpointHost),
            serverConfig.GetConfig<int>(GameLoginConfigKeys.EndpointPortA),
            serverConfig.GetConfig<int>(GameLoginConfigKeys.EndpointPortB));
        await commandBus.SendAsync(
            new LoginGameUserCommand(
                onlineUser.OnlineUserId,
                message.LoginId,
                message.PasswordHash),
            cancellationToken).ConfigureAwait(false);
        await sender.SendCmdAsync(
            context.Connection.ConnectionId,
            response,
            cancellationToken).ConfigureAwait(false);
    }
}
