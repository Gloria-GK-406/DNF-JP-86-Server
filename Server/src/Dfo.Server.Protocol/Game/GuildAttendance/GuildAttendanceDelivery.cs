using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.GuildInfo;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.GuildAttendance;

internal sealed class GuildAttendanceDelivery(IGuildAttendanceDomainService attendance,
    IGuildMonthlyContributionDomainService monthly, IGuildOverviewDomainService overviews, ICommandWorkUnits units,
    ISelectedSessionDomainService sessions, ICommandProjections projections, IGameCommandOutbox outbox,
    TimeProvider clock, ILogger logger)
{
    public async ValueTask RecordAsync(
        CommandExecutionContext execution, SelectedSessionIdentity session, CancellationToken ct)
    {
        if (!sessions.IsInWorld(session))
            return;

        Exception? actionFailure = null;
        var write = await units.WriteResultAsync(execution, new("RecordGuildAttendance", []), async (unit, token) =>
        {
            try
            {
                var now = clock.GetUtcNow();
                var attended = await attendance.RecordAttendanceAsync(session.CharacterId, now, token).ConfigureAwait(false);
                var contributed = await monthly.RecordAttendanceAsync(session.CharacterId, now, token).ConfigureAwait(false);
                return attended || contributed;
            }
            catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
            {
                actionFailure = error;
                unit.RequireRollback(error);
                return false;
            }
        }, ct).ConfigureAwait(false);
        var failure = write is CommandWriteResult<bool>.CommitRolledBack rollback ? rollback.Cause : actionFailure;
        if (failure is not null)
        {
            logger.LogWarning(failure, "Guild attendance could not be recorded for character {Character}", session.CharacterId.Value);
            return;
        }
        if (write is not CommandWriteResult<bool>.Returned changed)
            return;

        var panel = await units.ReadOnlyAsync(execution, new("ReadGuildAttendancePanel", []), async token =>
        {
            try
            {
                return await GuildInfoPanel.ReadAsync(overviews, session.CharacterId, token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "Guild attendance was recorded but the panel could not be read for character {Character}",
                    session.CharacterId.Value);
                return GuildInfoPanel.Empty;
            }
        }, ct).ConfigureAwait(false);
        if (changed.Value && panel.Members.Count != 0)
        {
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters(
                panel.Members.Select(member => member.Character).ToArray()), panel, ct).ConfigureAwait(false);
        }
        else if (panel.FindMember(session.CharacterId) is { } member)
        {
            await outbox.RegisterNotiAsync(session.ConnectionId, panel.CreateMessage(member), ct).ConfigureAwait(false);
            await outbox.RegisterNotiAsync(session.ConnectionId,
                new GuildHallInfoMessage(GuildHallTown.InitialEntranceStage), ct).ConfigureAwait(false);
        }
    }
}
