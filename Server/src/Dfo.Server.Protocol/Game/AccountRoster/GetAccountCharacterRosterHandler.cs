using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.HonorLevel;

namespace Dfo.Server.Protocol.Game.AccountRoster;

public sealed class GetAccountCharacterRosterHandler(
    IOnlineUserQueryService onlineUsers,
    IGameMessageSender sender,
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
        await sender.SendNotiAsync(
            context.Connection.ConnectionId,
            new AccountCharacterRosterResponse(roster),
            cancellationToken).ConfigureAwait(false);

        // The roster screen expects honour standing alongside the roster itself; the legacy server
        // sent both from this one request. There is no honour subsystem yet, so the standing is
        // reported as none rather than omitted -- see HonorLevelInfoNotice for why sending it is
        // safe even though the client's handling of it is uncorroborated.
        await sender.SendNotiAsync(
            context.Connection.ConnectionId,
            new HonorLevelInfoNotice(AccountHonorLevel.None),
            cancellationToken).ConfigureAwait(false);
    }
}
