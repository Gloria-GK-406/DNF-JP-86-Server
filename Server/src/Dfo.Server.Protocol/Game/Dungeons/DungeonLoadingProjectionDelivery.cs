using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Weakness;

namespace Dfo.Server.Protocol.Game.Dungeons;

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
    ICommandBus commands,
    WeaknessProjectionDelivery? weaknessProjection = null,
    Dfo.Server.Protocol.Game.StateSynchronization.UsageQuotaProjectionDelivery? quotaProjection = null,
    Dfo.Server.Protocol.Game.RoomMechanisms.RoomMechanismProjectionDelivery? mechanismProjection = null) : IDungeonLoadingProjectionDelivery
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
            if (!IsCurrentSession(session, connectionId))
                return false;
            await releaseClient(cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (!IsCurrentSession(session, connectionId)
            || deliveryRegistry.Find(expected.RunId).SingleOrDefault(value =>
                value.CharacterId == session.CharacterId) != session)
        {
            return false;
        }

        // Entry USERINFO refreshes the client's combat attributes. Restore the retained
        // weakness after the first map loads, once per participant. Room changes and duplicate
        // loading reports must not reapply a notification that can also reset current HP/MP.
        var restoreEntryWeakness = expected.RoomVersion == 0
            && !expected.ReadyParticipants.Contains(session.CharacterId);

        _ = await commands.SendAsync(
            new MarkDungeonParticipantReadyCommand(
                session.CharacterId,
                expected.RunId,
                expected.RoomVersion),
            cancellationToken).ConfigureAwait(false);

        var current = runs.Find(session.CharacterId);
        var accepted = current is not null
            && current.RunId == expected.RunId
            && current.RoomVersion == expected.RoomVersion
            && current.ReadyParticipants.Contains(session.CharacterId)
            && IsCurrentSession(session, connectionId)
            && deliveryRegistry.Find(expected.RunId).SingleOrDefault(value =>
                value.CharacterId == session.CharacterId) == session;
        if (!accepted)
            return false;

        // The direct response belongs to this frozen generation too; it follows the validation above.
        await releaseClient(cancellationToken).ConfigureAwait(false);
        if (mechanismProjection is not null)
            await mechanismProjection.SendCurrentAsync(session, current!, cancellationToken).ConfigureAwait(false);
        if (current!.Layout.Mirror is not null && quotaProjection is not null)
            _ = await quotaProjection.SendAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.MirrorDungeon,
                cancellationToken).ConfigureAwait(false);
        if (current!.Layout.Altar is not null && quotaProjection is not null)
            _ = await quotaProjection.SendAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.AltarDungeon,
                cancellationToken).ConfigureAwait(false);
        if (current!.Layout.Tournament is not null && quotaProjection is not null)
            _ = await quotaProjection.SendAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.TournamentDungeon,
                cancellationToken).ConfigureAwait(false);
        if (current!.Layout.DungeonId == 3900 && quotaProjection is not null)
            _ = await quotaProjection.SendAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.HerasDungeon, cancellationToken).ConfigureAwait(false);
        if (restoreEntryWeakness && weaknessProjection is not null)
            await weaknessProjection.SendCurrentAsync(session, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private bool IsCurrentSession(
        Dfo.Server.Data.World.SelectedSessionIdentity session,
        NetworkConnectionId connectionId) =>
        session.ConnectionId == connectionId
        && selectedSessions.IsInWorld(session);
}
