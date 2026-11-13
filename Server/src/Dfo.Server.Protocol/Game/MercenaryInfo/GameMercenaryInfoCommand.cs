using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Game.MercenaryInfo;

public sealed record GameMercenaryInfoCommand(NetworkDispatchContext Dispatch, MercenaryInfoRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameMercenaryInfoCommandHandler(
    IGameCommandOutbox sender,
    IOnlineUserQueryDomainService onlineUsers,
    IAdventureGroupDomainService adventureGroups,
    ILogger<MercenaryInfoHandler> logger) : ICommandHandler<GameMercenaryInfoCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameMercenaryInfoCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        // The client asks on the roster screen as well as in town, so the answer is the
        // authenticated account's, with or without a selected character. Nothing to count for a
        // connection that has not logged in.
        var adventureGroup = onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online)
            && online?.AccountId is { } accountId
                ? await adventureGroups.ReadAsync(accountId, cancellationToken).ConfigureAwait(false)
                : AdventureGroupStanding.None;
        logger.LogInformation(
            "Mercenary info {ConnectionId}: adventure group level {Level}, {Point} points, empty mercenary roster",
            context.Connection.ConnectionId, adventureGroup.Level, adventureGroup.Point);
        await sender.RegisterCmdAsync(
            context.Connection.ConnectionId,
            new MercenaryInfoResponse(adventureGroup),
            cancellationToken).ConfigureAwait(false);
        return true;
    }
}
