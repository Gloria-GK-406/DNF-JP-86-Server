using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.OnlineUsers;
using System.Runtime.ExceptionServices;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

/// <summary>
/// Enter the game with one of an account's characters.
/// </summary>
/// <param name="Channel">
/// The channel the connection arrived on. It is resolved before dispatch because entry
/// policy — admission, spawn, position persistence and duelling applicability — all follow
/// from it, and a command that had to ask which listener it came from would be reaching
/// back into the transport.
/// </param>
public sealed record SelectCharacterCommand(
    AccountId AccountId,
    ushort SlotIndex,
    ChannelEntryIdentity Channel,
    NetworkConnectionId ConnectionId = default,
    long ExpectedConnectionGeneration = 0) : ICommand<SelectCharacterResult>, IAccountCommand;

public sealed class SelectCharacterCommandHandler(
    IOnlineUserQueryService onlineUsers,
    ISelectCharacterPreparationService preparation,
    ISelectedSessionDirectory selectedSessions,
    ISelectedSessionCleanupService cleanup,
    ISelectCharacterEntryInventoryService inventoryEntry,
    ISelectCharacterSessionEntryService sessionEntry,
    ISelectCharacterQuestEntryService questEntry,
    ISelectCharacterPetEntryService petEntry,
    IActiveCharacterService activeCharacters,
    ICharacterEntryMutationRepository characterMutations,
    SelectedSessionTransitionGate lifecycleGate,
    INetworkMessageSender sender,
    INetworkConnectionCloser closer)
    : ICommandHandler<SelectCharacterCommand, SelectCharacterResult>
{
    public async ValueTask<SelectCharacterResult> HandleAsync(
        CommandExecutionContext context, SelectCharacterCommand command,
        CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(command.ConnectionId, out var online)
            || online?.AccountId != command.AccountId
            || online.Generation != command.ExpectedConnectionGeneration)
            throw new InvalidOperationException("Select-character connection generation is stale.");

        var pendingCloses = new List<NetworkConnectionId>();
        var lifecycleLease = await lifecycleGate.AcquireAsync(
            command.AccountId, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);
        SelectCharacterResult? completed = null;
        ExceptionDispatchInfo? failure = null;
        try
        {
            completed = await ExecuteUnderLifecycleGateAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failure = ExceptionDispatchInfo.Capture(error);
        }
        finally
        {
            await lifecycleLease.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var connectionId in pendingCloses.Distinct())
        {
            try
            {
                await closer.CloseAsync(connectionId, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Transport close is best effort after lifecycle ownership is released.
                // It must neither mask the workflow failure nor invalidate a new success.
            }
        }
        failure?.Throw();
        return completed ?? throw new InvalidOperationException("Select-character workflow produced no result.");

        async ValueTask<SelectCharacterResult> ExecuteUnderLifecycleGateAsync()
        {
        if (!onlineUsers.TryGetByConnection(command.ConnectionId, out online)
            || online?.AccountId != command.AccountId
            || online.Generation != command.ExpectedConnectionGeneration)
            throw new InvalidOperationException("Select-character connection generation is stale.");

        ActiveCharacterState selected;
        try
        {
            selected = await preparation.PreflightAsync(
                command.AccountId, command.SlotIndex, command.Channel, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ChannelAdmissionRefusedException refusal)
        {
            await sender.SendProjectionAsync(command.ConnectionId,
                new SelectCharacterProjectionDelivery(
                    [new SelectCharacterAdmissionRejectionProjection(
                        refusal.Refusal.ErrorCode, refusal.Refusal.Message)], []), cancellationToken)
                .ConfigureAwait(false);
            throw;
        }

        // Wire representability is a preflight and therefore precedes every lifecycle side effect.
        _ = SelectedSessionIdentity.ToWireUserId(selected.Identity.CharacterId);
        var previous = selectedSessions.Find(command.AccountId);
        if (previous is not null)
            await inventoryEntry.SaveExistingLeaseAsync(previous, cancellationToken).ConfigureAwait(false);
        if (previous?.ConnectionId == command.ConnectionId)
        {
            try
            {
                await cleanup.CleanupAsync(previous, includeOwner: true, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _ = selectedSessions.TryRemoveExact(previous);
                pendingCloses.Add(command.ConnectionId);
                throw;
            }
        }
        if (!onlineUsers.TryGetByConnection(command.ConnectionId, out var refreshed)
            || refreshed?.AccountId != command.AccountId
            || (previous?.ConnectionId != command.ConnectionId
                && refreshed.Generation != command.ExpectedConnectionGeneration))
            throw new InvalidOperationException("Select-character connection changed during cleanup.");
        var session = SelectedSessionIdentity.Create(
            command.AccountId, selected.Identity.CharacterId, command.ConnectionId,
            refreshed.Generation, command.Channel.ListenerPort);

        try
        {
            var displaced = selectedSessions.RegisterReplacing(session);
            if (displaced is not null)
            {
                try
                {
                    await cleanup.CleanupAsync(displaced, includeOwner: false, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    _ = selectedSessions.TryRemoveExact(displaced);
                    pendingCloses.Add(displaced.ConnectionId);
                }
            }
            var prepared = await preparation.PrepareAsync(
                selected, command.AccountId, command.Channel, cancellationToken).ConfigureAwait(false);
            EnsureCurrent(session);
            var spawn = prepared.World.Channel.Spawn;
            var resolvedPosition = new CharacterPositionState(
                spawn.TownId, spawn.AreaId, spawn.X, spawn.Y, spawn.Direction);
            prepared = prepared with { Character = prepared.Character with { Position = resolvedPosition } };
            prepared = await inventoryEntry.PrepareLeaseAsync(session, prepared, cancellationToken)
                .ConfigureAwait(false);
            if (prepared.World.Channel.PersistsPosition)
                await characterMutations.PersistPositionAsync(
                    command.AccountId, session.CharacterId, resolvedPosition,
                    cancellationToken).ConfigureAwait(false);

            // The complete G6 static stream is the first required owner delivery.
            EnsureCurrent(session);
            await sender.SendAsync(command.ConnectionId, new SelectCharacterResponse(prepared), cancellationToken)
                .ConfigureAwait(false);
            EnsureCurrent(session);
            var supplemental = BuildPrePublicationProjections(session, prepared);
            await sender.SendProjectionAsync(command.ConnectionId,
                new SelectCharacterProjectionDelivery(supplemental, []), cancellationToken).ConfigureAwait(false);
            EnsureCurrent(session);

            var inventory = await inventoryEntry.ApplyDailyRefillAsync(session, prepared, cancellationToken)
                .ConfigureAwait(false);
            EnsureCurrent(session);
            prepared = inventory.Result;
            if (inventory.CommittedUpdates.Count > 0)
                await sender.SendProjectionAsync(command.ConnectionId,
                    new SelectCharacterProjectionDelivery(inventory.CommittedUpdates, []), cancellationToken)
                    .ConfigureAwait(false);
            EnsureCurrent(session);

            activeCharacters.Publish(session, prepared.Character);
            sessionEntry.Publish(session, prepared);
            EnsureCurrent(session);

            var pvp = sessionEntry.PlanPvp(session, prepared);
            if (pvp is not null)
            {
                await sender.SendProjectionAsync(command.ConnectionId,
                    new SelectCharacterProjectionDelivery([pvp], []), cancellationToken).ConfigureAwait(false);
                EnsureCurrent(session);
                var targets = sessionEntry.PlanPvpPublication(session, prepared);
                sessionEntry.MarkPvpReady(session, prepared);
                if (targets.Count > 0)
                    await sender.SendProjectionAsync(command.ConnectionId,
                        new SelectCharacterProjectionDelivery([], targets), cancellationToken).ConfigureAwait(false);
            }

            await sender.SendProjectionAsync(command.ConnectionId,
                new SelectCharacterProjectionDelivery(
                    [new SelectCharacterTrailingEquipmentLocksProjection(prepared)], []), cancellationToken)
                .ConfigureAwait(false);
            EnsureCurrent(session);
            if (await questEntry.SynchronizeAsync(session, prepared, cancellationToken).ConfigureAwait(false) is { } quests)
            {
                EnsureCurrent(session);
                prepared = quests.Result;
                sessionEntry.ReplaceRuntimeExact(session, prepared);
                await sender.SendProjectionAsync(command.ConnectionId,
                    new SelectCharacterProjectionDelivery([quests.Projection], []), cancellationToken).ConfigureAwait(false);
            }
            EnsureCurrent(session);
            if (await petEntry.ReviveAsync(session, prepared, cancellationToken).ConfigureAwait(false) is { } pet)
            {
                EnsureCurrent(session);
                prepared = pet.Result;
                sessionEntry.ReplaceRuntimeExact(session, prepared);
                await sender.SendProjectionAsync(command.ConnectionId,
                    new SelectCharacterProjectionDelivery([pet.Projection], []), cancellationToken).ConfigureAwait(false);
            }
            EnsureCurrent(session);
            if (sessionEntry.PlanRejoin(session, prepared) is { } rejoin)
                await sender.SendProjectionAsync(command.ConnectionId,
                    new SelectCharacterProjectionDelivery([rejoin], []), cancellationToken).ConfigureAwait(false);
            return prepared;
        }
        catch
        {
            try { await cleanup.CleanupAsync(session, includeOwner: true, CancellationToken.None); }
            finally
            {
                _ = selectedSessions.TryRemoveExact(session);
                pendingCloses.Add(session.ConnectionId);
            }
            throw;
        }
        }
    }

    private static IReadOnlyList<ISelectCharacterSupplementalProjection> BuildPrePublicationProjections(
        SelectedSessionIdentity session, SelectCharacterResult result)
    {
        var projections = new List<ISelectCharacterSupplementalProjection>
        {
            new SelectCharacterVisibilityProjection(session.WireUserId,
                checked((byte)result.CharacterEntry.Public.VisibilityBits)),
            new SelectCharacterCloneTitleProjection(result.CharacterEntry.Public.CloneTitleItemId),
            result.RosterProjection ?? throw new InvalidDataException("Roster projection is unavailable."),
            new SelectCharacterHonorProjection(
                checked((uint)Math.Max(0, result.CharacterEntry.Public.Subtype1.Progress1)),
                checked((uint)Math.Max(0, result.CharacterEntry.Public.Subtype1.Progress2))),
        };
        var mirror = result.CharacterEntry.Skills.ProtocolMirror
            ?? throw new InvalidDataException("Skill protocol mirror is unavailable.");
        projections.Add(new SelectCharacterGrowthCapsuleProjection(
            result.Character.Progression.Level, result.Character.Progression.Experience,
            mirror.Page0Sp, mirror.Page1Sp, mirror.Page0Tp, mirror.Page1Tp,
            result.CharacterEntry.Public.Subtype0.PvpRankPoint,
            result.Account.Progression.GrowthCapsuleExperience,
            checked((uint)Math.Max(0, result.CharacterEntry.Public.Subtype1.Progress1)),
            checked((uint)Math.Max(0, result.CharacterEntry.Public.Subtype1.Progress2))));
        return projections;
    }

    private void EnsureCurrent(SelectedSessionIdentity session)
    {
        if (selectedSessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var current)
            || current?.AccountId != session.AccountId)
            throw new InvalidOperationException("Selected session is no longer current.");
    }
}
