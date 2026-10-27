using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.AccountRoster;

public readonly record struct GameOtherCharacterDetailCompleted;
public sealed record GameOtherCharacterDetailCommand(NetworkDispatchContext Dispatch,
    GetOtherCharacterDetailRequest Request) : IProtocolCommand<GameOtherCharacterDetailCompleted>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

public sealed class GameOtherCharacterDetailCommandHandler(
    ISelectedSessionDomainService sessions,
    IOtherCharacterDetailQueryDomainService details,
    ICommandProjections projections,
    ILogger<GameOtherCharacterDetailCommandHandler> logger)
    : ICommandHandler<GameOtherCharacterDetailCommand, GameOtherCharacterDetailCompleted>
{
    public async ValueTask<GameOtherCharacterDetailCompleted> HandleAsync(CommandExecutionContext context,
        GameOtherCharacterDetailCommand command, CancellationToken cancellationToken)
    {
        var requester = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (requester is null)
        {
            logger.LogInformation(
                "GET_USERINFO mode=3 ignored for non-current connection {ConnectionId}",
                command.Dispatch.Connection.ConnectionId);
            return default;
        }

        if (!sessions.TryGetInWorld(command.Request.TargetWireUserId, out var target))
        {
            logger.LogInformation(
                "GET_USERINFO mode=3 requester={Requester} target={Target} refused={Failure}",
                requester.WireUserId,
                command.Request.TargetWireUserId,
                CharacterDetailQueryFailure.TargetNotCurrent);
            return default;
        }

        CharacterDetailQueryResult result;
        if (!sessions.IsInWorld(requester))
        {
            result = CharacterDetailQueryResult.Reject(CharacterDetailQueryFailure.RequesterNotCurrent);
        }
        else if (!sessions.IsInWorld(target))
        {
            result = CharacterDetailQueryResult.Reject(CharacterDetailQueryFailure.TargetNotCurrent);
        }
        else
        {
            result = await details.QueryAsync(requester, command.Request.TargetWireUserId,
                cancellationToken).ConfigureAwait(false);
        }

        if (result.Projection is { } projection)
        {
            await projections.ProjectAsync(
                new NotificationAudience.TheseCharacters([requester.CharacterId]),
                new OtherCharacterDetailRequested(projection),
                cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "GET_USERINFO mode=3 requester={Requester} target={Target} accepted={Accepted} failure={Failure}",
            requester.WireUserId,
            command.Request.TargetWireUserId,
            result.Succeeded,
            result.Failure);
        return default;
    }
}
