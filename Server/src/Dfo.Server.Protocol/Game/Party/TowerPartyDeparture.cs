using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Party;

/// <summary>
/// The party side of leaving a tower, for either engine: the tower writes a party command makes
/// and the projections that follow a member's departure or a change of host.
/// </summary>
/// <remarks>
/// Each command handler that moves a party out of a tower holds one, over its own close targets
/// and its own town return.
/// </remarks>
internal sealed class TowerPartyDeparture(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ICharacterDomainService characters, TimeProvider clock, IGameCommandOutbox outbox,
    ISelectedCharacterCurrentViewDomainService currentViews, ICharacterStateProjectionDomainService currentState,
    IOnlineCharacterRuntimeDomainService onlineCharacters, ICommandWorkUnits workUnits,
    ICommandConnectionClosures closures, ExactSessionClosure closeTargets, ILogger logger,
    Func<IReadOnlyList<TownAreaEntry>, List<(SelectedSessionIdentity, Exception)>, CancellationToken, ValueTask> returnTown)
{
    internal ValueTask<ITowerActionOutcome> LeaveAsync(CommandExecutionContext execution, ITowerRunKind kind,
        TowerActionContext context, IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken token) =>
        workUnits.WriteAsync(execution, new($"party-leave-{kind.WorkLabel}-tower", Recipients(kind, participants, context.Session),
                Footprint: CommandFootprintMode.DeclaredNonRequest, FootprintOwners: participants),
            (unit, ct) => kind.LeaveAsync(context, unit.Footprint, ct), token);

    internal ValueTask<ITowerActionOutcome> SynchronizeLeaderAsync(CommandExecutionContext execution, ITowerRunKind kind,
        TowerActionContext context, SelectedSessionIdentity leader, IReadOnlyList<SelectedSessionIdentity> participants,
        CancellationToken token) =>
        workUnits.WriteAsync(execution, new($"party-synchronize-{kind.WorkLabel}-leader", Recipients(kind, participants, context.Session)),
            (_, ct) => kind.SynchronizeLeaderAsync(context, leader, ct), token);

    private ValueTask<ITowerActionOutcome> StartAsync(CommandExecutionContext execution, ITowerRunKind kind,
        ITowerRunSnapshot run, SelectedSessionIdentity leader, CancellationToken token) =>
        workUnits.WriteAsync(execution, new($"party-start-{kind.WorkLabel}-tower", Recipients(kind, TowerActionProjection.Members(run), leader)),
            (_, ct) => kind.StartAsync(TowerActionProjection.Context(run, leader), ct), token);

    /// <summary>A member has left the run; tell the rest who hosts, resume a ready room, settle, and send the member home.</summary>
    internal async ValueTask ProjectDepartureAsync(CommandExecutionContext execution, ITowerRunKind kind,
        ITowerActionOutcome action, SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token)
    {
        if (!action.Changed || action.Run is null) return;
        var captured = action.Run;
        closeTargets.Capture(sessions, onlineUsers, captured.Members.Select(member => member.Session).Append(returning).OfType<SelectedSessionIdentity>());
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var exact = TowerActionProjection.ExactMembers(sessions, kind, captured);
        var current = TowerActionProjection.CurrentRun(sessions, kind, captured);
        if (exact.Count > 0 && current?.Members.SingleOrDefault(member => member.IsLeader) is { } leader)
        {
            failures.AddRange(await TowerActionProjection.SendToExactAsync(sessions, kind, outbox,
                current, [new UdpHostNotification(leader.Slot)], token).ConfigureAwait(false));
        }
        if (kind.StartsReadyRoomOnDeparture)
        {
            current = TowerActionProjection.CurrentRun(sessions, kind, captured);
            if (current is not null && TowerActionProjection.ExactMembers(sessions, kind, current).Count == current.Members.Length
                && current.Room.Phase == TowerRoomPhase.Ready
                && current.Members.SingleOrDefault(member => member.IsLeader) is { } readyLeader)
            {
                var start = await StartAsync(execution, kind, current, readyLeader.Session, token).ConfigureAwait(false);
                if (start.Run is { Room.Phase: TowerRoomPhase.Fighting } fighting)
                    failures.AddRange(await TowerActionProjection.SendToExactAsync(sessions, kind, outbox, fighting, [new LoadingCompleteNotification()], token).ConfigureAwait(false));
            }
        }
        else if (exact.Count > 0 && captured.Room.Phase == TowerRoomPhase.Ready)
            failures.AddRange(await TowerActionProjection.SendToExactAsync(sessions, kind, outbox,
                captured, [new LoadingCompleteNotification()], token).ConfigureAwait(false));
        if (exact.Count > 0 && action.SettlementCreated && captured.Completion is { Settled: true })
            failures.AddRange(await TowerActionProjection.ProjectActionAsync(sessions, characters, kind, clock, outbox, action, null, token).ConfigureAwait(false));
        if (returning is not null
            && sessions.IsInWorld(returning)
            && !kind.IsInTower(returning.CharacterId))
        {
            var entries = await TowerActionProjection.CaptureReturnEntriesAsync(sessions, currentViews, currentState, onlineCharacters, outbox,
                [returning], response, failures, token, kind.RestoresInventoryOnReturn(captured)).ConfigureAwait(false);
            await returnTown(entries, failures, token).ConfigureAwait(false);
        }
        await TowerActionProjection.CloseProjectionFailuresAsync(sessions, kind, logger, closures, closeTargets, failures, captured.RunId).ConfigureAwait(false);
    }

    /// <summary>No member left, but the host may have changed; tell the run who hosts now.</summary>
    internal async ValueTask ProjectHostAsync(ITowerRunKind kind, ITowerRunSnapshot captured, CancellationToken token)
    {
        if (captured.Members.IsEmpty)
            return;
        closeTargets.Capture(sessions, onlineUsers, captured.Members.Select(member => member.Session));
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var current = TowerActionProjection.CurrentRun(sessions, kind, captured);
        if (current?.Members.SingleOrDefault(member => member.IsLeader) is { } leader)
        {
            failures.AddRange(await TowerActionProjection.SendToExactAsync(sessions, kind, outbox,
                current, [new UdpHostNotification(leader.Slot)], token).ConfigureAwait(false));
        }
        await TowerActionProjection.CloseProjectionFailuresAsync(sessions, kind, logger, closures, closeTargets, failures, captured.RunId).ConfigureAwait(false);
    }

    /// <summary>The reporter with its physical connection, the other members by identity alone.</summary>
    private SelectedSessionRecipient[] Recipients(ITowerRunKind kind, IReadOnlyList<SelectedSessionIdentity> participants,
        SelectedSessionIdentity reporter) => kind.NamesWorkRecipients
        ? participants.Select(session => session == reporter
            ? GameParticipantCapture.Capture(onlineUsers, session) : new SelectedSessionRecipient(session, null)).ToArray()
        : [];
}
