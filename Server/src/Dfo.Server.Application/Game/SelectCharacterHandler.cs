using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Game;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Application.Game;

public sealed class SelectCharacterHandler(
    IOnlineUserQueryService onlineUsers,
    INetworkMessageSender sender,
    ICommandBus commandBus) : ClientMessageHandler<SelectCharacterRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SelectCharacterRequest message,
        CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            || onlineUser?.AccountId is not { } accountId)
        {
            throw new InvalidOperationException(
                $"Game connection '{context.Connection.ConnectionId}' is not authenticated.");
        }

        var result = await commandBus.SendAsync(
            new SelectCharacterCommand(accountId, message.SlotIndex),
            cancellationToken).ConfigureAwait(false);
        await sender.SendAsync(
            context.Connection.ConnectionId,
            new SelectCharacterResponse(result),
            cancellationToken).ConfigureAwait(false);
    }
}
