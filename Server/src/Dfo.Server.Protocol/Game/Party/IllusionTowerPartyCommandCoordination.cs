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

namespace Dfo.Server.Protocol.Game.Party;

using Dfo.Server.Protocol.Game;
internal abstract partial class PartyMutationProtocolCommandHandler
{
    protected async ValueTask<IllusionTowerPartyAttempt> CoordinateIllusionTowerPartyAsync(
        CommandExecutionContext? execution, PartyChange change,
        SelectedSessionIdentity caller,
        byte? targetSlot,
        bool allowVirtual,
        IGameServerMessage? departureResponse,
        CancellationToken token)
    {
        var observed = IllusionTowerCapture(caller, targetSlot);
        if (observed.Runs.Count == 0)
            return IllusionTowerNotHandled();
        if (observed.Runs.Count != 1)
            return IllusionTowerRejected(observed.Target, PartyFailureReason.PartyGenerationChanged);

        if (!IllusionTowerExactSessions(caller, observed))
            return IllusionTowerRejected(null, PartyFailureReason.CallerNotCurrent);

        var validation = IllusionTowerValidate(change, caller, observed, allowVirtual);
        if (validation is not null)
            return IllusionTowerRejected(observed.Target, validation.Value);

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
            departure = await LeaveIllusionPartyTowerAsync(execution, new TowerActionContext(departingMember.Session, run.RunId, run.Room.Version), run.Members.Select(value => value.Session).ToArray(), token).ConfigureAwait(false);
            if (!departure.Changed || departure.Run is null)
                return IllusionTowerRejected(observed.Target, PartyFailureReason.CallerNotCurrent);
            projectionRun = departure.Run;
        }
        else if (IllusionTowerLeaderToSynchronize(change, caller, observed) is { } nextLeader)
        {
            var synchronized = await SynchronizeIllusionTowerPartyTowerAsync(execution, new TowerActionContext(nextLeader, run.RunId, run.Room.Version), nextLeader, run.Members.Select(value => value.Session).ToArray(), token).ConfigureAwait(false);
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
                partyResult = await MutatePartyAsync(change, caller, observed.Target,
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
            await IllusionTowerCloseExactAsync(affected).ConfigureAwait(false);
            return new(true, partyResult, observed.Target);
        }

        try
        {
            if (partyResult.Succeeded && partyResult.Notifications != PartyNotificationPlan.Empty)
            {
                await ProjectPartyAsync(
                    partyResult.Notifications, recipients, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (departure is not null)
            {
                await ProjectIllusionTowerPartyDepartureAsync(execution,
                    departure, departing, departureResponse, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else
            {
                await ProjectIllusionTowerPartyHostAsync(
                    projectionRun, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            logger.LogWarning(error,
                "Party/tower post-commit projection failed for run {RunId}; isolating exact sessions",
                run.RunId);
            await IllusionTowerCloseExactAsync(affected).ConfigureAwait(false);
        }

        return new(true, partyResult, observed.Target);
    }

    private SelectedSessionIdentity? IllusionTowerLeaderToSynchronize(
        PartyChange change,
        SelectedSessionIdentity caller,
        IllusionTowerCaptureState state)
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

    private PartyFailureReason? IllusionTowerValidate(
        PartyChange change,
        SelectedSessionIdentity caller,
        IllusionTowerCaptureState state,
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

    private IllusionTowerCaptureState IllusionTowerCapture(SelectedSessionIdentity caller, byte? targetSlot)
    {
        var party = PartyService!.FindParty(IllusionTowerCharacter(caller));
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
            .Select(illusionTowers.Find)
            .Where(run => run is not null)
            .Cast<IllusionTowerSnapshot>()
            .DistinctBy(run => run.RunId)
            .ToArray();
        return new(party, target, runs);
    }

    private bool IllusionTowerExactSessions(SelectedSessionIdentity caller, IllusionTowerCaptureState state)
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
            && illusionTowers.Find(member.Session) is { } current
            && current.RunId == state.Runs[0].RunId
            && current.Room.Version == state.Runs[0].Room.Version);
    }

    private async ValueTask IllusionTowerCloseExactAsync(IEnumerable<SelectedSessionIdentity> affected)
    {
        foreach (var session in affected.Distinct())
        {
            if (!sessions.IsInWorld(session))
                continue;
            await close(session).ConfigureAwait(false);
        }
    }

    private static PartyCharacterSnapshot IllusionTowerCharacter(SelectedSessionIdentity session) =>
        new(session.AccountId, session.CharacterId, session.WireUserId, null);

    private static IllusionTowerPartyAttempt IllusionTowerNotHandled() =>
        new(false, PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent));

    private static IllusionTowerPartyAttempt IllusionTowerRejected(
        SelectedSessionIdentity? target, PartyFailureReason reason) =>
        new(true, PartyOperationResult.Reject(reason), target);


    private sealed record IllusionTowerCaptureState(
        PartySnapshot Party,
        SelectedSessionIdentity? Target,
        IReadOnlyList<IllusionTowerSnapshot> Runs);

}
