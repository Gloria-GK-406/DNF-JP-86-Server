using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CardReward;
using Microsoft.Extensions.Logging;

using Dfo.Server.Application.Commands;
namespace Dfo.Server.Protocol.Game.DungeonClear;
internal class GameBossDieCheckCommandHandler(
    ISelectedSessionDomainService selectedSessions,
    IDungeonRunDomainService runs,
    ILogger<GameBossDieCheckCommandHandler> logger) : ICommandHandler<GameBossDieCheckCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameBossDieCheckCommand command, CancellationToken ct)
    { await RunAsync(command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    protected ValueTask RunAsync(
        NetworkDispatchContext context,
        BossDieCheckRequest message,
        CancellationToken cancellationToken)
    {
        var session = selectedSessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null)
        {
            logger.LogWarning(
                "Boss die check {ConnectionId}: no published session, ignoring",
                context.Connection.ConnectionId);
            return ValueTask.CompletedTask;
        }

        var run = runs.Find(session.CharacterId);
        if (run is null)
        {
            logger.LogWarning(
                "Boss die check {ConnectionId} character={CharacterId}: no run in progress, "
                    + "ignoring",
                context.Connection.ConnectionId,
                session.CharacterId.Value);
            return ValueTask.CompletedTask;
        }

        var agrees = run.CurrentCellState.FindActor(message.ReportedActorSequence)?.IsDead == true;

        logger.LogInformation(
            "Boss die check {ConnectionId} character={CharacterId}: actor {Actor} of room {RoomId} "
                + "reported by user {User}; boss room={IsBossRoom} already dead here={Agrees}. "
                + "Nothing answered, nothing changed",
            context.Connection.ConnectionId,
            session.CharacterId.Value,
            message.ReportedActorSequence,
            run.CurrentRoomMapId,
            message.ReportedUserId,
            run.IsBossRoom,
            agrees);

        return ValueTask.CompletedTask;
    }
}
