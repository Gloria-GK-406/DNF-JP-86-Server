using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
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
    ISelectedSessionDomainService sessions,
    IAdventureGroupDomainService adventureGroups,
    ILogger<MercenaryInfoHandler> logger) : ICommandHandler<GameMercenaryInfoCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameMercenaryInfoCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        // The account's adventure group when the connection has a selected character; nothing to
        // count otherwise.
        var adventureGroup = sessions.FindByConnection(context.Connection.ConnectionId) is { } session
            ? await adventureGroups.ReadAsync(session.AccountId, cancellationToken).ConfigureAwait(false)
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
