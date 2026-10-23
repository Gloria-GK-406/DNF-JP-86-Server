using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game;

public sealed record IllusionTowerPartyAttempt(
    bool Handled,
    PartyOperationResult Result,
    SelectedSessionIdentity? Target = null);

/// <summary>
/// Coordinates the one non-atomic boundary between durable tower work and process-local party work.
/// </summary>
public sealed class IllusionTowerPartyCoordinator(
    IIllusionTowerDomainService towers,
    IPartyService parties,
    ISelectedSessionDomainService sessions,
    ICommandBus commands,
    PartyNotificationDelivery notifications,
    IllusionTowerProjectionDelivery towerProjection,
    INetworkConnectionCloser connections,
    ITownAreaDomainService townAreas,
    ILogger<IllusionTowerPartyCoordinator> logger)
{
    public ValueTask<IllusionTowerPartyAttempt> TryLeaveAsync(
        SelectedSessionIdentity caller,
        bool allowVirtual,
        IGameServerMessage? departureResponse,
        CancellationToken token) =>
        TryCoordinateAsync(PartyChange.Leave, caller, null, allowVirtual,
            departureResponse, token);

    public ValueTask<IllusionTowerPartyAttempt> TryRemoveAsync(
        SelectedSessionIdentity caller,
        byte targetSlot,
        CancellationToken token) =>
        TryCoordinateAsync(PartyChange.Remove, caller, targetSlot, false, null, token);

    public ValueTask<IllusionTowerPartyAttempt> TryTransferAsync(
        SelectedSessionIdentity caller,
        byte targetSlot,
        CancellationToken token) =>
        TryCoordinateAsync(PartyChange.Transfer, caller, targetSlot, false, null, token);

    private async ValueTask<IllusionTowerPartyAttempt> TryCoordinateAsync(
        PartyChange change,
        SelectedSessionIdentity caller,
        byte? targetSlot,
        bool allowVirtual,
        IGameServerMessage? departureResponse,
        CancellationToken token)
    {
        var observed = Capture(caller, targetSlot);
        if (observed.Runs.Count == 0)
            return NotHandled();
        if (observed.Runs.Count != 1)
            return Rejected(observed.Target, PartyFailureReason.PartyGenerationChanged);

        if (!ExactSessions(caller, observed))
            return Rejected(null, PartyFailureReason.CallerNotCurrent);

        var validation = Validate(change, caller, observed, allowVirtual);
        if (validation is not null)
            return Rejected(observed.Target, validation.Value);

        if (change == PartyChange.Transfer && observed.Target == caller)
            return new(true, PartyOperationResult.Success(), caller);

        TowerActionResult? departure = null;
        IllusionTowerSnapshot projectionRun = observed.Runs[0];
        var run = observed.Runs[0];
        var departing = change switch
        {
            PartyChange.Leave => caller,
            PartyChange.Remove => observed.Target,
            _ => null,
        };
        var departingMember = departing is null
            ? null
            : run.Members.SingleOrDefault(member => member.Session == departing);

        // Durable tower work is deliberately completed before the in-memory PartyService write.
        // Any predictable party refusal has already been resolved above.
        if (departingMember is not null)
        {
            departure = await commands.SendAsync(
                new LeaveIllusionTowerCommand(
                    new TowerActionContext(departingMember.Session, run.RunId, run.Room.Version),
                    Disconnected: false),
                token).ConfigureAwait(false);
            if (!departure.Changed || departure.Run is null)
                return Rejected(observed.Target, PartyFailureReason.CallerNotCurrent);
            projectionRun = departure.Run;
        }
        else if (LeaderToSynchronize(change, caller, observed) is { } nextLeader)
        {
            var synchronized = await commands.SendAsync(
                new SynchronizeIllusionTowerLeaderCommand(
                    new TowerActionContext(nextLeader, run.RunId, run.Room.Version), nextLeader),
                token).ConfigureAwait(false);
            if (synchronized.Run is not null)
                projectionRun = synchronized.Run;
        }

        PartyOperationResult partyResult;
        PartyNotificationRecipientPlan recipients;
        Exception? secondPhaseFailure = null;
        if (observed.Party.IsVirtual)
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
                partyResult = await SendPartyCommandAsync(change, caller, observed.Target,
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

        var affected = observed.Party.Members
            .Select(member => sessions.FindByCharacter(member.CharacterId))
            .Concat(run.Members.Select(member => member.Session))
            .Append(caller)
            .Append(observed.Target)
            .OfType<SelectedSessionIdentity>()
            .Distinct()
            .ToArray();

        if (secondPhaseFailure is not null)
        {
            logger.LogError(secondPhaseFailure,
                "Party mutation failed after tower work for run {RunId}; isolating exact sessions",
                run.RunId);
            await CloseExactAsync(affected).ConfigureAwait(false);
            return new(true, partyResult, observed.Target);
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

        return new(true, partyResult, observed.Target);
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
            .Cast<IllusionTowerSnapshot>()
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

    private async ValueTask CloseExactAsync(IEnumerable<SelectedSessionIdentity> affected)
    {
        foreach (var session in affected.Distinct())
        {
            if (!sessions.IsInWorld(session))
                continue;
            await connections.RequestCloseAsync(
                session.ConnectionId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static PartyCharacterSnapshot Character(SelectedSessionIdentity session) =>
        new(session.AccountId, session.CharacterId, session.WireUserId, null);

    private static IllusionTowerPartyAttempt NotHandled() =>
        new(false, PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent));

    private static IllusionTowerPartyAttempt Rejected(
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
        IReadOnlyList<IllusionTowerSnapshot> Runs);

}
