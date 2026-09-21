using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Weakness;

namespace Dfo.Server.Protocol.Game.Dungeons;

/// <summary>Serializes every projection and loading transition for one shared dungeon run.</summary>
public sealed class DungeonProjectionGate : UserExecutionGate<DungeonRunId>;

/// <summary>Validates and submits one exact selected-session generation's loading completion.</summary>
public interface IDungeonLoadingProjectionDelivery
{
    ValueTask<bool> CompleteLoadingAsync(
        NetworkConnectionId connectionId,
        Func<CancellationToken, ValueTask> releaseClient,
        CancellationToken cancellationToken);
}

public sealed record MarkDungeonParticipantReadyCommand(
    CharacterId CharacterId,
    DungeonRunId RunId,
    long RoomVersion) : ICommand<MarkDungeonParticipantReadyResult>;

public sealed record MarkDungeonParticipantReadyResult(DungeonEntryChange? Change);

public sealed class MarkDungeonParticipantReadyCommandHandler(IDungeonRunDomainService runs)
    : ICommandHandler<MarkDungeonParticipantReadyCommand, MarkDungeonParticipantReadyResult>
{
    public ValueTask<MarkDungeonParticipantReadyResult> HandleAsync(
        CommandExecutionContext context,
        MarkDungeonParticipantReadyCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new MarkDungeonParticipantReadyResult(
            runs.TryMarkParticipantReady(
                command.CharacterId,
                command.RunId,
                command.RoomVersion)));
}

/// <summary>
/// Bridges FINISH_LOADING into the transport-free dungeon instance without allowing a replacement
/// connection to inherit or mutate the previous selected-session generation's run.
/// </summary>
public sealed class DungeonLoadingProjectionDelivery(
    ISelectedSessionDomainService selectedSessions,
    IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry deliveryRegistry,
    SelectedSessionTransitionGate transitionGate,
    DungeonProjectionGate projectionGate,
    ICommandBus commands,
    WeaknessProjectionDelivery? weaknessProjection = null) : IDungeonLoadingProjectionDelivery
{
    public async ValueTask<bool> CompleteLoadingAsync(
        NetworkConnectionId connectionId,
        Func<CancellationToken, ValueTask> releaseClient,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(releaseClient);
        var session = selectedSessions.FindByConnection(connectionId);
        if (session is null)
            return false;

        var expected = runs.Find(session.CharacterId);
        // FINISH_LOADING also closes ordinary town loads. There is no dungeon state to mutate,
        // but the response must still belong to the exact selected-session generation.
        if (expected is null)
        {
            await using var townTransition = await transitionGate.AcquireAsync(
                session.AccountId, UserAccessMode.Exclusive, cancellationToken)
                .ConfigureAwait(false);
            if (!IsCurrentSession(session, connectionId))
                return false;
            await releaseClient(cancellationToken).ConfigureAwait(false);
            return true;
        }

        // Lock order is shared by entry: dungeon projection first, account transition second.
        // Replacement/teardown uses the account gate and therefore cannot cross the CMD boundary.
        await using var projection = await projectionGate.AcquireAsync(
            expected.RunId, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);
        await using var transition = await transitionGate.AcquireAsync(
            session.AccountId, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);

        if (!IsCurrentSession(session, connectionId)
            || deliveryRegistry.Find(expected.RunId).SingleOrDefault(value =>
                value.CharacterId == session.CharacterId) != session)
        {
            return false;
        }

        var current = runs.Find(session.CharacterId);
        if (current is null
            || current.RunId != expected.RunId
            || current.RoomVersion != expected.RoomVersion)
        {
            return false;
        }

        // Entry USERINFO refreshes the client's combat attributes. Restore the retained
        // weakness after the first map loads, once per participant. Room changes and duplicate
        // loading reports must not reapply a notification that can also reset current HP/MP.
        var restoreEntryWeakness = current.RoomVersion == 0
            && !current.ReadyParticipants.Contains(session.CharacterId);

        _ = await commands.SendAsync(
            new MarkDungeonParticipantReadyCommand(
                session.CharacterId,
                expected.RunId,
                expected.RoomVersion),
            cancellationToken).ConfigureAwait(false);

        current = runs.Find(session.CharacterId);
        var accepted = current is not null
            && current.RunId == expected.RunId
            && current.RoomVersion == expected.RoomVersion
            && current.ReadyParticipants.Contains(session.CharacterId)
            && IsCurrentSession(session, connectionId)
            && deliveryRegistry.Find(expected.RunId).SingleOrDefault(value =>
                value.CharacterId == session.CharacterId) == session;
        if (!accepted)
            return false;

        // The direct response belongs to this frozen generation too. Keep both leases until the
        // frame has been accepted by the required connection, closing the validation/send race.
        await releaseClient(cancellationToken).ConfigureAwait(false);
        if (restoreEntryWeakness && weaknessProjection is not null)
            await weaknessProjection.SendCurrentAsync(session, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private bool IsCurrentSession(
        Dfo.Server.Data.World.SelectedSessionIdentity session,
        NetworkConnectionId connectionId) =>
        selectedSessions.FindByConnection(connectionId) == session
        && selectedSessions.FindByCharacter(session.CharacterId) == session;
}
