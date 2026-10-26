using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.TownProjection;

namespace Dfo.Server.Protocol.Game.DungeonClear;

internal sealed partial class GameDungeonExitCommandHandler
{
    private async ValueTask<IReadOnlyList<CharacterId>> EndOrdinaryRunAsync(
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

    private async ValueTask<bool> SelectBossReturnAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, GameMessageMetadata metadata, byte state, byte option, CancellationToken token)
    {
        var expected = bossTowers.Find(session);
        if (expected is null) return false;
        if (TowerProtocol.IsStale(metadata)) return true;
        if (!BossTowerActionProjection.BossTowerExact(sessions, bossTowers, session, expected)) return true;
        if (state == 2)
        {
            await outbox.RegisterExplicitAsync(session.ConnectionId, new([new EplpCommandResponse(state, option)], []), token).ConfigureAwait(false);
            return true;
        }
        if (state != 1 || option > 2) return true;
        var action = await SelectBossResultAsync(execution, expected, session, option, token).ConfigureAwait(false);
        if (!action.Changed || action.Run is not { } run) return true;
        var frozen = BossTowerActionProjection.BossTowerExactMembers(sessions, bossTowers, run);
        var failures = (await BossTowerActionProjection.BossTowerSendToExactAsync(sessions, bossTowers, outbox, run, [new EplpCommandResponse(state, option)], token).ConfigureAwait(false)).ToList();
        if (option == 2)
        {
            foreach (var member in frozen)
                _ = await LeaveBossPartyTowerAsync(execution, BossTowerActionProjection.BossTowerContext(run, member), BossTowerActionProjection.BossTowerMembers(run), CancellationToken.None).ConfigureAwait(false);
            var entries = await BossTowerActionProjection.BossTowerCaptureReturnEntriesAsync(sessions, currentViews, currentState, onlineCharacters, outbox, frozen, null, failures, CancellationToken.None, false).ConfigureAwait(false);
            await ReturnTownIsolatedAsync(entries, failures, CancellationToken.None).ConfigureAwait(false);
        }
        await BossTowerActionProjection.BossTowerCloseProjectionFailuresAsync(sessions, logger, closures, onlineUsers, failures, expected.RunId).ConfigureAwait(false);
        return true;
    }

    private async ValueTask<bool> SelectIllusionReturnAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, GameMessageMetadata metadata, byte state, byte option, CancellationToken token)
    {
        if (TowerProtocol.IsStale(metadata)) return true;
        var expected = illusionTowers.Find(session);
        if (expected is null) return false;
        if (state != EplpCommandHandler.LeaveState) return true;
        if (!IllusionTowerActionProjection.IllusionTowerExact(sessions, illusionTowers, session, expected)) return true;
        var action = await workUnits.WriteAsync(execution, new("select-illusion-tower-return", []),
            (_, ct) => illusionTowers.SelectReturnAsync(IllusionTowerActionProjection.IllusionTowerContext(expected, session), option, ct), token).ConfigureAwait(false);
        Exception? failure = null;
        if (action.Changed)
        {
            try { await outbox.RegisterExplicitAsync(session.ConnectionId, new([new EplpCommandResponse(state, option)], []), token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) { failure = error; }
        }
        if (failure is not null) await IllusionTowerActionProjection.IllusionTowerCloseProjectionFailuresAsync(sessions, logger, closures, onlineUsers, [(session, failure)], expected.RunId).ConfigureAwait(false);
        return true;
    }
    private ValueTask<Dfo.Server.Abstractions.World.BossTower.BossTowerActionResult> SelectBossResultAsync(CommandExecutionContext execution,
        Dfo.Server.Abstractions.World.BossTower.BossTowerSnapshot expected, SelectedSessionIdentity session, byte option, CancellationToken token) =>
        workUnits.WriteAsync(execution, new("select-boss-tower-result", ProtectedParticipants(BossTowerActionProjection.BossTowerMembers(expected), session)),
            (_, ct) => bossTowers.SelectResultAsync(BossTowerActionProjection.BossTowerContext(expected, session), option, ct), token);

    private async ValueTask<bool> TryGiveUpTowersAsync(CommandExecutionContext execution, SelectedSessionIdentity session, CancellationToken ct)
    {
        if ((await CoordinateBossTowerPartyAsync(execution, PartyChange.Leave, session, null, true, new GiveUpGameResponse(), ct).ConfigureAwait(false)).Handled) return true;
        return (await CoordinateIllusionTowerPartyAsync(execution, PartyChange.Leave, session, null, true, new GiveUpGameResponse(), ct).ConfigureAwait(false)).Handled;
    }

}
