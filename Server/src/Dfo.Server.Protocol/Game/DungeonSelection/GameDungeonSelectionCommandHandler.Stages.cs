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

namespace Dfo.Server.Protocol.Game.DungeonSelection;

using Dfo.Server.Protocol.Game.Party;

internal sealed partial class GameDungeonSelectionCommandHandler
{
    internal async ValueTask ProjectBossTowerPartyDepartureAsync(CommandExecutionContext execution, BossTowerActionResult action,
        SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token)
    {
        if (!action.Changed || action.Run is null) return;
        var captured = action.Run;
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
        await BossTowerActionProjection.BossTowerCloseProjectionFailuresAsync(sessions, logger, closures, onlineUsers, failures, captured.RunId).ConfigureAwait(false);
    }

    internal async ValueTask ProjectIllusionTowerPartyDepartureAsync(CommandExecutionContext execution, TowerActionResult action,
        SelectedSessionIdentity? returning, IGameServerMessage? response, CancellationToken token)
    {
        if (!action.Changed || action.Run is null) return;
        var captured = action.Run;
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
            var entries = await IllusionTowerActionProjection.IllusionTowerCaptureReturnEntriesAsync(sessions, currentViews, currentState, onlineCharacters, outbox, [returning], response, failures, token, captured.DungeonId == 11000).ConfigureAwait(false);
            await ReturnTownIsolatedAsync(entries, failures, token).ConfigureAwait(false);
        }
        await IllusionTowerActionProjection.IllusionTowerCloseProjectionFailuresAsync(sessions, logger, closures, onlineUsers, failures, captured.RunId).ConfigureAwait(false);
    }

    private SelectedSessionRecipient CaptureParticipant(SelectedSessionIdentity session)
    {
        NetworkConnectionContext? connection = null;
        if (onlineUsers.TryGetByConnection(session.ConnectionId, out var user) == true && user is not null)
        {
            if (user.AccountId != session.AccountId || user.CurrentCharacterId != session.CharacterId)
                throw new InvalidOperationException("The tower participant connection no longer owns the captured character.");
            connection = user.Connection;
        }
        return new(session, connection);
    }

    private SelectedSessionRecipient[] ProtectedParticipants(IReadOnlyList<SelectedSessionIdentity> participants,
        SelectedSessionIdentity reporter) => participants.Select(session => session == reporter
            ? CaptureParticipant(session) : new SelectedSessionRecipient(session, null)).ToArray();

    internal ValueTask<BossTowerActionResult> LeaveBossPartyTowerAsync(CommandExecutionContext execution,
        BossTowerActionContext context, IReadOnlyList<SelectedSessionIdentity> participants, CancellationToken token) =>
        workUnits.WriteAsync(execution!, new("party-leave-boss-tower", ProtectedParticipants(participants, context.Session),
                Footprint: CommandFootprintMode.DeclaredNonRequest, FootprintOwners: participants),
            (unit, ct) => bossTowers.LeaveAsync(context, false, unit.Footprint, ct), token);

    internal ValueTask<TowerActionResult> LeaveIllusionPartyTowerAsync(CommandExecutionContext execution,
        TowerActionContext context, IReadOnlyList<SelectedSessionIdentity> owners, CancellationToken token) =>
        workUnits.WriteAsync(execution!, new("party-leave-illusion-tower", [], Footprint: CommandFootprintMode.DeclaredNonRequest, FootprintOwners: owners),
            (unit, ct) => illusionTowers.LeaveAsync(context, false, unit.Footprint, ct), token);

    internal ValueTask<BossTowerActionResult> StartBossPartyTowerAsync(CommandExecutionContext execution,
        BossTowerSnapshot run, SelectedSessionIdentity leader, CancellationToken token) =>
        workUnits.WriteAsync(execution!, new("party-start-boss-tower", ProtectedParticipants(run.Members.Select(member => member.Session).ToArray(), leader)),
            (_, ct) => bossTowers.StartAsync(BossTowerActionProjection.BossTowerContext(run, leader), ct), token);

    private ValueTask<int> TownEnterAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken token) =>
        TownArrivalProjection.EnterAsync(sessions, towns, housing, townAreas, currentViews, outbox,
            weaknessState, projectWeakness, projections,
            new PartyTownProjectionDelivery(parties, townAreas, sessions, projections, projectionState), entries, token);

    internal ValueTask<int> ReturnTownAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken token) => TownEnterAsync(entries, token);

    private async ValueTask ReturnTownIsolatedAsync(IReadOnlyList<TownAreaEntry> entries,
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
}
