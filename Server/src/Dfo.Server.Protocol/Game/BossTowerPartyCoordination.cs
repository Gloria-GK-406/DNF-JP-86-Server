using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game;

public sealed record BossTowerPartyAttempt(
    bool Handled,
    PartyOperationResult Result,
    SelectedSessionIdentity? Target = null);

/// <summary>
/// Coordinates the one non-atomic boundary between durable tower work and process-local party work.
/// </summary>
public sealed class BossTowerPartyCoordinator(
    IBossTowerDomainService towers,
    IPartyService parties,
    ISelectedSessionDomainService sessions,
    SelectedSessionTransitionGate transitions,
    BossTowerProjectionGate projectionGate,
    ICommandBus commands,
    PartyNotificationDelivery notifications,
    BossTowerProjectionDelivery towerProjection,
    INetworkConnectionCloser connections,
    ITownAreaDomainService townAreas,
    ILogger<BossTowerPartyCoordinator> logger)
{
    private const int RetryLimit = 8;

    public ValueTask<BossTowerPartyAttempt> TryLeaveAsync(
        SelectedSessionIdentity caller,
        bool allowVirtual,
        IGameServerMessage? departureResponse,
        CancellationToken token) =>
        TryCoordinateAsync(PartyChange.Leave, caller, null, allowVirtual,
            departureResponse, token);

    public ValueTask<BossTowerPartyAttempt> TryRemoveAsync(
        SelectedSessionIdentity caller,
        byte targetSlot,
        CancellationToken token) =>
        TryCoordinateAsync(PartyChange.Remove, caller, targetSlot, false, null, token);

    public ValueTask<BossTowerPartyAttempt> TryTransferAsync(
        SelectedSessionIdentity caller,
        byte targetSlot,
        CancellationToken token) =>
        TryCoordinateAsync(PartyChange.Transfer, caller, targetSlot, false, null, token);

    /// <summary>
    /// Rechecks the ordinary path while its selected-session leases are held. A true result is only
    /// a reroute signal; callers must release those leases before entering tower run ordering.
    /// </summary>
    public bool HasInvolvedTower(SelectedSessionIdentity caller, byte? targetSlot = null) =>
        Capture(caller, targetSlot).Runs.Count != 0;

    private async ValueTask<BossTowerPartyAttempt> TryCoordinateAsync(
        PartyChange change,
        SelectedSessionIdentity caller,
        byte? targetSlot,
        bool allowVirtual,
        IGameServerMessage? departureResponse,
        CancellationToken token)
    {
        for (var attempt = 0; attempt < RetryLimit; attempt++)
        {
            var observed = Capture(caller, targetSlot);
            if (observed.Runs.Count == 0)
                return NotHandled();
            if (observed.Runs.Count != 1)
                return Rejected(observed.Target, PartyFailureReason.PartyGenerationChanged);

            var expectedRun = observed.Runs[0];
            await using var runLease = new OnceAsyncDisposable(await projectionGate.AcquireAsync(
                expectedRun.RunId, UserAccessMode.Exclusive, token).ConfigureAwait(false));

            var underRun = Capture(caller, targetSlot);
            if (underRun.Runs.Count != 1 || underRun.Runs[0].RunId != expectedRun.RunId)
                continue;

            var accountIds = underRun.Party.Members
                .Select(member => member.Character.AccountId)
                .Concat(underRun.Runs[0].Members.Select(member => member.Session.AccountId))
                .Append(caller.AccountId)
                .Concat(underRun.Target is null ? [] : [underRun.Target.AccountId])
                .Distinct()
                .OrderBy(accountId => accountId.Value)
                .ToArray();

            await using var accountLeases = new LeaseSet();
            foreach (var account in accountIds)
                accountLeases.Add(await transitions.AcquireAsync(account, UserAccessMode.Exclusive, token).ConfigureAwait(false));
            var locked = Capture(caller, targetSlot);
            if (!SameCapture(underRun, locked) || !ExactSessions(caller, locked))
                continue;

            var validation = Validate(change, caller, locked, allowVirtual);
            if (validation is not null)
                return Rejected(locked.Target, validation.Value);

            if (change == PartyChange.Transfer && locked.Target == caller)
                return new(true, PartyOperationResult.Success(), caller);

            BossTowerActionResult? departure = null;
            BossTowerSnapshot projectionRun = locked.Runs[0];
            var run = locked.Runs[0];
            var departing = change switch
            {
                PartyChange.Leave => caller,
                PartyChange.Remove => locked.Target,
                _ => null,
            };
            var departingMember = departing is null
                ? null
                : run.Members.SingleOrDefault(member => member.Session == departing);

            // Durable tower work is deliberately completed before the in-memory PartyService write.
            // Any predictable party refusal has already been resolved above while the full account
            // set is stable.
            if (departingMember is not null)
            {
                departure = await commands.SendAsync(
                    new LeaveBossTowerCommand(
                        new BossTowerActionContext(departingMember.Session, run.RunId, run.Room.Version),
                        Disconnected: false) { Sessions = run.Members.Select(value => value.Session).ToArray() },
                    token).ConfigureAwait(false);
                if (!departure.Changed || departure.Run is null)
                    return Rejected(locked.Target, PartyFailureReason.CallerNotCurrent);
                projectionRun = departure.Run;
            }
            else if (LeaderToSynchronize(change, caller, locked) is { } nextLeader)
            {
                var synchronized = await commands.SendAsync(
                    new SynchronizeBossTowerLeaderCommand(
                        new BossTowerActionContext(nextLeader, run.RunId, run.Room.Version), nextLeader) { Sessions = run.Members.Select(value => value.Session).ToArray() },
                    token).ConfigureAwait(false);
                if (synchronized.Run is not null)
                    projectionRun = synchronized.Run;
            }

            PartyOperationResult partyResult;
            PartyNotificationRecipientPlan recipients;
            Exception? secondPhaseFailure = null;
            if (locked.Party.IsVirtual)
            {
                partyResult = PartyOperationResult.Success();
                recipients = PartyNotificationRecipientPlan.Empty;
            }
            else
            {
                try
                {
                    // There must be no request-cancellation point or fresh business read between the
                    // committed tower action and this immediate process-local mutation.
                    partyResult = await SendPartyCommandAsync(change, caller, locked.Target,
                        CancellationToken.None).ConfigureAwait(false);
                    if (!partyResult.Succeeded)
                    {
                        secondPhaseFailure = new InvalidOperationException(
                            $"Validated party mutation was refused after tower commit: {partyResult.Failure}.");
                        recipients = PartyNotificationRecipientPlan.Empty;
                    }
                    else
                    {
                        recipients = PartyNotificationRecipientCapture.Capture(
                            partyResult.Notifications, sessions, townAreas);
                    }
                }
                catch (Exception error)
                {
                    partyResult = PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent);
                    recipients = PartyNotificationRecipientPlan.Empty;
                    secondPhaseFailure = error;
                }
            }

            var affected = locked.Party.Members
                .Select(member => sessions.FindByCharacter(member.CharacterId))
                .Concat(run.Members.Select(member => member.Session))
                .Append(caller)
                .Append(locked.Target)
                .OfType<SelectedSessionIdentity>()
                .Distinct()
                .ToArray();

            // Dispose account and run leases before any transport work, tower re-projection, or
            // connection close. The deferred continuation below owns no mutable gate.
            await accountLeases.DisposeAsync().ConfigureAwait(false);
            await runLease.DisposeAsync().ConfigureAwait(false);

            if (secondPhaseFailure is not null)
            {
                logger.LogError(secondPhaseFailure,
                    "Party mutation failed after tower work for run {RunId}; isolating exact sessions",
                    run.RunId);
                await CloseExactAsync(affected).ConfigureAwait(false);
                return new(true, partyResult, locked.Target);
            }

            try
            {
                if (partyResult.Succeeded && partyResult.Notifications != PartyNotificationPlan.Empty)
                {
                    await notifications.DeliverAsync(
                        partyResult.Notifications, recipients, CancellationToken.None)
                        .ConfigureAwait(false);
                }

                if (departure is not null)
                {
                    await towerProjection.ProjectCommittedDepartureAsync(
                        departure, departing, departureResponse, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                else
                {
                    await towerProjection.ProjectCurrentHostAsync(
                        projectionRun, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception error)
            {
                logger.LogWarning(error,
                    "Party/tower post-commit projection failed for run {RunId}; isolating exact sessions",
                    run.RunId);
                await CloseExactAsync(affected).ConfigureAwait(false);
            }

            return new(true, partyResult, locked.Target);
        }

        return Rejected(null, PartyFailureReason.CallerNotCurrent);
    }

    private async ValueTask<PartyOperationResult> SendPartyCommandAsync(
        PartyChange change,
        SelectedSessionIdentity caller,
        SelectedSessionIdentity? target,
        CancellationToken token) => change switch
    {
        PartyChange.Leave => await commands.SendAsync(
            new LeavePartyCommand(caller.CharacterId), token).ConfigureAwait(false),
        PartyChange.Remove => await commands.SendAsync(
            new RemovePartyMemberCommand(caller.CharacterId, target!.CharacterId), token)
            .ConfigureAwait(false),
        PartyChange.Transfer => await commands.SendAsync(
            new TransferPartyLeadershipCommand(caller.CharacterId, target!.CharacterId), token)
            .ConfigureAwait(false),
        _ => throw new ArgumentOutOfRangeException(nameof(change)),
    };

    private SelectedSessionIdentity? LeaderToSynchronize(
        PartyChange change,
        SelectedSessionIdentity caller,
        CaptureState state)
    {
        var run = state.Runs[0];
        if (change == PartyChange.Transfer)
            return run.Members.Any(member => member.Session == state.Target) ? state.Target : null;
        if (change != PartyChange.Leave || state.Party.LeaderMember.CharacterId != caller.CharacterId)
            return null;

        var successor = state.Party.Members
            .Where(member => member.CharacterId != caller.CharacterId)
            .OrderBy(member => member.Slot)
            .FirstOrDefault();
        return successor is null
            ? null
            : run.Members.SingleOrDefault(member => member.Session.CharacterId == successor.CharacterId)?.Session;
    }

    private PartyFailureReason? Validate(
        PartyChange change,
        SelectedSessionIdentity caller,
        CaptureState state,
        bool allowVirtual)
    {
        if (state.Party.IsVirtual)
            return change == PartyChange.Leave && allowVirtual
                && state.Runs[0].Members.Any(member => member.Session == caller)
                    ? null
                    : PartyFailureReason.NotInParty;

        var callerMember = state.Party.Members.SingleOrDefault(member =>
            member.CharacterId == caller.CharacterId
            && member.Character.AccountId == caller.AccountId
            && member.WireUserId == caller.WireUserId);
        if (callerMember is null)
            return PartyFailureReason.NotInParty;
        if (change == PartyChange.Leave)
            return null;
        if (state.Party.LeaderMember.CharacterId != caller.CharacterId)
            return PartyFailureReason.NotPartyLeader;
        if (state.Target is null)
            return PartyFailureReason.TargetNotFound;
        if (change == PartyChange.Remove && state.Target == caller)
            return PartyFailureReason.TargetNotFound;
        return null;
    }

    private CaptureState Capture(SelectedSessionIdentity caller, byte? targetSlot)
    {
        var party = parties.FindParty(Character(caller));
        var targetMember = targetSlot is null || party.IsVirtual
            ? null
            : party.Members.SingleOrDefault(member => member.Slot == targetSlot.Value);
        var target = targetMember is not null
            && sessions.TryGetInWorld(targetMember.CharacterId, out var inWorld)
            ? inWorld
            : null;
        var candidates = new List<SelectedSessionIdentity> { caller };
        if (target is not null)
            candidates.Add(target);
        foreach (var member in party.Members)
        {
            if (sessions.TryGetInWorld(member.CharacterId, out var current)
                && current.AccountId == member.Character.AccountId
                && current.WireUserId == member.WireUserId)
            {
                candidates.Add(current);
            }
        }
        var runs = candidates.Distinct()
            .Select(towers.Find)
            .Where(run => run is not null)
            .Cast<BossTowerSnapshot>()
            .DistinctBy(run => run.RunId)
            .ToArray();
        return new(party, target, runs);
    }

    private bool ExactSessions(SelectedSessionIdentity caller, CaptureState state)
    {
        if (!sessions.IsInWorld(caller))
            return false;
        if (state.Target is not null
            && !sessions.IsInWorld(state.Target))
            return false;
        foreach (var member in state.Party.Members)
        {
            if (sessions.FindByCharacter(member.CharacterId) is not { } current
                || current.AccountId != member.Character.AccountId
                || current.WireUserId != member.WireUserId)
                return false;
        }
        return state.Runs[0].Members.All(member =>
            sessions.IsInWorld(member.Session)
            && towers.Find(member.Session) is { } current
            && current.RunId == state.Runs[0].RunId
            && current.Room.Version == state.Runs[0].Room.Version);
    }

    private static bool SameCapture(CaptureState expected, CaptureState current)
    {
        if (expected.Runs.Count != 1 || current.Runs.Count != 1
            || !SameParty(expected.Party, current.Party)
            || expected.Target != current.Target)
            return false;
        var left = expected.Runs[0];
        var right = current.Runs[0];
        return left.RunId == right.RunId
            && left.Room.Version == right.Room.Version
            && left.Room.Phase == right.Room.Phase
            && Equals(left.Completion, right.Completion)
            && left.Members.OrderBy(member => member.Slot)
                .Select(member => (member.Session, member.Slot, member.IsLeader, member.Dead))
                .SequenceEqual(right.Members.OrderBy(member => member.Slot)
                    .Select(member => (member.Session, member.Slot, member.IsLeader, member.Dead)));
    }

    private static bool SameParty(PartySnapshot expected, PartySnapshot current) =>
        expected.Key == current.Key
        && expected.Generation == current.Generation
        && expected.Revision == current.Revision
        && expected.LeaderWireUserId == current.LeaderWireUserId
        && expected.Members.OrderBy(member => member.Slot)
            .Select(member => (member.Character.AccountId, member.CharacterId,
                member.WireUserId, member.Slot))
            .SequenceEqual(current.Members.OrderBy(member => member.Slot)
                .Select(member => (member.Character.AccountId, member.CharacterId,
                    member.WireUserId, member.Slot)));

    private async ValueTask CloseExactAsync(IEnumerable<SelectedSessionIdentity> affected)
    {
        foreach (var session in affected.Distinct())
        {
            if (!sessions.IsInWorld(session))
                continue;
            try
            {
                await connections.RequestCloseAsync(
                    session.ConnectionId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                logger.LogWarning(error,
                    "Failed to isolate party/tower session {ConnectionId}", session.ConnectionId);
            }
        }
    }

    private static PartyCharacterSnapshot Character(SelectedSessionIdentity session) =>
        new(session.AccountId, session.CharacterId, session.WireUserId, null);

    private static BossTowerPartyAttempt NotHandled() =>
        new(false, PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent));

    private static BossTowerPartyAttempt Rejected(
        SelectedSessionIdentity? target, PartyFailureReason reason) =>
        new(true, PartyOperationResult.Reject(reason), target);

    private enum PartyChange
    {
        Leave,
        Remove,
        Transfer,
    }

    private sealed record CaptureState(
        PartySnapshot Party,
        SelectedSessionIdentity? Target,
        IReadOnlyList<BossTowerSnapshot> Runs);

    private sealed class LeaseSet : IAsyncDisposable
    {
        private IReadOnlyList<IAsyncDisposable>? held = new List<IAsyncDisposable>();
        public void Add(IAsyncDisposable lease) => ((List<IAsyncDisposable>)held!).Add(lease);

        public async ValueTask DisposeAsync()
        {
            var releasing = Interlocked.Exchange(ref held, null);
            if (releasing is null)
                return;
            for (var index = releasing.Count - 1; index >= 0; index--)
                await releasing[index].DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class OnceAsyncDisposable(IAsyncDisposable lease) : IAsyncDisposable
    {
        private IAsyncDisposable? held = lease;

        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref held, null)?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
