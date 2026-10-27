using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.WeeklyAttendance;

public sealed record GameWeeklyAttendanceCommand(NetworkDispatchContext Dispatch, WeeklyAttendanceRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameWeeklyAttendanceCommandHandler(ISelectedSessionDomainService sessions,
    IWeeklyAttendanceDomainService attendance, TimeProvider clock, ICommandWorkUnits units, ICommandProjections projections)
    : ICommandHandler<GameWeeklyAttendanceCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameWeeklyAttendanceCommand command, CancellationToken ct)
    {
        if (sessions.FindByConnection(command.Dispatch.Connection.ConnectionId) is not { } session) return false;
        var result = await units.WriteAsync(execution, new("ClaimWeeklyAttendance", [new(session, command.Dispatch.Connection, command.Input.Metadata)]),
            (_, token) => attendance.ClaimAsync(session.AccountId, session.CharacterId, clock.GetUtcNow(), token), ct).ConfigureAwait(false);
        await projections.ProjectAsync(new NotificationAudience.TheseSessions([session]), new AttendanceCompleted(session, result.Succeeded), ct).ConfigureAwait(false);
        await projections.ProjectAsync(new NotificationAudience.TheseSessions([session]), new EventsChanged(), ct).ConfigureAwait(false);
        return result.Succeeded;
    }
}
