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
internal partial class GameMapLoadingCommandHandler
{
    protected virtual ValueTask<BossTowerActionResult> BossLoadingUnitAsync(CommandExecutionContext execution, string name,
        BossTowerSnapshot run, SelectedSessionIdentity actor, Func<CancellationToken, ValueTask<BossTowerActionResult>> action, CancellationToken token) =>
        workUnits!.WriteAsync(execution, new(name, run.Members.Select(member => member.Session == actor
            ? CaptureParticipant(member.Session) : new SelectedSessionRecipient(member.Session, null)).ToArray()), (_, ct) => action(ct), token);
    protected virtual ValueTask<TowerActionResult> IllusionLoadingUnitAsync(CommandExecutionContext execution, string name,
        Func<CancellationToken, ValueTask<TowerActionResult>> action, CancellationToken token) =>
        workUnits!.WriteAsync(execution, new(name, []), (_, ct) => action(ct), token);
    private Dictionary<SelectedSessionIdentity, NetworkConnectionContext?> CaptureLoadingConnections(IEnumerable<SelectedSessionIdentity> members) =>
        members.ToDictionary(member => member, member => onlineUsers?.TryGetByConnection(member.ConnectionId, out var user) == true && user is not null
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
    protected virtual async ValueTask<bool> CompleteBossLoadingAsync(CommandExecutionContext execution, NetworkConnectionId connectionId,
        GameMessageMetadata metadata, CancellationToken token)
    {
        if (IllusionTowerProjectionDelivery.Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : bossTowers.Find(session);
        if (session is null || expected is null) return false;
        var captured = CaptureLoadingConnections(expected.Members.Select(member => member.Session));
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures = [];
        if (!BossTowerExact(session, expected)) return true;
        var action = await BossLoadingUnitAsync(execution, "load-boss-tower", expected, session, ct => bossTowers.LoadAsync(BossTowerContext(expected, session), ct), token).ConfigureAwait(false);
        if (action.Run is not { Room.Phase: TowerRoomPhase.Ready } run) return true;
        var leader = run.Members.Single(value => value.IsLeader).Session;
        var started = await BossLoadingUnitAsync(execution, "start-boss-tower", run, leader, ct => bossTowers.StartAsync(BossTowerContext(run, leader), ct), token).ConfigureAwait(false);
        if (started.Run is not { Room.Phase: TowerRoomPhase.Fighting } fighting) return true;
        failures = await BossTowerSendToExactAsync(
            fighting, _ => [new LoadingCompleteNotification()], token).ConfigureAwait(false);
        await CloseLoadingFailuresAsync(failures, captured).ConfigureAwait(false);
        return true;
    }

    protected virtual async ValueTask<bool> AdvanceBossAsync(CommandExecutionContext execution, NetworkConnectionId connectionId,
        GameMessageMetadata metadata, byte targetX, byte targetY, CancellationToken token)
    {
        if (IllusionTowerProjectionDelivery.Stale(metadata))
        {
            logger.LogWarning("Move-map ignored connection={ConnectionId} sequence={Sequence} ordinal={Ordinal} target=({X},{Y}) reason=StaleSequence",
                connectionId, metadata.Sequence, metadata.InboundOrdinal, targetX, targetY);
            return true;
        }
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : bossTowers.Find(session);
        if (session is null || expected is null) return false;
        logger.LogInformation("Boss tower move requested connection={ConnectionId} character={CharacterId} sequence={Sequence} ordinal={Ordinal} run={RunId} roomVersion={RoomVersion} stage={Stage} phase={Phase} target=({X},{Y})",
            connectionId, session.CharacterId.Value, metadata.Sequence, metadata.InboundOrdinal, expected.RunId,
            expected.Room.Version, expected.Room.Level, expected.Room.Phase, targetX, targetY);
        var captured = CaptureLoadingConnections(expected.Members.Select(member => member.Session));
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures = [];
        if (!BossTowerExact(session, expected))
        {
            logger.LogWarning("Boss tower move refused character={CharacterId} run={RunId} roomVersion={RoomVersion} reason=SnapshotOrSessionChanged",
                session.CharacterId.Value, expected.RunId, expected.Room.Version);
            return true;
        }
        var action = await BossLoadingUnitAsync(execution, "advance-boss-tower", expected, session, ct => bossTowers.NextLevelAsync(BossTowerContext(expected, session), targetX, targetY, ct), token).ConfigureAwait(false);
        logger.LogInformation("Boss tower move result character={CharacterId} run={RunId} changed={Changed} stage={Stage} map={MapId} phase={Phase} roomVersion={RoomVersion}",
            session.CharacterId.Value, expected.RunId, action.Changed, action.Run?.Room.Level,
            action.Run?.Room.MapId, action.Run?.Room.Phase, action.Run?.Room.Version);
        if (action.Changed && action.Run is { } run)
            failures = await BossTowerSendToExactAsync(
                run, _ => [new BossTowerMapNotification(run.Room)], token).ConfigureAwait(false);
        await CloseLoadingFailuresAsync(failures, captured).ConfigureAwait(false);
        return true;
    }

    protected virtual async ValueTask<bool> CompleteIllusionLoadingAsync(CommandExecutionContext execution, NetworkConnectionId connectionId,
        GameMessageMetadata metadata, CancellationToken token)
    {
        if (IllusionTowerProjectionDelivery.Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : illusionTowers.Find(session);
        if (session is null || expected is null) return false;
        var captured = CaptureLoadingConnections(expected.Members.Select(member => member.Session));
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures = [];
        if (!IllusionTowerExact(session, expected)) return true;
        var action = await IllusionLoadingUnitAsync(execution, "load-illusion-tower", ct => illusionTowers.LoadAsync(IllusionTowerContext(expected, session), ct), token).ConfigureAwait(false);
        if (action.Run is not { Room.Phase: TowerRoomPhase.Ready } run) return true;
        failures = await IllusionTowerSendToExactAsync(
            run, _ => [new LoadingCompleteNotification()], token).ConfigureAwait(false);
        await CloseLoadingFailuresAsync(failures, captured).ConfigureAwait(false);
        return true;
    }

    protected virtual async ValueTask<bool> StartIllusionAsync(CommandExecutionContext execution, NetworkConnectionId connectionId,
        GameMessageMetadata metadata, CancellationToken token)
    {
        if (IllusionTowerProjectionDelivery.Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : illusionTowers.Find(session);
        if (session is null || expected is null) return false;
        if (IllusionTowerExact(session, expected))
            _ = await IllusionLoadingUnitAsync(execution, "start-illusion-tower", ct => illusionTowers.StartAsync(IllusionTowerContext(expected, session), ct), token).ConfigureAwait(false);
        return true;
    }

    protected virtual async ValueTask<bool> AdvanceIllusionAsync(CommandExecutionContext execution, NetworkConnectionId connectionId,
        GameMessageMetadata metadata, CancellationToken token)
    {
        if (IllusionTowerProjectionDelivery.Stale(metadata)) return true;
        var session = sessions.FindByConnection(connectionId);
        var expected = session is null ? null : illusionTowers.Find(session);
        if (session is null || expected is null) return false;
        var captured = CaptureLoadingConnections(expected.Members.Select(member => member.Session));
        IReadOnlyList<(SelectedSessionIdentity Session, Exception Error)> failures = [];
        if (!IllusionTowerExact(session, expected)) return true;
        var action = await IllusionLoadingUnitAsync(execution, "advance-illusion-tower", ct => illusionTowers.NextLevelAsync(IllusionTowerContext(expected, session), ct), token).ConfigureAwait(false);
        if (action.Changed && action.Run is { } run)
            failures = await IllusionTowerSendToExactAsync(
                run, _ => [new IllusionTowerMapNotification(run.Room)], token).ConfigureAwait(false);
        await CloseLoadingFailuresAsync(failures, captured).ConfigureAwait(false);
        return true;
    }


}
