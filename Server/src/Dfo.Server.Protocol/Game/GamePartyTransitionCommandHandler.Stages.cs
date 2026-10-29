using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Dfo.Server.Protocol.Game;

using Dfo.Server.Protocol.Game.Party;

internal sealed partial class GamePartyTransitionCommandHandler
{
    private readonly ExactSessionClosure closeTargets = new();

    internal async ValueTask ProjectBossTowerPartyDepartureAsync(CommandExecutionContext execution, BossTowerActionResult action,
        SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token)
    {
        if (!action.Changed || action.Run is null) return;
        var captured = action.Run;
        closeTargets.Capture(sessions, onlineUsers, captured.Members.Select(member => member.Session).Append(returning).OfType<SelectedSessionIdentity>());
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var participants = captured.Members.Select(member => member.Session)
            .Append(returning).OfType<SelectedSessionIdentity>().Distinct().ToArray();
        var exact = BossTowerActionProjection.BossTowerExactMembers(sessions, bossTowers, captured);
        var current = BossTowerActionProjection.BossTowerCurrentRun(sessions, bossTowers, captured);
        if (exact.Count > 0 && current?.Members.SingleOrDefault(member => member.IsLeader) is { } leader)
        {
            failures.AddRange(await BossTowerActionProjection.BossTowerSendToExactAsync(sessions, bossTowers, outbox, 
                current, [new UdpHostNotification(leader.Slot)], token).ConfigureAwait(false));
        }
        current = BossTowerActionProjection.BossTowerCurrentRun(sessions, bossTowers, captured);
        if (current is not null && BossTowerActionProjection.BossTowerExactMembers(sessions, bossTowers, current).Count == current.Members.Length
            && current.Room.Phase == TowerRoomPhase.Ready
            && current.Members.SingleOrDefault(member => member.IsLeader) is { } readyLeader)
        {
            var start = await StartBossPartyTowerAsync(execution, current, readyLeader.Session, token).ConfigureAwait(false);
            if (start.Run is { Room.Phase: TowerRoomPhase.Fighting } fighting)
                failures.AddRange(await BossTowerActionProjection.BossTowerSendToExactAsync(sessions, bossTowers, outbox, fighting, [new LoadingCompleteNotification()], token).ConfigureAwait(false));
        }
        if (exact.Count > 0 && action.SettlementCreated && captured.Completion is { Settled: true })
            failures.AddRange(await BossTowerActionProjection.BossTowerProjectActionAsync(sessions, characters, bossTowers, clock, outbox, action, null, token).ConfigureAwait(false));
        if (returning is not null
            && sessions.IsInWorld(returning)
            && !bossTowers.IsInTower(returning.CharacterId))
            {
            var entries = await BossTowerActionProjection.BossTowerCaptureReturnEntriesAsync(sessions, currentViews, currentState, onlineCharacters, outbox, [returning], response, failures, token, false).ConfigureAwait(false);
            await ReturnTownIsolatedAsync(entries, failures, token).ConfigureAwait(false);
        }
        await BossTowerActionProjection.BossTowerCloseProjectionFailuresAsync(sessions, logger, closures, closeTargets, failures, captured.RunId).ConfigureAwait(false);
    }

    internal async ValueTask ProjectBossTowerPartyHostAsync(
        BossTowerSnapshot captured, CancellationToken token)
    {
        if (captured.Members.IsEmpty)
            return;
        closeTargets.Capture(sessions, onlineUsers, captured.Members.Select(member => member.Session));
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var current = BossTowerActionProjection.BossTowerCurrentRun(sessions, bossTowers, captured);
        if (current?.Members.SingleOrDefault(member => member.IsLeader) is { } leader)
        {
            failures.AddRange(await BossTowerActionProjection.BossTowerSendToExactAsync(sessions, bossTowers, outbox, 
                current, [new UdpHostNotification(leader.Slot)], token).ConfigureAwait(false));
        }
        await BossTowerActionProjection.BossTowerCloseProjectionFailuresAsync(sessions, logger, closures, closeTargets, failures, captured.RunId).ConfigureAwait(false);
    }

    internal async ValueTask ProjectIllusionTowerPartyDepartureAsync(CommandExecutionContext execution, TowerActionResult action,
        SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token)
    {
        if (!action.Changed || action.Run is null) return;
        var captured = action.Run;
        closeTargets.Capture(sessions, onlineUsers, captured.Members.Select(member => member.Session).Append(returning).OfType<SelectedSessionIdentity>());
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var participants = captured.Members.Select(member => member.Session)
            .Append(returning).OfType<SelectedSessionIdentity>().Distinct().ToArray();
        var exact = IllusionTowerActionProjection.IllusionTowerExactMembers(sessions, illusionTowers, captured);
        var current = IllusionTowerActionProjection.IllusionTowerCurrentRun(sessions, illusionTowers, captured);
        if (exact.Count > 0 && current?.Members.SingleOrDefault(member => member.IsLeader) is { } leader)
        {
            failures.AddRange(await IllusionTowerActionProjection.IllusionTowerSendToExactAsync(sessions, illusionTowers, outbox, 
                current, [new UdpHostNotification(leader.Slot)], token).ConfigureAwait(false));
        }
        if (exact.Count > 0 && captured.Room.Phase == TowerRoomPhase.Ready)
            failures.AddRange(await IllusionTowerActionProjection.IllusionTowerSendToExactAsync(sessions, illusionTowers, outbox, 
                captured, [new LoadingCompleteNotification()], token).ConfigureAwait(false));
        if (exact.Count > 0 && action.SettlementCreated && captured.Completion is { Settled: true })
            failures.AddRange(await IllusionTowerActionProjection.IllusionTowerProjectActionAsync(sessions, characters, illusionTowers, clock, outbox, action, null, token).ConfigureAwait(false));
        if (returning is not null
            && sessions.IsInWorld(returning)
            && !illusionTowers.IsInTower(returning.CharacterId))
            {
            var entries = await IllusionTowerActionProjection.IllusionTowerCaptureReturnEntriesAsync(sessions, currentViews, currentState, onlineCharacters, outbox, [returning], response, failures, token, captured.UsesTowerInventory).ConfigureAwait(false);
            await ReturnTownIsolatedAsync(entries, failures, token).ConfigureAwait(false);
        }
        await IllusionTowerActionProjection.IllusionTowerCloseProjectionFailuresAsync(sessions, logger, closures, closeTargets, failures, captured.RunId).ConfigureAwait(false);
    }

    internal async ValueTask ProjectIllusionTowerPartyHostAsync(
        IllusionTowerSnapshot captured, CancellationToken token)
    {
        if (captured.Members.IsEmpty)
            return;
        closeTargets.Capture(sessions, onlineUsers, captured.Members.Select(member => member.Session));
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var current = IllusionTowerActionProjection.IllusionTowerCurrentRun(sessions, illusionTowers, captured);
        if (current?.Members.SingleOrDefault(member => member.IsLeader) is { } leader)
        {
            failures.AddRange(await IllusionTowerActionProjection.IllusionTowerSendToExactAsync(sessions, illusionTowers, outbox, 
                current, [new UdpHostNotification(leader.Slot)], token).ConfigureAwait(false));
        }
        await IllusionTowerActionProjection.IllusionTowerCloseProjectionFailuresAsync(sessions, logger, closures, closeTargets, failures, captured.RunId).ConfigureAwait(false);
    }

    internal ValueTask ProjectPartyAsync(PartyNotificationPlan plan, PartyNotificationRecipientPlan recipients,
        CancellationToken token) => PartyNotificationStages.DeliverPartyNotificationsAsync(projections, projectionState, parties, ordering, plan, recipients, token);

    internal ValueTask<PartyOperationResult> MutatePartyAsync(PartyChange change,
        SelectedSessionIdentity caller, SelectedSessionIdentity? target, CancellationToken token) =>
        MutatePartyStateAsync(parties!, runs, change, caller.CharacterId, target?.CharacterId, token);

    internal static async ValueTask<PartyOperationResult> MutatePartyStateAsync(Dfo.Server.Abstractions.Parties.IPartyService parties,
        IDungeonRunDomainService runs, PartyChange change, Dfo.Server.Data.Characters.CharacterId caller,
        Dfo.Server.Data.Characters.CharacterId? target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var departing = change == PartyChange.Leave ? caller : change == PartyChange.Remove ? target : null;
        var run = departing is null ? null : runs.Find(departing.Value);
        var result = change switch
        {
            PartyChange.Leave => parties.LeaveParty(caller),
            PartyChange.Remove => parties.RemoveMember(caller, target!.Value),
            PartyChange.Transfer => parties.TransferLeadership(caller, target!.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
        if (result.Succeeded && run is not null)
            _ = await runs.EndAsync(departing!.Value, run.RunId).ConfigureAwait(false);
        return result;
    }

    internal SelectedSessionRecipient CaptureParticipant(SelectedSessionIdentity session) =>
        GameParticipantCapture.Capture(onlineUsers, session);

    internal SelectedSessionRecipient[] ProtectedParticipants(IReadOnlyList<SelectedSessionIdentity> participants,
        SelectedSessionIdentity reporter) => participants.Select(session => session == reporter
            ? CaptureParticipant(session) : new SelectedSessionRecipient(session, null)).ToArray();

    internal ValueTask<BossTowerActionResult> LeaveBossPartyTowerAsync(CommandExecutionContext execution,
        TowerActionContext context, IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken token) =>
        workUnits.WriteAsync(execution!, new("party-leave-boss-tower", ProtectedParticipants(participants, context.Session),
                Footprint: CommandFootprintMode.DeclaredNonRequest, FootprintOwners: participants),
            (unit, ct) => bossTowers.LeaveAsync(context, false, unit.Footprint, ct), token);

    internal ValueTask<TowerActionResult> LeaveIllusionPartyTowerAsync(CommandExecutionContext execution,
        TowerActionContext context, IReadOnlyList<SelectedSessionIdentity> owners, CancellationToken token) =>
        workUnits.WriteAsync(execution!, new("party-leave-illusion-tower", [], Footprint: CommandFootprintMode.DeclaredNonRequest, FootprintOwners: owners),
            (unit, ct) => illusionTowers.LeaveAsync(context, false, unit.Footprint, ct), token);

    internal ValueTask<BossTowerActionResult> SynchronizeBossTowerPartyTowerAsync(CommandExecutionContext execution,
        TowerActionContext context, SelectedSessionIdentity leader, IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken token) =>
        workUnits.WriteAsync(execution!, new("party-synchronize-boss-leader", ProtectedParticipants(participants, context.Session)),
            (_, ct) => bossTowers.SynchronizeLeaderAsync(context, leader, ct), token);

    internal ValueTask<TowerActionResult> SynchronizeIllusionTowerPartyTowerAsync(CommandExecutionContext execution,
        TowerActionContext context, SelectedSessionIdentity leader, IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken token) =>
        workUnits.WriteAsync(execution!, new("party-synchronize-illusion-leader", []),
            (_, ct) => illusionTowers.SynchronizeLeaderAsync(context, leader, ct), token);

    internal ValueTask<BossTowerActionResult> StartBossPartyTowerAsync(CommandExecutionContext execution,
        BossTowerSnapshot run, SelectedSessionIdentity leader, CancellationToken token) =>
        workUnits.WriteAsync(execution!, new("party-start-boss-tower", ProtectedParticipants(run.Members.Select(member => member.Session).ToArray(), leader)),
            (_, ct) => bossTowers.StartAsync(BossTowerActionProjection.BossTowerContext(run, leader), ct), token);

    /// <summary>The Illusion tower channel of a party command: the whole party has been located in this tower.</summary>
    internal async ValueTask<IllusionTowerPartyAttempt> CoordinateIllusionTowerPartyAsync(
        CommandExecutionContext execution, PartyChange change,
        SelectedSessionIdentity caller,
        PartyCandidates candidates,
        DungeonPartyPlace.PartyInIllusionTower place,
        bool allowVirtual,
        IGameServerMessage? departureResponse,
        CancellationToken token)
    {
        var observed = new IllusionTowerCaptureState(candidates.Party, candidates.Target, place.Runs);
        if (observed.Runs.Count != 1)
            return IllusionTowerRejected(observed.Target, PartyFailureReason.PartyGenerationChanged);

        if (!IllusionTowerExactSessions(caller, observed))
            return IllusionTowerRejected(null, PartyFailureReason.CallerNotCurrent);

        closeTargets.Capture(sessions, onlineUsers, observed.Party.Members
            .Select(member => sessions.FindByCharacter(member.CharacterId))
            .Concat(observed.Runs[0].Members.Select(member => member.Session))
            .Append(caller).Append(observed.Target).OfType<SelectedSessionIdentity>());
        var validation = IllusionTowerValidate(change, caller, observed, allowVirtual);
        if (validation is not null)
            return IllusionTowerRejected(observed.Target, validation.Value);

        if (change == PartyChange.Transfer && observed.Target == caller)
            return new(PartyOperationResult.Success(), caller);

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

        // Durable tower work is deliberately completed before the in-memory parties write.
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
            return new(partyResult, observed.Target);
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

        return new(partyResult, observed.Target);
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

    internal async ValueTask IllusionTowerCloseExactAsync(IEnumerable<SelectedSessionIdentity> affected)
    {
        foreach (var session in affected.Distinct())
        {
            await closeTargets.RequestAsync(sessions, closures, session).ConfigureAwait(false);
        }
    }

    private static IllusionTowerPartyAttempt IllusionTowerRejected(
        SelectedSessionIdentity? target, PartyFailureReason reason) =>
        new(PartyOperationResult.Reject(reason), target);

    internal ValueTask<int> TownEnterAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken token) =>
        TownArrivalProjection.EnterAsync(sessions, towns, housing, townAreas, onlineCharacters, currentViews, outbox,
            weaknessState, projectWeakness, projections,
            new PartyTownProjectionDelivery(parties, townAreas, sessions, projections, projectionState), entries, token);

    internal ValueTask<int> ReturnTownAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken token) => TownEnterAsync(entries, token);

    internal async ValueTask ReturnTownIsolatedAsync(IReadOnlyList<TownAreaEntry> entries,
        List<(SelectedSessionIdentity, Exception)> failures, CancellationToken token)
    {
        if (entries.Count == 0) return;
        try { _ = await ReturnTownAsync(entries, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            foreach (var entry in entries) failures.Add((entry.Session, error));
        }
    }

    /// <summary>The Boss tower channel of a party command: the whole party has been located in this tower.</summary>
    internal async ValueTask<BossTowerPartyAttempt> CoordinateBossTowerPartyAsync(
        CommandExecutionContext execution, PartyChange change,
        SelectedSessionIdentity caller,
        PartyCandidates candidates,
        DungeonPartyPlace.PartyInBossTower place,
        bool allowVirtual,
        IGameServerMessage? departureResponse,
        CancellationToken token)
    {
        var observed = new BossTowerCaptureState(candidates.Party, candidates.Target, place.Runs);
        if (observed.Runs.Count != 1)
            return BossTowerRejected(observed.Target, PartyFailureReason.PartyGenerationChanged);

        if (!BossTowerExactSessions(caller, observed))
            return BossTowerRejected(null, PartyFailureReason.CallerNotCurrent);

        closeTargets.Capture(sessions, onlineUsers, observed.Party.Members
            .Select(member => sessions.FindByCharacter(member.CharacterId))
            .Concat(observed.Runs[0].Members.Select(member => member.Session))
            .Append(caller).Append(observed.Target).OfType<SelectedSessionIdentity>());
        var validation = BossTowerValidate(change, caller, observed, allowVirtual);
        if (validation is not null)
            return BossTowerRejected(observed.Target, validation.Value);

        if (change == PartyChange.Transfer && observed.Target == caller)
            return new(PartyOperationResult.Success(), caller);

        BossTowerActionResult? departure = null;
        BossTowerSnapshot projectionRun = observed.Runs[0];
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

        // Durable tower work is deliberately completed before the in-memory parties write.
        // Any predictable party refusal has already been resolved above.
        if (departingMember is not null)
        {
            departure = await LeaveBossPartyTowerAsync(execution, new TowerActionContext(departingMember.Session, run.RunId, run.Room.Version), run.Members.Select(value => value.Session).ToArray(), token).ConfigureAwait(false);
            if (!departure.Changed || departure.Run is null)
                return BossTowerRejected(observed.Target, PartyFailureReason.CallerNotCurrent);
            projectionRun = departure.Run;
        }
        else if (BossTowerLeaderToSynchronize(change, caller, observed) is { } nextLeader)
        {
            var synchronized = await SynchronizeBossTowerPartyTowerAsync(execution, new TowerActionContext(nextLeader, run.RunId, run.Room.Version), nextLeader, run.Members.Select(value => value.Session).ToArray(), token).ConfigureAwait(false);
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
            await BossTowerCloseExactAsync(affected).ConfigureAwait(false);
            return new(partyResult, observed.Target);
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
                await ProjectBossTowerPartyDepartureAsync(execution,
                    departure, departing, departureResponse, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            else
            {
                await ProjectBossTowerPartyHostAsync(
                    projectionRun, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception error)
        {
            logger.LogWarning(error,
                "Party/tower post-commit projection failed for run {RunId}; isolating exact sessions",
                run.RunId);
            await BossTowerCloseExactAsync(affected).ConfigureAwait(false);
        }

        return new(partyResult, observed.Target);
    }

    private SelectedSessionIdentity? BossTowerLeaderToSynchronize(
        PartyChange change,
        SelectedSessionIdentity caller,
        BossTowerCaptureState state)
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

    private PartyFailureReason? BossTowerValidate(
        PartyChange change,
        SelectedSessionIdentity caller,
        BossTowerCaptureState state,
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

    private bool BossTowerExactSessions(SelectedSessionIdentity caller, BossTowerCaptureState state)
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
            && bossTowers.Find(member.Session) is { } current
            && current.RunId == state.Runs[0].RunId
            && current.Room.Version == state.Runs[0].Room.Version);
    }

    internal async ValueTask BossTowerCloseExactAsync(IEnumerable<SelectedSessionIdentity> affected)
    {
        foreach (var session in affected.Distinct())
        {
            await closeTargets.RequestAsync(sessions, closures, session).ConfigureAwait(false);
        }
    }

    private static BossTowerPartyAttempt BossTowerRejected(
        SelectedSessionIdentity? target, PartyFailureReason reason) =>
        new(PartyOperationResult.Reject(reason), target);

    /// <summary>The party a party command speaks for, and the sessions its place is judged over.</summary>
    /// <param name="Candidates">The caller, the target if any, then every online member whose session matches its roster identity, in that order.</param>
    internal sealed record PartyCandidates(PartySnapshot Party, SelectedSessionIdentity? Target, IReadOnlyList<SelectedSessionIdentity> Candidates);

    /// <summary>Collects the party's candidates once and locates the whole party over them.</summary>
    internal (PartyCandidates Candidates, DungeonPartyPlace Place) LocatePartyOf(SelectedSessionIdentity caller, byte? targetSlot)
    {
        var party = parties.FindParty(new(caller.AccountId, caller.CharacterId, caller.WireUserId, null));
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
        var observed = new PartyCandidates(party, target, candidates);
        return (observed, DungeonLocator.LocateParty(candidates, participation, bossTowers, illusionTowers));
    }

    private async ValueTask<bool> HandleMutationAsync(CommandExecutionContext execution, NetworkDispatchContext dispatch,
        GameMessageMetadata metadata, PartyChange change, byte? targetSlot, CancellationToken token)
    {
        var request = execution.Request ?? throw new InvalidOperationException("A party protocol request is required.");
        request.DemandCurrent();
        if (GameRequestFreshness.IsStale(metadata)) return false;
        var caller = sessions.FindByConnection(dispatch.Connection.ConnectionId);
        if (caller is null) return false;

        var (candidates, place) = LocatePartyOf(caller, targetSlot);
        switch (place)
        {
            case DungeonPartyPlace.PartyInBossTower boss:
                return (await CoordinateBossTowerPartyAsync(execution, change, caller, candidates, boss, false, null, token).ConfigureAwait(false)).Result.Succeeded;
            case DungeonPartyPlace.PartyInIllusionTower illusion:
                var attempt = await CoordinateIllusionTowerPartyAsync(execution, change, caller, candidates, illusion, false, null, token).ConfigureAwait(false);
                if (change == PartyChange.Remove && !attempt.Result.Succeeded)
                    await outbox.RegisterExplicitAsync(dispatch.Connection.ConnectionId, new GameExplicitMessageDelivery([new RemovePartyMemberRefusal()], []), token).ConfigureAwait(false);
                LogPartyMutation(change, caller, attempt.Target, targetSlot, attempt.Result);
                return attempt.Result.Succeeded;
            default:
                return await MutatePartyOrdinaryAsync(dispatch, change, caller, targetSlot, token).ConfigureAwait(false);
        }
    }

    private async ValueTask<bool> MutatePartyOrdinaryAsync(NetworkDispatchContext dispatch,
        PartyChange change, SelectedSessionIdentity caller, byte? targetSlot, CancellationToken token)
    {
        SelectedSessionIdentity? target = null;
        if (change != PartyChange.Leave)
        {
            var roster = parties.FindParty(new(caller.AccountId, caller.CharacterId, caller.WireUserId, null));
            var member = roster.IsVirtual ? null : roster.Members.SingleOrDefault(member => member.Slot == targetSlot);
            target = member is not null && sessions.TryGetInWorld(member.CharacterId, out var inWorld) ? inWorld : null;
            if (target is null)
            {
                if (change == PartyChange.Remove)
                    await outbox.RegisterExplicitAsync(dispatch.Connection.ConnectionId, new GameExplicitMessageDelivery([new RemovePartyMemberRefusal()], []), token).ConfigureAwait(false);
                return false;
            }
        }
        var departing = change == PartyChange.Leave ? caller : change == PartyChange.Remove ? target : null;
        var departingRun = departing is null ? null : runs.Find(departing.CharacterId);
        var runtime = change == PartyChange.Remove && departingRun is not null
            ? await currentViews.TryLoadAsync(target!, token).ConfigureAwait(false) : null;
        var position = change == PartyChange.Remove && departingRun is not null ? onlineCharacters.PositionOf(target!) : null;
        var result = !sessions.IsInWorld(caller) || target is not null && !sessions.IsInWorld(target)
            ? PartyOperationResult.Reject(PartyFailureReason.CallerNotCurrent)
            : await MutatePartyAsync(change, caller, target, token).ConfigureAwait(false);
        var recipients = PartyNotificationRecipientCapture.Capture(result.Notifications, sessions, townAreas);
        if (!result.Succeeded)
        {
            if (change == PartyChange.Remove)
                await outbox.RegisterExplicitAsync(dispatch.Connection.ConnectionId, new GameExplicitMessageDelivery([new RemovePartyMemberRefusal()], []), token).ConfigureAwait(false);
        }
        else
        {
            if (departingRun is not null && runs.Find(departing!.CharacterId) is null)
                registry.ParticipantDeparted(departingRun.RunId, departing!.CharacterId);
            await ProjectPartyAsync(result.Notifications, recipients, token).ConfigureAwait(false);
            if (change == PartyChange.Remove && departingRun is not null && runtime is not null && position is not null && sessions.IsInWorld(target!))
                await ReturnTownAsync([new TownAreaEntry(target!, runtime, position)], token).ConfigureAwait(false);
        }
        LogPartyMutation(change, caller, target, targetSlot, result);
        return result.Succeeded;
    }

    private void LogPartyMutation(PartyChange change, SelectedSessionIdentity caller, SelectedSessionIdentity? target,
        byte? slot, PartyOperationResult result) => logger.LogInformation(
            "Party operation={Operation} caller={Caller} target={Target} slot={Slot} accepted={Accepted} failure={Failure}",
            change, caller.CharacterId, target?.CharacterId, slot, result.Succeeded, result.Failure);
}
