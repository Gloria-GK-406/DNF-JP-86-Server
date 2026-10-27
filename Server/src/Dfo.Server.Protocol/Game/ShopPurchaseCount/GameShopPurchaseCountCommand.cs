using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.ShopPurchaseCount;

public sealed record GameShopPurchaseCountCommand(NetworkDispatchContext Dispatch, ShopPurchaseCountRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameShopPurchaseCountCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ISelectedSessionDomainService sessions,
    INpcShopDomainService shops,
    TimeProvider clock,
    ICommandProjections notifications,
    IGameCommandOutbox sender) : ICommandHandler<GameShopPurchaseCountCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameShopPurchaseCountCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            || online is null)
        {
            await sender.RegisterCmdAsync(context.Connection.ConnectionId,
                new ShopPurchaseCountResponse(new(NpcShopRejection.NoSession, [])), cancellationToken).ConfigureAwait(false);
            return false;
        }
        var result = await shops.PurchaseCountsAsync(session, message.NpcId, clock.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new ShopPurchaseCountsReported(session, result), cancellationToken).ConfigureAwait(false);
        return true;
    }
}
