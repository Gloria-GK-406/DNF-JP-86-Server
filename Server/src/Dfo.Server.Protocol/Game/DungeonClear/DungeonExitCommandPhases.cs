using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.TownProjection;

namespace Dfo.Server.Protocol.Game.DungeonClear;

internal partial class GameDungeonExitCommandHandler
{
    protected virtual async ValueTask<IReadOnlyList<CharacterId>> EndOrdinaryRunAsync(
        IReadOnlyList<SelectedSessionIdentity> members, DungeonRunId runId, CancellationToken token)
    {
        return await EndOrdinaryRunCoreAsync(runs, members, runId, token).ConfigureAwait(false);
    }
    internal static async ValueTask<IReadOnlyList<CharacterId>> EndOrdinaryRunCoreAsync(IDungeonRunDomainService runs, IReadOnlyList<SelectedSessionIdentity> members, DungeonRunId runId, CancellationToken token)
    {
        var ended = new List<CharacterId>();
        foreach (var member in members)
        {
            token.ThrowIfCancellationRequested();
            if (await runs.EndAsync(member.CharacterId, runId).ConfigureAwait(false)) ended.Add(member.CharacterId);
        }
        return ended;
    }

    protected virtual async ValueTask SettleAltarExitAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, DungeonRunId runId, bool onlyCaller, CancellationToken ct)
    {
        var run = runs.Find(session.CharacterId);
        if (run?.RunId != runId || !sessions.IsInWorld(session)
            || !onlyCaller && (run.Layout.Altar is null || !run.Participants.Any(p => p.CharacterId == session.CharacterId && p.IsLeader))) return;
        if (altars.Find(runId) is null) return;
        var cohort = AltarTransitionProjection.Participants(runs, sessions, registry, runId);
        if (!cohort.Contains(session)) return;
        var action = await workUnits!.WriteAsync(execution, new("settle-altar-before-exit",
            ProtectedParticipants(cohort, session)),
            async (_, token) => new AltarExitResult(await altars.SettleBeforeExitAsync(runId, token,
                onlyCaller ? session.CharacterId : null).ConfigureAwait(false)), ct).ConfigureAwait(false);
        if (action.Action is not { } transition) return;
        var recipients = AltarTransitionProjection.Participants(runs, sessions, registry, transition.State.RunId);
        await AltarTransitionProjection.ProjectAsync(projections, transition, recipients, clock, wake, logger, ct).ConfigureAwait(false);
    }
    private sealed record AltarExitResult(AltarActionResult? Action);

    protected virtual async ValueTask<bool> SelectBossReturnAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, GameMessageMetadata metadata, byte state, byte option, CancellationToken token)
    {
        var expected = bossTowers?.Find(session);
        if (expected is null) return false;
        if (BossTowerProjectionDelivery.Stale(metadata)) return true;
        if (!BossTowerExact(session, expected)) return true;
        if (state == 2)
        {
            await register(session.ConnectionId, new([new EplpCommandResponse(state, option)], []), token).ConfigureAwait(false);
            return true;
        }
        if (state != 1 || option > 2) return true;
        var action = await SelectBossResultAsync(execution, expected, session, option, token).ConfigureAwait(false);
        if (!action.Changed || action.Run is not { } run) return true;
        var frozen = BossTowerExactMembers(run);
        var failures = (await BossTowerSendToExactAsync(run, _ => [new EplpCommandResponse(state, option)], token).ConfigureAwait(false)).ToList();
        if (option == 2)
        {
            foreach (var member in frozen)
                _ = await LeaveBossPartyTowerAsync(execution, BossTowerContext(run, member), BossTowerMembers(run), CancellationToken.None).ConfigureAwait(false);
            var entries = await BossTowerCaptureReturnEntriesAsync(frozen, null, failures, CancellationToken.None, false).ConfigureAwait(false);
            await ReturnTownIsolatedAsync(entries, failures, CancellationToken.None).ConfigureAwait(false);
        }
        await BossTowerCloseProjectionFailuresAsync(failures, expected.RunId).ConfigureAwait(false);
        return true;
    }

    protected virtual async ValueTask<bool> SelectIllusionReturnAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, GameMessageMetadata metadata, byte state, byte option, CancellationToken token)
    {
        if (IllusionTowerProjectionDelivery.Stale(metadata)) return true;
        var expected = illusionTowers?.Find(session);
        if (expected is null) return false;
        if (state != EplpCommandHandler.LeaveState) return true;
        if (!IllusionTowerExact(session, expected)) return true;
        var action = await workUnits!.WriteAsync(execution, new("select-illusion-tower-return", []),
            (_, ct) => illusionTowers!.SelectReturnAsync(IllusionTowerContext(expected, session), option, ct), token).ConfigureAwait(false);
        Exception? failure = null;
        if (action.Changed)
        {
            try { await register(session.ConnectionId, new([new EplpCommandResponse(state, option)], []), token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { failure = error; }
        }
        if (failure is not null) await IllusionTowerCloseProjectionFailuresAsync([(session, failure)], expected.RunId).ConfigureAwait(false);
        return true;
    }
    protected virtual ValueTask<Dfo.Server.Abstractions.World.BossTower.BossTowerActionResult> SelectBossResultAsync(CommandExecutionContext execution,
        Dfo.Server.Abstractions.World.BossTower.BossTowerSnapshot expected, SelectedSessionIdentity session, byte option, CancellationToken token) =>
        workUnits!.WriteAsync(execution, new("select-boss-tower-result", ProtectedParticipants(BossTowerMembers(expected), session)),
            (_, ct) => bossTowers.SelectResultAsync(BossTowerContext(expected, session), option, ct), token);

    protected virtual async ValueTask<bool> TryGiveUpTowersAsync(CommandExecutionContext execution, SelectedSessionIdentity session, CancellationToken ct)
    {
        if (bossTowers is not null && (await CoordinateBossTowerPartyAsync(execution, PartyChange.Leave, session, null, true, new GiveUpGameResponse(), ct).ConfigureAwait(false)).Handled) return true;
        return illusionTowers is not null && (await CoordinateIllusionTowerPartyAsync(execution, PartyChange.Leave, session, null, true, new GiveUpGameResponse(), ct).ConfigureAwait(false)).Handled;
    }

}
