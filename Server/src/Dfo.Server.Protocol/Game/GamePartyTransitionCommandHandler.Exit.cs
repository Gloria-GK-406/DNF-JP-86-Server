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
    private async ValueTask<IReadOnlyList<CharacterId>> EndOrdinaryRunAsync(
        DungeonRunId runId, CancellationToken token)
    {
        return await EndOrdinaryRunCoreAsync(runs, runId, token).ConfigureAwait(false);
    }

    /// <summary>The whole party leaves together: the run ends for every member at once.</summary>
    internal static async ValueTask<IReadOnlyList<CharacterId>> EndOrdinaryRunCoreAsync(
        IDungeonRunDomainService runs, DungeonRunId runId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return await runs.EndRunAsync(runId).ConfigureAwait(false);
    }

    private async ValueTask SettleAltarExitAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, DungeonRunId runId, bool onlyCaller, CancellationToken ct)
    {
        var run = runs.Find(session.CharacterId);
        if (run?.RunId != runId || !sessions.IsInWorld(session)
            || !onlyCaller && (run.Layout.Altar is null || !run.Participants.Any(p => p.CharacterId == session.CharacterId && p.IsLeader))) return;
        if (altars.Find(runId) is null) return;
        var cohort = AltarTransitionProjection.Participants(runs, sessions, registry, runId);
        if (!cohort.Contains(session)) return;
        var action = await workUnits.WriteAsync(execution, new("settle-altar-before-exit",
            ProtectedParticipants(cohort, session)),
            async (_, token) => new AltarExitResult(await altars.SettleBeforeExitAsync(runId, token,
                onlyCaller ? session.CharacterId : null).ConfigureAwait(false)), ct).ConfigureAwait(false);
        if (action.Action is not { } transition) return;
        var recipients = AltarTransitionProjection.Participants(runs, sessions, registry, transition.State.RunId);
        await AltarTransitionProjection.ProjectAsync(projections, transition, recipients, clock, wake, logger, ct).ConfigureAwait(false);
    }
    private sealed record AltarExitResult(AltarActionResult? Action);

    // EPLP tower channels: the caller has already dropped a stale request and located the requester in this tower.
    private async ValueTask SelectBossReturnAsync(CommandExecutionContext execution,
        DungeonPlace.InBossTower place, byte state, byte option, CancellationToken token)
    {
        var session = place.Session; var expected = place.Run;
        closeTargets.Capture(sessions, onlineUsers, expected.Members.Select(member => member.Session));
        if (!BossTowerActionProjection.BossTowerExact(sessions, bossTowers, session, expected)) return;
        if (state == 2)
        {
            await outbox.RegisterExplicitAsync(session.ConnectionId, new([new EplpCommandResponse(state, option)], []), token).ConfigureAwait(false);
            return;
        }
        if (state != 1 || option > 2) return;
        var action = await SelectBossResultAsync(execution, expected, session, option, token).ConfigureAwait(false);
        if (!action.Changed || action.Run is not { } run) return;
        var frozen = BossTowerActionProjection.BossTowerExactMembers(sessions, bossTowers, run);
        var failures = (await BossTowerActionProjection.BossTowerSendToExactAsync(sessions, bossTowers, outbox, run, [new EplpCommandResponse(state, option)], token).ConfigureAwait(false)).ToList();
        if (option == 2)
        {
            foreach (var member in frozen)
                _ = await LeaveBossPartyTowerAsync(execution, BossTowerActionProjection.BossTowerContext(run, member), BossTowerActionProjection.BossTowerMembers(run), CancellationToken.None).ConfigureAwait(false);
            var entries = await BossTowerActionProjection.BossTowerCaptureReturnEntriesAsync(sessions, currentViews, currentState, onlineCharacters, outbox, frozen, null, failures, CancellationToken.None, false).ConfigureAwait(false);
            await ReturnTownIsolatedAsync(entries, failures, CancellationToken.None).ConfigureAwait(false);
        }
        await BossTowerActionProjection.BossTowerCloseProjectionFailuresAsync(sessions, logger, closures, closeTargets, failures, expected.RunId).ConfigureAwait(false);
    }

    private async ValueTask SelectIllusionReturnAsync(CommandExecutionContext execution,
        DungeonPlace.InIllusionTower place, byte state, byte option, CancellationToken token)
    {
        var session = place.Session; var expected = place.Run;
        closeTargets.Capture(sessions, onlineUsers, expected.Members.Select(member => member.Session));
        if (state != EplpCommandHandler.LeaveState) return;
        if (!IllusionTowerActionProjection.IllusionTowerExact(sessions, illusionTowers, session, expected)) return;
        var action = await workUnits.WriteAsync(execution, new("select-illusion-tower-return", []),
            (_, ct) => illusionTowers.SelectReturnAsync(IllusionTowerActionProjection.IllusionTowerContext(expected, session), option, ct), token).ConfigureAwait(false);
        Exception? failure = null;
        if (action.Changed)
        {
            try { await outbox.RegisterExplicitAsync(session.ConnectionId, new([new EplpCommandResponse(state, option)], []), token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { failure = error; }
        }
        if (failure is not null) await IllusionTowerActionProjection.IllusionTowerCloseProjectionFailuresAsync(sessions, logger, closures, closeTargets, [(session, failure)], expected.RunId).ConfigureAwait(false);
    }
    private ValueTask<Dfo.Server.Abstractions.World.BossTower.BossTowerActionResult> SelectBossResultAsync(CommandExecutionContext execution,
        Dfo.Server.Abstractions.World.BossTower.BossTowerSnapshot expected, SelectedSessionIdentity session, byte option, CancellationToken token) =>
        workUnits.WriteAsync(execution, new("select-boss-tower-result", ProtectedParticipants(BossTowerActionProjection.BossTowerMembers(expected), session)),
            (_, ct) => bossTowers.SelectResultAsync(BossTowerActionProjection.BossTowerContext(expected, session), option, ct), token);

}
