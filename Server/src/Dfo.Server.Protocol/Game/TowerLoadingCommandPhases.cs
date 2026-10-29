using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.IllusionTowerStage;
using Dfo.Server.Protocol.Game.BossTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.Dungeons;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game;
// Finish-loading and move-map tower channels receive a requester already located in their tower,
// with any stale request dropped by the caller. The 0x009F start has only the illusion owner and
// still checks its own frame and run.
internal sealed partial class GameMapLoadingCommandHandler
{
    private ValueTask<BossTowerActionResult> BossLoadingUnitAsync(CommandExecutionContext execution, string name,
        BossTowerSnapshot run, SelectedSessionIdentity actor, Func<CancellationToken, ValueTask<BossTowerActionResult>> action, CancellationToken token) =>
        workUnits.WriteAsync(execution, new(name, run.Members.Select(member => member.Session == actor
            ? CaptureParticipant(member.Session) : new SelectedSessionRecipient(member.Session, null)).ToArray()), (_, ct) => action(ct), token);
    private ValueTask<TowerActionResult> IllusionLoadingUnitAsync(CommandExecutionContext execution, string name,
        Func<CancellationToken, ValueTask<TowerActionResult>> action, CancellationToken token) =>
        workUnits.WriteAsync(execution, new(name, []), (_, ct) => action(ct), token);
    private Dictionary<SelectedSessionIdentity, NetworkConnectionContext?> CaptureLoadingConnections(IEnumerable<SelectedSessionIdentity> members) =>
        members.ToDictionary(member => member, member => onlineUsers.TryGetByConnection(member.ConnectionId, out var user) == true && user is not null
            && user.AccountId == member.AccountId && user.CurrentCharacterId == member.CharacterId ? user.Connection : null);
    private async ValueTask CloseLoadingFailuresAsync(IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures,
        Dictionary<SelectedSessionIdentity, NetworkConnectionContext?> captured)
    {
        foreach (var (session, error) in failures.DistinctBy(value => value.Session))
        {
            logger.LogWarning(error, "Tower loading projection failed for exact session {ConnectionId}", session.ConnectionId);
            if (sessions.IsInWorld(session) && captured.GetValueOrDefault(session) is { } connection)
                await closures.RequestCloseAsync(connection, CancellationToken.None).ConfigureAwait(false);
        }
    }
    internal async ValueTask CompleteBossLoadingAsync(CommandExecutionContext execution, DungeonPlace.InBossTower place, CancellationToken token)
    {
        var session = place.Session; var expected = place.Run;
        var captured = CaptureLoadingConnections(expected.Members.Select(member => member.Session));
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures = [];
        if (!TowerActionProjection.Exact(sessions, bossKind, session, expected)) return;
        var action = await BossLoadingUnitAsync(execution, "load-boss-tower", expected, session, ct => bossTowers.LoadAsync(TowerActionProjection.Context(expected, session), ct), token).ConfigureAwait(false);
        if (action.Run is not { Room.Phase: TowerRoomPhase.Ready } run) return;
        var leader = run.Members.Single(value => value.IsLeader).Session;
        var started = await BossLoadingUnitAsync(execution, "start-boss-tower", run, leader, ct => bossTowers.StartAsync(TowerActionProjection.Context(run, leader), ct), token).ConfigureAwait(false);
        if (started.Run is not { Room.Phase: TowerRoomPhase.Fighting } fighting) return;
        failures = await TowerActionProjection.SendToExactAsync(sessions, bossKind, outbox,
            fighting, [new LoadingCompleteNotification()], token).ConfigureAwait(false);
        await CloseLoadingFailuresAsync(failures, captured).ConfigureAwait(false);
    }

    private async ValueTask AdvanceBossAsync(CommandExecutionContext execution, NetworkConnectionId connectionId,
        DungeonPlace.InBossTower place, GameMessageMetadata metadata, byte targetX, byte targetY, CancellationToken token)
    {
        var session = place.Session; var expected = place.Run;
        logger.LogInformation("Boss tower move requested connection={ConnectionId} character={CharacterId} sequence={Sequence} ordinal={Ordinal} run={RunId} roomVersion={RoomVersion} stage={Stage} phase={Phase} target=({X},{Y})",
            connectionId, session.CharacterId.Value, metadata.Sequence, metadata.InboundOrdinal, expected.RunId,
            expected.Room.Version, expected.Room.Level, expected.Room.Phase, targetX, targetY);
        var captured = CaptureLoadingConnections(expected.Members.Select(member => member.Session));
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures = [];
        if (!TowerActionProjection.Exact(sessions, bossKind, session, expected))
        {
            logger.LogWarning("Boss tower move refused character={CharacterId} run={RunId} roomVersion={RoomVersion} reason=SnapshotOrSessionChanged",
                session.CharacterId.Value, expected.RunId, expected.Room.Version);
            return;
        }
        var action = await BossLoadingUnitAsync(execution, "advance-boss-tower", expected, session, ct => bossTowers.NextLevelAsync(TowerActionProjection.Context(expected, session), targetX, targetY, ct), token).ConfigureAwait(false);
        logger.LogInformation("Boss tower move result character={CharacterId} run={RunId} changed={Changed} stage={Stage} map={MapId} phase={Phase} roomVersion={RoomVersion}",
            session.CharacterId.Value, expected.RunId, action.Changed, action.Run?.Room.Level,
            action.Run?.Room.MapId, action.Run?.Room.Phase, action.Run?.Room.Version);
        if (action.Changed && action.Run is { } run)
            failures = await TowerActionProjection.SendToExactAsync(sessions, bossKind, outbox,
                run, [new BossTowerMapNotification(run.Room)], token).ConfigureAwait(false);
        await CloseLoadingFailuresAsync(failures, captured).ConfigureAwait(false);
    }

    private async ValueTask CompleteIllusionLoadingAsync(CommandExecutionContext execution, DungeonPlace.InIllusionTower place, CancellationToken token)
    {
        var session = place.Session; var expected = place.Run;
        var captured = CaptureLoadingConnections(expected.Members.Select(member => member.Session));
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures = [];
        if (!TowerActionProjection.Exact(sessions, illusionKind, session, expected)) return;
        var action = await IllusionLoadingUnitAsync(execution, "load-illusion-tower", ct => illusionTowers.LoadAsync(TowerActionProjection.Context(expected, session), ct), token).ConfigureAwait(false);
        if (action.Run is not { Room.Phase: TowerRoomPhase.Ready } run) return;
        failures = await TowerActionProjection.SendToExactAsync(sessions, illusionKind, outbox,
            run, [new LoadingCompleteNotification()], token).ConfigureAwait(false);
        await CloseLoadingFailuresAsync(failures, captured).ConfigureAwait(false);
    }

    private async ValueTask<bool> StartIllusionAsync(CommandExecutionContext execution, NetworkConnectionId connectionId,
        GameMessageMetadata metadata, CancellationToken token)
    {
        if (GameRequestFreshness.IsStale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : illusionTowers.Find(session);
        if (session is null || expected is null) return false;
        if (TowerActionProjection.Exact(sessions, illusionKind, session, expected))
            _ = await IllusionLoadingUnitAsync(execution, "start-illusion-tower", ct => illusionTowers.StartAsync(TowerActionProjection.Context(expected, session), ct), token).ConfigureAwait(false);
        return true;
    }

    private async ValueTask AdvanceIllusionAsync(CommandExecutionContext execution, DungeonPlace.InIllusionTower place, CancellationToken token)
    {
        var session = place.Session; var expected = place.Run;
        var captured = CaptureLoadingConnections(expected.Members.Select(member => member.Session));
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures = [];
        if (!TowerActionProjection.Exact(sessions, illusionKind, session, expected)) return;
        var action = await IllusionLoadingUnitAsync(execution, "advance-illusion-tower", ct => illusionTowers.NextLevelAsync(TowerActionProjection.Context(expected, session), ct), token).ConfigureAwait(false);
        if (action.Changed && action.Run is { } run)
            failures = await TowerActionProjection.SendToExactAsync(sessions, illusionKind, outbox,
                run, [new IllusionTowerMapNotification(run.Room)], token).ConfigureAwait(false);
        await CloseLoadingFailuresAsync(failures, captured).ConfigureAwait(false);
    }
}
