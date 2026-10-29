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

namespace Dfo.Server.Protocol.Game;

using Dfo.Server.Protocol.Game.Party;

internal partial class TowerReturnCommandHandler
{
    private readonly ExactSessionClosure closeTargets = new();

    internal async ValueTask<bool> ReturnBossTowerRunAsync(CommandExecutionContext execution, ReturnBossTowerRunCommand command, CancellationToken token)
    {
        var expected = bossTowers.Find(command.Context.Session);
        if (expected is null || expected.RunId != command.Context.RunId || expected.Room.Version != command.Context.RoomVersion
            || !(expected.Completion is { Settled: true } && expected.ReturnDueAt is { } due && due <= command.CapturedAt
                || expected.Completion is null && expected.Room.Phase == TowerRoomPhase.Loading && expected.LoadDueAt <= command.CapturedAt)) return false;
        closeTargets.Capture(sessions, onlineUsers, expected.Members.Select(member => member.Session));
        var frozen = TowerActionProjection.ExactMembers(sessions, bossKind, expected);
        if (frozen.Count == 0) return false;
        var owners = TowerActionProjection.Members(expected);
        foreach (var member in frozen.OrderBy(member => expected.Members.Single(value => value.Session == member).Dead ? 0 : 1))
            await Departures.LeaveAsync(execution, bossKind, TowerActionProjection.Context(expected, member), owners, token).ConfigureAwait(false);
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var entries = await TowerActionProjection.CaptureReturnEntriesAsync(sessions, currentViews, currentState, onlineCharacters, outbox, frozen, null, failures, CancellationToken.None, false).ConfigureAwait(false);
        await ReturnTownIsolatedAsync(entries, failures, CancellationToken.None).ConfigureAwait(false);
        await TowerActionProjection.CloseProjectionFailuresAsync(sessions, bossKind, logger, closures, closeTargets, failures, expected.RunId).ConfigureAwait(false);
        return true;
    }

    internal async ValueTask<bool> ReturnIllusionTowerRunAsync(CommandExecutionContext execution, ReturnIllusionTowerRunCommand command, CancellationToken token)
    {
        var expected = illusionTowers.Find(command.Context.Session);
        if (expected is null || expected.RunId != command.Context.RunId || expected.Room.Version != command.Context.RoomVersion
            || expected.ReturnDueAt is not { } due || due > command.CapturedAt) return false;
        closeTargets.Capture(sessions, onlineUsers, expected.Members.Select(member => member.Session));
        var frozen = TowerActionProjection.ExactMembers(sessions, illusionKind, expected);
        if (frozen.Count == 0) return false;
        var owners = expected.Members.Select(member => member.Session).ToArray();
        foreach (var member in frozen)
            await Departures.LeaveAsync(execution, illusionKind, TowerActionProjection.Context(expected, member), owners, token).ConfigureAwait(false);
        IGameServerMessage? response = expected.ReturnOption is { } option
            ? new EplpCommandResponse(EplpCommandHandler.LeaveState, option) : null;
        var failures = new List<(SelectedSessionIdentity, Exception)>();
        var entries = await TowerActionProjection.CaptureReturnEntriesAsync(sessions, currentViews, currentState, onlineCharacters, outbox, frozen, response, failures, token, expected.UsesTowerInventory).ConfigureAwait(false);
        await ReturnTownIsolatedAsync(entries, failures, token).ConfigureAwait(false);
        await TowerActionProjection.CloseProjectionFailuresAsync(sessions, illusionKind, logger, closures, closeTargets, failures, expected.RunId).ConfigureAwait(false);
        return true;
    }

    private SelectedSessionRecipient CaptureParticipant(SelectedSessionIdentity session) =>
        GameParticipantCapture.Capture(onlineUsers, session);

    private SelectedSessionRecipient[] ProtectedParticipants(IReadOnlyList<SelectedSessionIdentity> participants,
        SelectedSessionIdentity reporter) => participants.Select(session => session == reporter
            ? CaptureParticipant(session) : new SelectedSessionRecipient(session, null)).ToArray();

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
}
