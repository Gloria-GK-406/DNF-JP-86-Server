using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.TownProjection;

namespace Dfo.Server.Protocol.Game;

internal sealed partial class GamePartyTransitionCommandHandler
{
    private async ValueTask SettleAltarExitAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, DungeonRunId runId, bool onlyCaller, CancellationToken ct)
    {
        var run = runs.Find(session.CharacterId);
        if (run?.RunId != runId || !sessions.IsInWorld(session)
            || !onlyCaller && (run.Layout.Mode is not DungeonRunMode.Altar || !run.Participants.Any(p => p.CharacterId == session.CharacterId && p.IsLeader))) return;
        if (altars.Find(runId) is null) return;
        var cohort = AltarTransitionProjection.Participants(runs, sessions, registry, runId);
        if (!cohort.Contains(session)) return;
        var action = await workUnits.WriteAsync(execution, new("settle-altar-before-exit",
            ProtectedParticipants(cohort, session)),
            async (_, token) => new AltarExitResult(await altars.SettleBeforeExitAsync(runId, token,
                onlyCaller ? session.CharacterId : null).ConfigureAwait(false)), ct).ConfigureAwait(false);
        if (action.Action is not { } transition) return;
        var recipients = AltarTransitionProjection.Participants(runs, sessions, registry, transition.State.RunId);
        await AltarTransitionProjection.ProjectAsync(projections, transition, recipients, clock, logger, ct).ConfigureAwait(false);
    }
    private sealed record AltarExitResult(AltarActionResult? Action);

    // EPLP tower channels: the caller has already dropped a stale request and located the requester in this tower.
    private async ValueTask SelectBossReturnAsync(CommandExecutionContext execution,
        DungeonPlace.InBossTower place, byte state, byte option, CancellationToken token)
    {
        var session = place.Session; var expected = place.Run;
        closeTargets.Capture(sessions, onlineUsers, expected.Members.Select(member => member.Session));
        if (!TowerActionProjection.Exact(sessions, bossKind, session, expected)) return;
        if (state == 2)
        {
            await outbox.RegisterExplicitAsync(session.ConnectionId, new([new EplpCommandResponse(state, option)], []), token).ConfigureAwait(false);
            return;
        }
        if (state != 1 || option > 2) return;
        var action = await SelectBossResultAsync(execution, expected, session, option, token).ConfigureAwait(false);
        if (!action.Changed || action.Run is not { } run) return;
        var frozen = TowerActionProjection.ExactMembers(sessions, bossKind, run);
        var failures = (await TowerActionProjection.SendToExactAsync(sessions, bossKind, outbox, run, [new EplpCommandResponse(state, option)], token).ConfigureAwait(false)).ToList();
        if (option == 2)
        {
            foreach (var member in frozen)
                _ = await Departures.LeaveAsync(execution, bossKind, TowerActionProjection.Context(run, member), TowerActionProjection.Members(run), CancellationToken.None).ConfigureAwait(false);
            var entries = await TowerActionProjection.CaptureReturnEntriesAsync(sessions, currentViews, currentState, onlineCharacters, outbox, frozen, null, failures, CancellationToken.None, false).ConfigureAwait(false);
            await ReturnTownIsolatedAsync(entries, failures, CancellationToken.None).ConfigureAwait(false);
        }
        await TowerActionProjection.CloseProjectionFailuresAsync(sessions, bossKind, logger, closures, closeTargets, failures, expected.RunId).ConfigureAwait(false);
    }

    private async ValueTask SelectIllusionReturnAsync(CommandExecutionContext execution,
        DungeonPlace.InIllusionTower place, byte state, byte option, CancellationToken token)
    {
        var session = place.Session; var expected = place.Run;
        closeTargets.Capture(sessions, onlineUsers, expected.Members.Select(member => member.Session));
        if (state != EplpCommandHandler.LeaveState) return;
        if (!TowerActionProjection.Exact(sessions, illusionKind, session, expected)) return;
        var action = await workUnits.WriteAsync(execution, new("select-illusion-tower-return", []),
            (_, ct) => illusionTowers.SelectReturnAsync(TowerActionProjection.Context(expected, session), option, ct), token).ConfigureAwait(false);
        Exception? failure = null;
        if (action.Changed)
        {
            try { await outbox.RegisterExplicitAsync(session.ConnectionId, new([new EplpCommandResponse(state, option)], []), token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { failure = error; }
        }
        if (failure is not null) await TowerActionProjection.CloseProjectionFailuresAsync(sessions, illusionKind, logger, closures, closeTargets, [(session, failure)], expected.RunId).ConfigureAwait(false);
    }
    private ValueTask<Dfo.Server.Abstractions.World.BossTower.BossTowerActionResult> SelectBossResultAsync(CommandExecutionContext execution,
        Dfo.Server.Abstractions.World.BossTower.BossTowerSnapshot expected, SelectedSessionIdentity session, byte option, CancellationToken token) =>
        workUnits.WriteAsync(execution, new("select-boss-tower-result", ProtectedParticipants(TowerActionProjection.Members(expected), session)),
            (_, ct) => bossTowers.SelectResultAsync(TowerActionProjection.Context(expected, session), option, ct), token);

}
