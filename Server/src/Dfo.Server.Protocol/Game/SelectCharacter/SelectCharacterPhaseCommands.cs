using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

public sealed record PrepareSelectedCharacterSessionCommand(
    AccountId AccountId,
    ushort SlotIndex,
    ChannelEntryIdentity Channel,
    NetworkConnectionId ConnectionId,
    long ExpectedConnectionGeneration)
    : ICommand<PreparedSelectedCharacterSession>, IAccountCommand;

public sealed record PreparedSelectedCharacterSession(
    SelectedSessionIdentity Session,
    SelectCharacterResult State,
    IReadOnlyList<SelectedSessionCleanupNotificationPlan> CleanupNotifications,
    IReadOnlyList<NetworkConnectionId> ConnectionsToClose);

public sealed class PrepareSelectedCharacterSessionCommandHandler(
    IOnlineUserQueryService onlineUsers,
    ISelectCharacterPreparationService preparation,
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionCleanupService cleanup,
    ISelectCharacterEntryInventoryService inventoryEntry,
    ICharacterEntryMutationRepository characterMutations)
    : ICommandHandler<PrepareSelectedCharacterSessionCommand, PreparedSelectedCharacterSession>
{
    public async ValueTask<PreparedSelectedCharacterSession> HandleAsync(
        CommandExecutionContext context,
        PrepareSelectedCharacterSessionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureExpectedConnection(command);
        var selected = await preparation.PreflightAsync(
            command.AccountId, command.SlotIndex, command.Channel, cancellationToken)
            .ConfigureAwait(false);

        // This must precede every lifecycle side effect. A selected character that cannot be
        // represented by the game protocol is not an accepted session.
        _ = SelectedSessionIdentity.ToWireUserId(selected.Identity.CharacterId);
        var cleanupNotifications = new List<SelectedSessionCleanupNotificationPlan>();
        var previous = selectedSessions.Find(command.AccountId);
        if (previous is not null)
            await inventoryEntry.SaveExistingLeaseAsync(previous, cancellationToken).ConfigureAwait(false);
        if (previous?.ConnectionId == command.ConnectionId)
        {
            try
            {
                cleanupNotifications.Add(await cleanup.CleanupAsync(
                    previous, includeOwner: true, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception error)
            {
                if (error is SelectedSessionCleanupFailure cleanupFailure)
                    cleanupNotifications.Add(cleanupFailure.NotificationPlan);
                _ = selectedSessions.TryRemoveExact(previous);
                throw SelectCharacterPhaseFailure.Create(
                    error, [command.ConnectionId], cleanupNotifications);
            }
        }

        if (!onlineUsers.TryGetByConnection(command.ConnectionId, out var refreshed)
            || refreshed?.AccountId != command.AccountId
            || (previous?.ConnectionId != command.ConnectionId
                && refreshed.Generation != command.ExpectedConnectionGeneration))
        {
            throw new InvalidOperationException(
                "Select-character connection changed during cleanup.");
        }

        var session = SelectedSessionIdentity.Create(
            command.AccountId,
            selected.Identity.CharacterId,
            command.ConnectionId,
            refreshed.Generation,
            command.Channel.ListenerPort);
        var pendingCloses = new List<NetworkConnectionId>();
        try
        {
            var displaced = selectedSessions.RegisterReplacing(session);
            if (displaced is not null)
            {
                pendingCloses.Add(displaced.ConnectionId);
                try
                {
                    cleanupNotifications.Add(await cleanup.CleanupAsync(
                        displaced, includeOwner: false, cancellationToken).ConfigureAwait(false));
                }
                finally
                {
                    _ = selectedSessions.TryRemoveExact(displaced);
                }
            }

            var prepared = await preparation.PrepareAsync(
                selected, command.AccountId, command.Channel, cancellationToken)
                .ConfigureAwait(false);
            var spawn = prepared.World.Channel.Spawn;
            var resolvedPosition = new CharacterPositionState(
                spawn.TownId, spawn.AreaId, spawn.X, spawn.Y, spawn.Direction);
            prepared = prepared with
            {
                Character = prepared.Character with { Position = resolvedPosition },
            };
            prepared = await inventoryEntry.PrepareLeaseAsync(session, prepared, cancellationToken)
                .ConfigureAwait(false);
            if (prepared.World.Channel.PersistsPosition)
            {
                await characterMutations.PersistPositionAsync(
                    command.AccountId,
                    session.CharacterId,
                    resolvedPosition,
                    cancellationToken).ConfigureAwait(false);
            }

            return new PreparedSelectedCharacterSession(
                session, prepared, cleanupNotifications, pendingCloses);
        }
        catch (Exception error)
        {
            if (error is SelectedSessionCleanupFailure phaseCleanupFailure)
                cleanupNotifications.Add(phaseCleanupFailure.NotificationPlan);
            try
            {
                cleanupNotifications.Add(await cleanup.CleanupAsync(
                    session, includeOwner: true, CancellationToken.None).ConfigureAwait(false));
            }
            catch (SelectedSessionCleanupFailure cleanupFailure)
            {
                cleanupNotifications.Add(cleanupFailure.NotificationPlan);
                // Preparation failure remains primary; the failed cleanup still exposes its
                // already-resolved notification plan to the protocol handler.
            }
            catch
            {
                // Preparation failure remains the primary failure. Exact removal below is the
                // minimum compensation even when richer cleanup reports its own failure.
            }
            finally
            {
                _ = selectedSessions.TryRemoveExact(session);
            }

            pendingCloses.Add(session.ConnectionId);
            throw SelectCharacterPhaseFailure.Create(
                error, pendingCloses, cleanupNotifications);
        }
    }

    private void EnsureExpectedConnection(PrepareSelectedCharacterSessionCommand command)
    {
        if (!onlineUsers.TryGetByConnection(command.ConnectionId, out var online)
            || online?.AccountId != command.AccountId
            || online.Generation != command.ExpectedConnectionGeneration)
        {
            throw new InvalidOperationException(
                "Select-character connection generation is stale.");
        }
    }
}

public sealed record ApplySelectedCharacterDailyRefillCommand(
    SelectedSessionIdentity Session,
    SelectCharacterResult State)
    : ICommand<EntryInventoryPreparation>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class ApplySelectedCharacterDailyRefillCommandHandler(
    ISelectCharacterEntryInventoryService inventoryEntry)
    : ICommandHandler<ApplySelectedCharacterDailyRefillCommand, EntryInventoryPreparation>
{
    public ValueTask<EntryInventoryPreparation> HandleAsync(
        CommandExecutionContext context,
        ApplySelectedCharacterDailyRefillCommand command,
        CancellationToken cancellationToken) =>
        inventoryEntry.ApplyDailyRefillAsync(command.Session, command.State, cancellationToken);
}

public sealed record PublishSelectedCharacterSessionCommand(
    SelectedSessionIdentity Session,
    SelectCharacterResult State)
    : ICommand<PublishedSelectedCharacterSession>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed record PublishedSelectedCharacterSession(
    PvpLobbySynchronizationState? PvpLobby);

public sealed class PublishSelectedCharacterSessionCommandHandler(
    IActiveCharacterService activeCharacters,
    ISelectCharacterSessionEntryService sessionEntry)
    : ICommandHandler<PublishSelectedCharacterSessionCommand, PublishedSelectedCharacterSession>
{
    public ValueTask<PublishedSelectedCharacterSession> HandleAsync(
        CommandExecutionContext context,
        PublishSelectedCharacterSessionCommand command,
        CancellationToken cancellationToken)
    {
        activeCharacters.Publish(command.Session, command.State.Character);
        sessionEntry.Publish(command.Session, command.State);
        return ValueTask.FromResult(
            new PublishedSelectedCharacterSession(sessionEntry.PlanPvp(command.Session, command.State)));
    }
}

public sealed record MarkSelectedCharacterPvpReadyCommand(
    SelectedSessionIdentity Session,
    SelectCharacterResult State)
    : ICommand<SelectedCharacterPvpPublication>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed record SelectedCharacterPvpPublication(
    IReadOnlyList<PvpParticipantPublicationTarget> Targets);

public sealed class MarkSelectedCharacterPvpReadyCommandHandler(
    ISelectCharacterSessionEntryService sessionEntry)
    : ICommandHandler<MarkSelectedCharacterPvpReadyCommand, SelectedCharacterPvpPublication>
{
    public ValueTask<SelectedCharacterPvpPublication> HandleAsync(
        CommandExecutionContext context,
        MarkSelectedCharacterPvpReadyCommand command,
        CancellationToken cancellationToken)
    {
        var targets = sessionEntry.PlanPvpPublication(command.Session, command.State);
        sessionEntry.MarkPvpReady(command.Session, command.State);
        return ValueTask.FromResult(new SelectedCharacterPvpPublication(targets));
    }
}

public sealed record SynchronizeSelectedCharacterQuestsCommand(
    SelectedSessionIdentity Session,
    SelectCharacterResult State)
    : ICommand<SynchronizedSelectedCharacterQuests>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed record SynchronizedSelectedCharacterQuests(
    SelectCharacterQuestSynchronization? Change);

public sealed class SynchronizeSelectedCharacterQuestsCommandHandler(
    ISelectCharacterQuestEntryService questEntry,
    ISelectCharacterSessionEntryService sessionEntry)
    : ICommandHandler<SynchronizeSelectedCharacterQuestsCommand, SynchronizedSelectedCharacterQuests>
{
    public async ValueTask<SynchronizedSelectedCharacterQuests> HandleAsync(
        CommandExecutionContext context,
        SynchronizeSelectedCharacterQuestsCommand command,
        CancellationToken cancellationToken)
    {
        var change = await questEntry.SynchronizeAsync(
            command.Session, command.State, cancellationToken).ConfigureAwait(false);
        if (change is not null)
            sessionEntry.ReplaceRuntimeExact(command.Session, change.Result);
        return new SynchronizedSelectedCharacterQuests(change);
    }
}

public sealed record ReviveSelectedCharacterPetCommand(
    SelectedSessionIdentity Session,
    SelectCharacterResult State)
    : ICommand<RevivedSelectedCharacterPet>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed record RevivedSelectedCharacterPet(SelectCharacterPetRevival? Change);

public sealed class ReviveSelectedCharacterPetCommandHandler(
    ISelectCharacterPetEntryService petEntry,
    ISelectCharacterSessionEntryService sessionEntry)
    : ICommandHandler<ReviveSelectedCharacterPetCommand, RevivedSelectedCharacterPet>
{
    public async ValueTask<RevivedSelectedCharacterPet> HandleAsync(
        CommandExecutionContext context,
        ReviveSelectedCharacterPetCommand command,
        CancellationToken cancellationToken)
    {
        var change = await petEntry.ReviveAsync(
            command.Session, command.State, cancellationToken).ConfigureAwait(false);
        if (change is not null)
            sessionEntry.ReplaceRuntimeExact(command.Session, change.Result);
        return new RevivedSelectedCharacterPet(change);
    }
}

public sealed record PlanSelectedCharacterRejoinCommand(
    SelectedSessionIdentity Session,
    SelectCharacterResult State)
    : ICommand<PlannedSelectedCharacterRejoin>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed record PlannedSelectedCharacterRejoin(
    DungeonRejoinSynchronizationState? State);

public sealed class PlanSelectedCharacterRejoinCommandHandler(
    ISelectCharacterSessionEntryService sessionEntry)
    : ICommandHandler<PlanSelectedCharacterRejoinCommand, PlannedSelectedCharacterRejoin>
{
    public ValueTask<PlannedSelectedCharacterRejoin> HandleAsync(
        CommandExecutionContext context,
        PlanSelectedCharacterRejoinCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new PlannedSelectedCharacterRejoin(
            sessionEntry.PlanRejoin(command.Session, command.State)));
}

public sealed record CompensateSelectedCharacterSessionCommand(
    SelectedSessionIdentity Session)
    : ICommand<CompensatedSelectedCharacterSession>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed record CompensatedSelectedCharacterSession(
    NetworkConnectionId ConnectionId,
    SelectedSessionCleanupNotificationPlan Notifications);

public sealed class CompensateSelectedCharacterSessionCommandHandler(
    ISelectedSessionCleanupService cleanup,
    ISelectedSessionDirectory selectedSessions)
    : ICommandHandler<CompensateSelectedCharacterSessionCommand, CompensatedSelectedCharacterSession>
{
    public async ValueTask<CompensatedSelectedCharacterSession> HandleAsync(
        CommandExecutionContext context,
        CompensateSelectedCharacterSessionCommand command,
        CancellationToken cancellationToken)
    {
        SelectedSessionCleanupNotificationPlan notifications;
        try
        {
            notifications = await cleanup.CleanupAsync(
                command.Session, includeOwner: true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _ = selectedSessions.TryRemoveExact(command.Session);
        }

        return new CompensatedSelectedCharacterSession(
            command.Session.ConnectionId, notifications);
    }
}

internal sealed class SelectCharacterPhaseFailure : Exception
{
    private SelectCharacterPhaseFailure(
        Exception failure,
        IReadOnlyList<NetworkConnectionId> connectionsToClose,
        IReadOnlyList<SelectedSessionCleanupNotificationPlan>? cleanupNotifications = null)
        : base("A SelectCharacter phase failed after acquiring lifecycle ownership.", failure)
    {
        Failure = ExceptionDispatchInfo.Capture(failure);
        ConnectionsToClose = connectionsToClose;
        CleanupNotifications = cleanupNotifications ?? [];
    }

    public ExceptionDispatchInfo Failure { get; }
    public IReadOnlyList<NetworkConnectionId> ConnectionsToClose { get; }
    public IReadOnlyList<SelectedSessionCleanupNotificationPlan> CleanupNotifications { get; }

    public static SelectCharacterPhaseFailure Create(
        Exception failure,
        params NetworkConnectionId[] connectionsToClose) =>
        new(failure, connectionsToClose);

    public static SelectCharacterPhaseFailure Create(
        Exception failure,
        IReadOnlyList<NetworkConnectionId> connectionsToClose) =>
        new(failure, connectionsToClose);

    public static SelectCharacterPhaseFailure Create(
        Exception failure,
        IReadOnlyList<NetworkConnectionId> connectionsToClose,
        IReadOnlyList<SelectedSessionCleanupNotificationPlan> cleanupNotifications) =>
        new(failure, connectionsToClose, cleanupNotifications);
}
