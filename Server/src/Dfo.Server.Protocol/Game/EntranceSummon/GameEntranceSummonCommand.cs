using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.EntranceSummon;

public sealed record GameEntranceSummonCommand(NetworkDispatchContext Dispatch, EntranceSummonRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>Stages the requested summon and answers the requester under its own request id.</summary>
/// <remarks>
/// Every request is answered: an accepted one with the summoned sequences the client then reports
/// deaths by, a refused one with the request id alone so the client can drop its pending record.
/// The answer goes to the requester only -- the A21 client pairs it by its own request id, which no
/// other member holds; how other party members are shown the summon is not yet observed.
/// </remarks>
internal sealed class GameEntranceSummonCommandHandler(
    ISelectedSessionDomainService sessions,
    IDungeonEntranceDomainService entrances,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    ILogger<GameEntranceSummonCommandHandler> logger) : ICommandHandler<GameEntranceSummonCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameEntranceSummonCommand command,
        CancellationToken cancellationToken)
    {
        var connection = command.Dispatch.Connection;
        var request = command.Input.Summon;
        if (sessions.FindByConnection(connection.ConnectionId) is not { } requester || !sessions.IsInWorld(requester))
        {
            logger.LogInformation("Entrance summon {ConnectionId}: no session in world, request={RequestId}",
                connection.ConnectionId, request.RequestId);
            await outbox.RegisterCmdAsync(connection.ConnectionId, new EntranceSummonResponse(request.RequestId, []),
                cancellationToken).ConfigureAwait(false);
            return false;
        }
        var recipient = new SelectedSessionRecipient(requester, connection, command.Input.Metadata);
        var result = await units.WriteAsync(execution, new("entrance-summon", [recipient]),
            (_, token) => entrances.SummonAsync(requester, request, token), cancellationToken).ConfigureAwait(false);
        await outbox.RegisterCmdAsync(connection.ConnectionId,
            new EntranceSummonResponse(request.RequestId, result.Actors), cancellationToken).ConfigureAwait(false);
        return result.Accepted;
    }
}
