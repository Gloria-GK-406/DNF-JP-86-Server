using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

/// <summary>
/// Whether the frames that follow the entry batch are put on the wire.
/// </summary>
/// <remarks>
/// The entry batch ends with NOTI 0x007C <c>ENTER_GAMEWORLD_COMPLETE</c>, a zero-length signal
/// telling the client that world entry is over. The reference server's entry sequence stops there
/// -- 53 templates, nothing after. This server kept going: pre-publication state, inventory deltas,
/// a trailing equipment-lock snapshot, quest and pet frames, and an account character
/// roster, all delivered after the completion signal. The client dies while reading that tail.
///
/// Off in production while that is under test, on in the tests that assert the delivery contract,
/// so suppressing the frames does not quietly delete the coverage that describes them. The commands
/// run either way -- they do the domain work, and publication in particular is what makes the
/// session readable to the dungeon-selection handler. Only the frames are withheld.
/// </remarks>
public sealed record SelectCharacterDeliveryOptions(bool SendsFramesAfterTheCompletionMarker)
{
    /// <summary>Matches the reference server: nothing after the completion marker.</summary>
    public static SelectCharacterDeliveryOptions ReferenceSequence { get; } = new(false);

    /// <summary>Everything this server historically sent, marker or not.</summary>
    public static SelectCharacterDeliveryOptions Everything { get; } = new(true);
}

public sealed class SelectCharacterHandler(
    IOnlineUserQueryDomainService onlineUsers,
    IChannelEntryPolicy channels,
    ICommandBus commandBus,
    ISelectedCharacterStateMessageFactory stateMessages,
    SelectedSessionTransitionGate lifecycleGate,
    ISelectedSessionDirectory selectedSessions,
    IGameMessageSender sender,
    INetworkConnectionCloser closer,
    ILogger<SelectCharacterHandler> logger,
    SelectCharacterDeliveryOptions delivery) : ClientMessageHandler<SelectCharacterRequest>
{
    private bool SendsFramesAfterTheCompletionMarker =>
        delivery.SendsFramesAfterTheCompletionMarker;

    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context,
        SelectCharacterRequest message,
        CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            || onlineUser?.AccountId is not { } accountId)
        {
            throw new InvalidOperationException(
                $"Game connection '{context.Connection.ConnectionId}' is not authenticated.");
        }

        var channel = await channels
            .ResolveAsync(context.Connection.ListenerPort, cancellationToken)
            .ConfigureAwait(false);
        var pendingCloses = new List<NetworkConnectionId>();
        SelectedSessionIdentity? session = null;
        ExceptionDispatchInfo? failure = null;
        var lifecycleLease = await lifecycleGate.AcquireAsync(
            accountId, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);
        try
        {
            var prepared = await commandBus.SendAsync(
                new PrepareSelectedCharacterSessionCommand(
                    accountId,
                    message.RosterIndex,
                    channel,
                    context.Connection.ConnectionId,
                    onlineUser.Generation),
                cancellationToken).ConfigureAwait(false);
            session = prepared.Session;
            pendingCloses.AddRange(prepared.ConnectionsToClose);
            foreach (var notifications in prepared.CleanupNotifications)
            {
                await sender.SendExplicitAsync(
                    notifications.OwnerConnectionId,
                    CleanupSynchronizationDelivery.Create(notifications),
                    cancellationToken).ConfigureAwait(false);
            }
            await ExecuteAcceptedWorkflowAsync(session, prepared.State, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ChannelAdmissionRefusedException refusal)
        {
            try
            {
                await sender.SendExplicitAsync(
                    context.Connection.ConnectionId,
                    new GameExplicitMessageDelivery(
                        [
                            new EntryAdmissionRejectedCommandMessage(refusal.Refusal.ErrorCode),
                            new EntryAdmissionNoticeMessage(refusal.Refusal.Message),
                        ],
                        []),
                    cancellationToken).ConfigureAwait(false);
                failure = ExceptionDispatchInfo.Capture(refusal);
            }
            catch (Exception deliveryFailure)
            {
                failure = ExceptionDispatchInfo.Capture(deliveryFailure);
            }
        }
        catch (SelectCharacterPhaseFailure phaseFailure)
        {
            pendingCloses.AddRange(phaseFailure.ConnectionsToClose);
            foreach (var notifications in phaseFailure.CleanupNotifications)
            {
                try
                {
                    await sender.SendExplicitAsync(
                        notifications.OwnerConnectionId,
                        CleanupSynchronizationDelivery.Create(notifications),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Preserve the phase failure that required compensation.
                }
            }
            failure = phaseFailure.Failure;
        }
        catch (Exception error)
        {
            failure = ExceptionDispatchInfo.Capture(error);
            if (session is not null)
            {
                pendingCloses.Add(session.ConnectionId);
                try
                {
                    var compensated = await commandBus.SendAsync(
                        new CompensateSelectedCharacterSessionCommand(session),
                        CancellationToken.None).ConfigureAwait(false);
                    await sender.SendExplicitAsync(
                        compensated.Notifications.OwnerConnectionId,
                        CleanupSynchronizationDelivery.Create(compensated.Notifications),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (SelectedSessionCleanupFailure cleanupFailure)
                {
                    try
                    {
                        await sender.SendExplicitAsync(
                            cleanupFailure.NotificationPlan.OwnerConnectionId,
                            CleanupSynchronizationDelivery.Create(
                                cleanupFailure.NotificationPlan),
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // The original workflow failure remains primary.
                    }
                }
                catch
                {
                    // The workflow failure remains primary. The compensation handler guarantees
                    // exact directory removal in its finally block.
                }
            }
        }
        finally
        {
            await lifecycleLease.DisposeAsync().ConfigureAwait(false);
        }

        // Logged where it is known to have happened. Every phase of this handler that can fail
        // funnels into `failure`, and the command pipeline only reports failures of commands --
        // a phase that throws after its commands succeeded left no trace at all, which is how six
        // runs in a row produced a bare "connection closed" and an empty log.
        if (failure is not null)
        {
            logger.LogError(
                failure.SourceException,
                "Select character failed {ConnectionId} account={AccountId} rosterIndex={RosterIndex} "
                    + "session={Session}",
                context.Connection.ConnectionId,
                accountId,
                message.RosterIndex,
                session?.ConnectionId.ToString() ?? "none");
        }

        foreach (var connectionId in pendingCloses.Distinct())
        {
            try
            {
                await closer.CloseAsync(connectionId, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Best effort after lifecycle ownership is released. A close callback may itself
                // wait on the same transition gate, so it must never run inside the lease.
            }
        }

        failure?.Throw();
    }

    private async ValueTask ExecuteAcceptedWorkflowAsync(
        SelectedSessionIdentity session,
        SelectCharacterResult prepared,
        CancellationToken cancellationToken)
    {
        var messages = stateMessages.CreateValidated(prepared);
        var prePublicationMessages = BuildPrePublicationMessages(session, prepared);
        ValidateSupplementalMessages(prePublicationMessages);
        ValidateSupplementalMessages(
            [new TrailingEquipmentLockSnapshotMessage(
                prepared.CharacterInventory, prepared.AccountInventory)]);

        foreach (var message in messages)
        {
            EnsureCurrent(session);
            await SendClassifiedAsync(session.ConnectionId, message, cancellationToken)
                .ConfigureAwait(false);
        }

        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker)
        {
            await sender.SendExplicitAsync(
                session.ConnectionId,
                new GameExplicitMessageDelivery(
                    prePublicationMessages,
                    []),
                cancellationToken).ConfigureAwait(false);
        }

        EnsureCurrent(session);

        var inventory = await commandBus.SendAsync(
            new ApplySelectedCharacterDailyRefillCommand(session, prepared),
            cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        prepared = inventory.Result;
        if (SendsFramesAfterTheCompletionMarker && inventory.CommittedUpdates.Count > 0)
        {
            await sender.SendExplicitAsync(
                session.ConnectionId,
                new GameExplicitMessageDelivery(
                    inventory.CommittedUpdates.Select(value => (IGameServerMessage)
                        new InventoryDeltaMessage(
                            value.ContainerType, value.Items, value.PreparedAt)).ToArray(),
                    []),
                cancellationToken).ConfigureAwait(false);
        }
        EnsureCurrent(session);

        var publication = await commandBus.SendAsync(
            new PublishSelectedCharacterSessionCommand(session, prepared),
            cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker && publication.PvpLobby is { } pvp)
        {
            await sender.SendExplicitAsync(
                session.ConnectionId,
                new GameExplicitMessageDelivery([new PvpLobbySnapshotMessage(pvp)], []),
                cancellationToken).ConfigureAwait(false);
            EnsureCurrent(session);
            var pvpPublication = await commandBus.SendAsync(
                new MarkSelectedCharacterPvpReadyCommand(session, prepared),
                cancellationToken).ConfigureAwait(false);
            EnsureCurrent(session);
            if (pvpPublication.Targets.Count > 0)
            {
                await sender.SendExplicitAsync(
                    session.ConnectionId,
                    new GameExplicitMessageDelivery([], pvpPublication.Targets.Select(target =>
                        new GameExplicitMessageTarget(
                            target.ConnectionId,
                            [(IGameServerMessage)new PvpParticipantPublicationMessage(
                                target.Participant)]))
                        .ToArray()),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (SendsFramesAfterTheCompletionMarker)
        {
            await sender.SendExplicitAsync(
                session.ConnectionId,
                new GameExplicitMessageDelivery(
                    [new TrailingEquipmentLockSnapshotMessage(
                        prepared.CharacterInventory, prepared.AccountInventory)],
                    []),
                cancellationToken).ConfigureAwait(false);
        }

        EnsureCurrent(session);

        var quests = await commandBus.SendAsync(
            new SynchronizeSelectedCharacterQuestsCommand(session, prepared),
            cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (quests.Change is { } questChange)
        {
            prepared = questChange.Result;
            if (SendsFramesAfterTheCompletionMarker)
                await sender.SendExplicitAsync(
                session.ConnectionId,
                new GameExplicitMessageDelivery(
                    [new AcceptedQuestProgressMessage(questChange.State.Quests)], []),
                cancellationToken).ConfigureAwait(false);
        }
        EnsureCurrent(session);

        var pet = await commandBus.SendAsync(
            new ReviveSelectedCharacterPetCommand(session, prepared),
            cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (pet.Change is { } petChange)
        {
            prepared = petChange.Result;
            if (SendsFramesAfterTheCompletionMarker)
                await sender.SendExplicitAsync(
                session.ConnectionId,
                new GameExplicitMessageDelivery(
                    [new PetRevivalStateMessage(petChange.State)], []),
                cancellationToken).ConfigureAwait(false);
        }
        EnsureCurrent(session);
    }

    private static IReadOnlyList<SupplementalSynchronizationMessage> BuildPrePublicationMessages(
            SelectedSessionIdentity session,
            SelectCharacterResult result)
    {
        var messages = new List<SupplementalSynchronizationMessage>
        {
            new UserVisibilityMessage(
                session.WireUserId,
                checked((byte)result.CharacterEntry.Public.VisibilityBits)),
            new CloneTitleStateMessage(result.CharacterEntry.Public.CloneTitleItemId),
            new AccountRosterStateMessage(result.RosterEntry
                ?? throw new InvalidDataException("Roster state is unavailable.")),
            new HonorProgressStateMessage(
                checked((uint)Math.Max(0, result.CharacterEntry.Public.DerivedStats.Progress1)),
                checked((uint)Math.Max(0, result.CharacterEntry.Public.DerivedStats.Progress2))),
        };
        var mirror = result.CharacterEntry.Skills.ProtocolMirror
            ?? throw new InvalidDataException("Skill protocol mirror is unavailable.");
        messages.Add(new GrowthCapsuleStateMessage(
            result.Character.Progression.Level,
            result.Character.Progression.Experience,
            mirror.Page0Sp,
            mirror.Page1Sp,
            mirror.Page0Tp,
            mirror.Page1Tp,
            checked((uint)result.CharacterEntry.Public.Pvp.RankPoint),
            result.Account.Progression.GrowthCapsuleExperience,
            checked((uint)Math.Max(0, result.CharacterEntry.Public.DerivedStats.Progress1)),
            checked((uint)Math.Max(0, result.CharacterEntry.Public.DerivedStats.Progress2))));
        return messages;
    }

    private static void ValidateSupplementalMessages(
        IEnumerable<SupplementalSynchronizationMessage> messages)
    {
        foreach (var message in messages)
            _ = message.EncodePayloads();
    }

    private ValueTask SendClassifiedAsync(
        NetworkConnectionId connectionId,
        IGameServerMessage message,
        CancellationToken cancellationToken) =>
        message switch
        {
            IGameCmdMessage command =>
                sender.SendCmdAsync(connectionId, command, cancellationToken),
            IGameNotiMessage notification =>
                sender.SendNotiAsync(connectionId, notification, cancellationToken),
            _ => throw new InvalidDataException(
                $"Game message '{message.GetType().Name}' has no CMD/NOTI classification."),
        };

    private void EnsureCurrent(SelectedSessionIdentity session)
    {
        if (selectedSessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var current)
            || current?.AccountId != session.AccountId)
        {
            throw new InvalidOperationException("Selected session is no longer current.");
        }
    }
}
