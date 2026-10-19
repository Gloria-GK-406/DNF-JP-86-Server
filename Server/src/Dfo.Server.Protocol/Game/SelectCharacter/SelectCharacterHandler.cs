using Dfo.Server.Abstractions.DomainEvents;
using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

/// <summary>
/// Whether the frames that follow the entry batch are put on the wire.
/// </summary>
/// <remarks>
/// The entry batch ends with NOTI 0x007C <c>ENTER_GAMEWORLD_COMPLETE</c>, an eight-byte signal
/// telling the client that world entry is over.
///
/// This flag was added when the A12 reference sequence was the only capture available, and that
/// sequence does stop at the marker. A21 does not: its entry sequence carries twelve more frames
/// after 0x007C, and four of them are the pre-publication frames built here -- NOTI 0x0165 user
/// visibility, CMD 0x0239 clone title, the NOTI 0x0002 subtype-0x02 character record, and NOTI
/// 0x0289 honor progress -- followed by the trailing equipment-lock snapshot and a second
/// mercenary answer. The earlier note recorded on this member, that the client dies while reading
/// that tail, does not survive the A21 capture: the client dies when the tail is *withheld*,
/// because the subtype-0x02 record is what it builds its own town avatar from.
///
/// On in production for A21, and the tests that assert the delivery contract keep asserting it.
/// The commands run either way -- they do the domain work, and publication in particular is what
/// makes the session readable to the dungeon-selection handler. Only the frames are withheld.
/// </remarks>
public sealed record SelectCharacterDeliveryOptions(bool SendsFramesAfterTheCompletionMarker)
{
    /// <summary>Matches the A12 reference sequence: nothing after the completion marker.</summary>
    /// <remarks>
    /// Retained because it describes a real captured sequence, not because it is the current
    /// production choice. A21 sends a post-marker tail; see the remarks on this type.
    /// </remarks>
    public static SelectCharacterDeliveryOptions ReferenceSequence { get; } = new(false);

    /// <summary>The post-marker tail on the wire, which is what A21 does.</summary>
    public static SelectCharacterDeliveryOptions Everything { get; } = new(true);
}

public sealed class SelectCharacterHandler(
    IOnlineUserQueryDomainService onlineUsers,
    IChannelEntryPolicy channels,
    ICommandBus commandBus,
    ISelectedCharacterStateMessageFactory stateMessages,
    SelectedSessionTransitionGate lifecycleGate,
    ISelectedSessionDomainService selectedSessions,
    IGameMessageSender sender,
    IEventService notifications,
    TownAreaProjectionDelivery townAreaProjection,
    ICleanupNotificationDelivery cleanupDelivery,
    INetworkConnectionCloser closer,
    ILogger<SelectCharacterHandler> logger,
    SelectCharacterDeliveryOptions delivery,
    Dfo.Server.Protocol.Game.Weakness.WeaknessProjectionDelivery? weaknessProjection = null,
    LotteryCounterExperimentOptions? lotteryCounterExperiment = null,
    ItemUsageProjectionDelivery? itemUsageProjection = null,
    Dfo.Server.Protocol.Game.TitleBook.AchievementTitleProjectionGate? achievementProjectionGate = null,
    UsageQuotaProjectionDelivery? quotaProjection = null,
    Dfo.Server.Abstractions.Accounts.IClientSettingsDomainService? clientSettings = null,
    Dfo.Server.Abstractions.World.ISaoTowerDomainService? sao = null,
    Dfo.Server.Abstractions.World.ITauKingdomDomainService? tau = null,
    Dfo.Server.Abstractions.World.ISouthernDaleDomainService? southern = null) : ClientMessageHandler<SelectCharacterRequest>
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
        var expiredItemCleanupRejected = false;
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
                await cleanupDelivery.DeliverAsync(notifications, cancellationToken).ConfigureAwait(false);
            }
            bool cleaned;
            try
            {
                cleaned = await commandBus.SendAsync(
                    new CleanupExpiredCharacterItemsCommand(session), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception error)
            {
                expiredItemCleanupRejected = true;
                throw new InvalidOperationException("Expired-item cleanup failed.", error);
            }
            if (!cleaned)
            {
                expiredItemCleanupRejected = true;
                throw new InvalidOperationException(
                    "Selected character is no longer owned by the account during expired-item cleanup.");
            }
            await commandBus.SendAsync(new EnsureDailyContractPuppetsCommand(session), cancellationToken)
                .ConfigureAwait(false);
            await commandBus.SendAsync(new Dfo.Server.Protocol.Game.MarriageHousing.InitializeHousingCommand(session), cancellationToken).ConfigureAwait(false);
            // Hold live achievement/title projections until the complete entry batch has reached the client.
            using var achievementProjection = achievementProjectionGate is null ? null
                : await achievementProjectionGate.EnterAsync(session.CharacterId, cancellationToken).ConfigureAwait(false);
            var state = await commandBus.SendAsync(
                new PrepareSelectedCharacterStateCommand(
                    session, prepared.Selected, prepared.Channel), cancellationToken)
                .ConfigureAwait(false);
            await ExecuteAcceptedWorkflowAsync(session, state, cancellationToken)
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
                    await cleanupDelivery.DeliverAsync(notifications, CancellationToken.None).ConfigureAwait(false);
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
            if (expiredItemCleanupRejected)
            {
                try
                {
                    await sender.SendExplicitAsync(
                        context.Connection.ConnectionId,
                        new GameExplicitMessageDelivery(
                            [new EntryAdmissionRejectedCommandMessage(0x02),
                                new EntryAdmissionNoticeMessage("Character entry failed. Please log in again.")],
                            []),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Entry failure and selected-session compensation remain primary.
                }
            }
            if (session is not null)
            {
                pendingCloses.Add(session.ConnectionId);
                try
                {
                    var compensated = await commandBus.SendAsync(
                        new CompensateSelectedCharacterSessionCommand(session),
                        CancellationToken.None).ConfigureAwait(false);
                    await cleanupDelivery.DeliverAsync(compensated.Notifications, CancellationToken.None).ConfigureAwait(false);
                }
                catch (SelectedSessionCleanupFailure cleanupFailure)
                {
                    try
                    {
                        await cleanupDelivery.DeliverAsync(cleanupFailure.NotificationPlan, CancellationToken.None).ConfigureAwait(false);
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
                if (connectionId == context.Connection.ConnectionId)
                    await closer.RequestCloseExactAsync(context.Connection, CancellationToken.None).ConfigureAwait(false);
                else
                    await closer.RequestCloseAsync(connectionId, CancellationToken.None).ConfigureAwait(false);
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
        ValidateSupplementalMessages(BuildPrePublicationMessages(session, prepared));
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

        // The refill runs, and its delta goes out, BEFORE the post-marker frames. The reference
        // session puts its one UPDATE_ITEM_LIST immediately after USER_POSITION -- the batch's own
        // last frame -- and only then sends the invisibility flags, the clone title, the roster
        // payload and the honour level. Running the command after them put our delta four frames
        // too late.
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
                    GameInventoryDelivery.Notifications(inventory.CommittedUpdates),
                    []),
                cancellationToken).ConfigureAwait(false);
        }

        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker)
        {
            // Built here, not before the refill: the refill replaces `prepared`, and a message set
            // composed from the older value would be stale by construction. None of these four
            // frames reads anything the refill touches today, so this is not a fix -- it is the
            // difference between that being guaranteed and it being a coincidence. They are also
            // validated once above, before anything is sent, so a malformed set still fails before
            // the first frame goes out rather than halfway through.
            await sender.SendExplicitAsync(
                session.ConnectionId,
                new GameExplicitMessageDelivery(
                    BuildPrePublicationMessages(session, prepared),
                    []),
                cancellationToken).ConfigureAwait(false);
        }
        EnsureCurrent(session);

        var publication = await commandBus.SendAsync(
            new PublishSelectedCharacterSessionCommand(session, prepared),
            cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        // Selection can straddle 06:00 while the scheduled snapshot sees it as unpublished.
        // Recheck the current period after publication using the same durable ticket claim.
        if (tau is not null)
            await commandBus.SendAsync(new Dfo.Server.Application.World.RefillOnlineTauTicketCommand(session), cancellationToken)
                .ConfigureAwait(false);
        if (southern is not null)
            await commandBus.SendAsync(new Dfo.Server.Application.World.RefillOnlineSouthernTicketCommand(session), cancellationToken)
                .ConfigureAwait(false);
        EnsureCurrent(session);
        if (clientSettings is not null)
        {
            if (!onlineUsers.TryGetByConnection(session.ConnectionId, out var current) || current is null)
                throw new InvalidOperationException("Selected hotkey profile has no current connection.");
            var origin = new Dfo.Server.Data.Accounts.ClientSettingsOrigin(current.SessionIdentity, session.AccountId, current.Generation, session);
            // Job 10 is Creator in the server career catalog (70/upstream reference); A21's
            // receiver selects its Creator default resource when 1C7.keyType is 1.
            var keyType = prepared.Character.Identity.Job == 10 ? (byte)1 : (byte)0;
            if (!await commandBus.SendAsync(new ActivateClientHotkeyProfileCommand(origin, keyType), cancellationToken))
                throw new InvalidOperationException("Selected hotkey profile identity changed during delivery.");
        }
        if (SendsFramesAfterTheCompletionMarker && publication.TownPresence is { } town)
        {
            await townAreaProjection.PublishCommittedEntryAsync(
                session, town, cancellationToken).ConfigureAwait(false);
        }
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
            // The command decided who; telling them is a separate step, and this is the shape
            // that pairing takes everywhere from here on. What this used to do instead was build
            // a delivery plan out of connections the domain service had looked up for it.
            if (pvpPublication.Publication.Audience.Count > 0)
            {
                await notifications.ProjectAsync(
                    new NotificationAudience.TheseCharacters(pvpPublication.Publication.Audience),
                    new PvpParticipantPublished(pvpPublication.Publication.Newcomer),
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
        if (SendsFramesAfterTheCompletionMarker && weaknessProjection is not null)
            await weaknessProjection.SendCurrentAsync(session, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker)
            await sender.SendExplicitAsync(session.ConnectionId,
                new GameExplicitMessageDelivery([new PremiumServiceStateMessage(prepared.Account.Entitlements,
                    prepared.CharacterEntry.Progression.PremiumUsageSlots, prepared.World.PreparedAt)], []),
                cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker && itemUsageProjection is not null)
            await itemUsageProjection.SendAsync(session, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker && quotaProjection is not null)
            _ = await quotaProjection.SendAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.DimensionDungeon,
                cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker && quotaProjection is not null)
            _ = await quotaProjection.SendAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.MirrorDungeon,
                cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker && quotaProjection is not null)
            _ = await quotaProjection.SendAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.DespairTower,
                cancellationToken).ConfigureAwait(false);
        if (SendsFramesAfterTheCompletionMarker && quotaProjection is not null)
            _ = await quotaProjection.SendAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.AltarDungeon,
                cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker && quotaProjection is not null)
            _ = await quotaProjection.SendAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.TournamentDungeon,
                cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker && quotaProjection is not null)
            _ = await quotaProjection.SendAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.HerasDungeon, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker && quotaProjection is not null)
            _ = await quotaProjection.SendAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.GoldSilverHorn, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker && sao is not null
            && await sao.ReadStatusAsync(cancellationToken).ConfigureAwait(false) is { } saoProgress)
            await sender.SendExplicitAsync(session.ConnectionId,
                new GameExplicitMessageDelivery([new SaoCurrentFloorNotification(saoProgress)], []), cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker && lotteryCounterExperiment is { Enabled: true } experiment)
        {
            await sender.SendExplicitAsync(session.ConnectionId,
                new GameExplicitMessageDelivery([new LotteryCounterStateMessage(experiment.Current, experiment.Threshold)], []),
                cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Lottery counter experiment character={CharacterId}: current={Current} threshold={Threshold}",
                session.CharacterId, experiment.Current, experiment.Threshold);
        }


    }

    private static IReadOnlyList<SupplementalSynchronizationMessage> BuildPrePublicationMessages(
            SelectedSessionIdentity session,
            SelectCharacterResult result)
    {
        var messages = new List<SupplementalSynchronizationMessage>
        {
            new UserVisibilityMessage(
                session.WireUserId,
                result.CharacterEntry.Public.Display?.VisibilityBits ?? checked((byte)result.CharacterEntry.Public.VisibilityBits)),
            new CloneTitleStateMessage(result.CharacterEntry.Public.Display?.TitleAppearanceItemId ?? result.CharacterEntry.Public.CloneTitleItemId),
            new AccountRosterStateMessage(result.RosterEntry
                ?? throw new InvalidDataException("Roster state is unavailable.")),
            // The same account-wide standing the roster screen already received, not a character
            // stat. The reference session sends 0x0289 twice with an identical body -- 01 00 00 00
            // 00 00 00 00 at the roster and again here -- while this read two per-character derived
            // stats, which are zero on a fresh character and disagreed with what login had just
            // sent for the same account.
            new HonorProgressStateMessage(
                AccountHonorLevel.Initial.Level,
                AccountHonorLevel.Initial.Experience),
        };
        // NOTI 0x0025 GROWTH CAPSULE is not part of the A21 entry sequence. The reference sends
        // 0x0165, CMD 0x0239, the subtype-0x02 record and 0x0289 here and nothing else, and the
        // client overruns a stack buffer while reading this body -- a 66-thousand-element copy off
        // the end of its own stack -- so its A21 layout is not the one this encoder writes. The
        // encoder and its mirror stay; only the unsolicited entry-time send is withdrawn until the
        // layout is derived from a capture that actually contains it.
        _ = result.CharacterEntry.Skills.ProtocolMirror
            ?? throw new InvalidDataException("Skill protocol mirror is unavailable.");
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
