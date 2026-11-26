using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Execution;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Parties;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.DomainEvents;
using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.AntonRaid;
using Dfo.Server.Protocol.Game.GuildInfo;
using Dfo.Server.Protocol.Game.GuildPresence;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.PvpDailyMission;
using Dfo.Server.Protocol.Game.PvpTotalMatchTeam;
using Dfo.Server.Protocol.Game.TownProjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SelectCharacter;

public sealed record GameSelectCharacterCommand(NetworkDispatchContext Dispatch, SelectCharacterRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal sealed partial class GameSelectCharacterCommandHandler : ICommandHandler<GameSelectCharacterCommand, bool>
{
    private readonly ITownAreaDomainService townAreas;
    private readonly ICommandProjections projections;
    private readonly IPartyProjectionStateDomainService projectionState;
    private readonly ILogger logger;
    private readonly TimeProvider clock;
    private readonly IPartyService parties;

    private readonly IOnlineUserQueryDomainService users;
    private readonly IChannelEntryDomainService channels;
    private readonly ISelectedCharacterStateMessageFactory stateMessages;
    private readonly ISelectedSessionDomainService selectedSessions;
    private readonly IGameCommandOutbox outbox;
    private readonly ICommandConnectionClosures closures;
    private readonly ICommandSelectedSessionCleanup cleanup;
    private readonly ICommandWorkUnits units;
    private readonly ISelectCharacterPreparationDomainService preparation;
    private readonly ISelectCharacterEntryInventoryDomainService inventoryEntry;
    private readonly ISelectCharacterSessionEntryDomainService sessionEntry;
    private readonly ISelectCharacterQuestEntryDomainService questEntry;
    private readonly ISelectCharacterPetEntryDomainService petEntry;
    private readonly IExpiredCharacterItemCleanupDomainService expiredItems;
    private readonly IQuestHelperPuppetDomainService puppets;
    private readonly IMarriageHousingDomainService housingEntry;
    private readonly SelectCharacterDeliveryOptions delivery;
    private readonly LotteryCounterExperimentOptions lotteryCounterExperiment;
    private readonly IWeaknessDomainService weaknessState;
    private readonly IItemUsageDomainService itemUsage;
    private readonly IClientSettingsDomainService clientSettings;
    private readonly ISaoTowerDomainService sao;
    private readonly OnlineTicketRefillRegistration[] ticketRefills;
    private readonly IOutboundOrdering ordering;
    private readonly IDarkElfRuinsDomainService ruins;
    private readonly ICharacterRelationQueryDomainService relations;
    private readonly ICharacterIdentityQueryDomainService identities;
    private readonly IGuildQueryDomainService guilds;
    private readonly IGuildProgressDomainService guildProgress;
    private readonly AntonRaidSquadProjection raidSquads;
    private readonly IPvpRecordDomainService pvpRecords;
    private readonly IPvpTotalMatchTeamDomainService pvpTotalMatchTeams;
    private readonly IPvpTotalMatchRepresentativeSwitchDomainService representativeSwitch;
    private readonly PvpTotalMatchCandidateProjection totalMatchCandidates;
    private readonly PvpDailyMissionProjection dailyMissions;
    public GameSelectCharacterCommandHandler(IOnlineUserQueryDomainService users, IChannelEntryDomainService channels,
        ISelectedCharacterStateMessageFactory stateMessages, ISelectedSessionDomainService selectedSessions,
        IGameCommandOutbox outbox, ICommandProjections projections, ICommandConnectionClosures closures,
        ICommandSelectedSessionCleanup cleanup, ICommandWorkUnits units, ISelectCharacterPreparationDomainService preparation,
        ISelectCharacterEntryInventoryDomainService inventoryEntry, ISelectCharacterSessionEntryDomainService sessionEntry,
        ISelectCharacterQuestEntryDomainService questEntry, ISelectCharacterPetEntryDomainService petEntry,
        IExpiredCharacterItemCleanupDomainService expiredItems, IQuestHelperPuppetDomainService puppets,
        IMarriageHousingDomainService housingEntry, IPartyService parties, IPartyProjectionStateDomainService projectionState,
        ITownAreaDomainService townAreas, ILogger<GameSelectCharacterCommandHandler> logger,
        SelectCharacterDeliveryOptions delivery, TimeProvider clock, LotteryCounterExperimentOptions lotteryCounterExperiment,
        IWeaknessDomainService weaknessState, IItemUsageDomainService itemUsage,
        IClientSettingsDomainService clientSettings,
        ISaoTowerDomainService sao, IEnumerable<OnlineTicketRefillRegistration> ticketRefills,
        IOutboundOrdering ordering, IDarkElfRuinsDomainService ruins,
        ICharacterRelationQueryDomainService relations, ICharacterIdentityQueryDomainService identities,
        AntonRaidSquadProjection raidSquads,
        IPvpRecordDomainService pvpRecords, IPvpTotalMatchTeamDomainService pvpTotalMatchTeams,
        IPvpTotalMatchRepresentativeSwitchDomainService representativeSwitch, IPvpRoomQueryDomainService pvpRooms,
        IPvpTotalMatchCandidateQueryDomainService totalMatchCandidates, IPvpDailyMissionQueryDomainService dailyMissions,
        IPvpColosseumDomainService colosseum,
        IGuildQueryDomainService guilds, IGuildProgressDomainService guildProgress)

    {
        this.townAreas = townAreas; this.projections = projections; this.projectionState = projectionState; this.logger = logger; this.clock = clock; this.parties = parties;
        this.users=users; this.channels=channels; this.stateMessages=stateMessages; this.selectedSessions=selectedSessions;
        this.outbox=outbox; this.closures=closures; this.cleanup=cleanup; this.units=units; this.preparation=preparation;
        this.inventoryEntry=inventoryEntry; this.sessionEntry=sessionEntry; this.questEntry=questEntry; this.petEntry=petEntry;
        this.expiredItems=expiredItems; this.puppets=puppets; this.housingEntry=housingEntry; this.delivery=delivery;
        this.lotteryCounterExperiment=lotteryCounterExperiment; this.weaknessState=weaknessState; this.itemUsage=itemUsage;
        this.clientSettings=clientSettings; this.sao=sao; this.ordering=ordering;
        this.ticketRefills = ticketRefills.OrderBy(refill => refill.SelectionOrder).ToArray();
        if (this.ticketRefills.Select(refill => refill.SelectionOrder).Distinct().Count() != this.ticketRefills.Length)
            throw new InvalidOperationException("Selected-character ticket refill orders must be unique.");
        this.ruins = ruins;
        this.relations = relations; this.identities = identities;
        this.raidSquads = raidSquads;
        this.pvpRecords = pvpRecords;
        this.pvpTotalMatchTeams = pvpTotalMatchTeams;
        this.representativeSwitch = representativeSwitch;
        this.totalMatchCandidates = new(pvpRooms, totalMatchCandidates, outbox, clock);
        this.dailyMissions = new(dailyMissions, outbox, colosseum);
        this.guildProgress = guildProgress;
        this.guilds = guilds;
    }
    private bool SendsFramesAfterTheCompletionMarker =>
        delivery.SendsFramesAfterTheCompletionMarker;

    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext execution, GameSelectCharacterCommand command,
        CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        if (!users.TryGetByConnection(context.Connection.ConnectionId, out var onlineUser)
            || onlineUser?.AccountId is not { } accountId)
        {
            throw new InvalidOperationException(
                $"Game connection '{context.Connection.ConnectionId}' is not authenticated.");
        }

        var channel = await channels
            .ResolveAsync(context.Connection.ListenerPort, cancellationToken)
            .ConfigureAwait(false);
        // RETURN_SELECT_CHARACTER already sent the roster that started this automatic selection.
        // While the A21 representative index is still pending, another roster restarts selection.
        var refreshRoster = !(representativeSwitch.Consume(context.Connection, accountId, message.RosterIndex)
            && channel.IsPvp && selectedSessions.Find(accountId) is null);
        var pendingCloses = new List<NetworkConnectionId>();
        SelectedSessionIdentity? session = null;
        ExceptionDispatchInfo? failure = null;
        var expiredItemCleanupRejected = false;
        try
        {
            var prepared = await PrepareSessionAsync(execution, accountId, message.RosterIndex, channel,
                context.Connection.ConnectionId, onlineUser.Generation, cancellationToken).ConfigureAwait(false);
            session = prepared.Session;
            pendingCloses.AddRange(prepared.ConnectionsToClose);
            foreach (var notifications in prepared.CleanupNotifications)
            {
                await DeliverCleanupAsync(notifications, cancellationToken).ConfigureAwait(false);
            }
            Exception? cleanupActionFailure = null;
            bool cleaned;
            try
            {
                cleaned = await CleanupExpiredAsync(execution, session,
                    cause => cleanupActionFailure = cause, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                expiredItemCleanupRejected = ReferenceEquals(cleanupActionFailure, error)
                    && execution.Request!.HasOnlyFailure(error)
                    && error is not OperationCanceledException
                    && !TransactionOutcomeUncertainException.Contains(error);
                if (expiredItemCleanupRejected)
                    throw new InvalidOperationException("Expired-item cleanup failed.", error);
                throw;
            }
            if (!cleaned)
            {
                expiredItemCleanupRejected = true;
                throw new InvalidOperationException(
                    "Selected character is no longer owned by the account during expired-item cleanup.");
            }
            await EnsurePuppetsAsync(execution, session, cancellationToken)
                .ConfigureAwait(false);
            await InitializeHousingAsync(execution, session, cancellationToken).ConfigureAwait(false);
            var state = await PrepareStateAsync(execution, session, prepared.Selected, prepared.Channel, cancellationToken)
                .ConfigureAwait(false);
            await ExecuteAcceptedWorkflowAsync(execution, session, state, prepared.Channel, refreshRoster, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ChannelAdmissionRefusedException refusal)
        {
            try
            {
                await outbox.RegisterExplicitAsync(
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
                    await DeliverCleanupAsync(notifications, CancellationToken.None).ConfigureAwait(false);
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
                    await outbox.RegisterExplicitAsync(
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
                    var compensated = await CompensateAsync(execution, session, CancellationToken.None).ConfigureAwait(false);
                    await DeliverCleanupAsync(compensated, CancellationToken.None).ConfigureAwait(false);
                }
                catch (SelectedSessionCleanupFailure cleanupFailure)
                {
                    try
                    {
                        await DeliverCleanupAsync(cleanupFailure.NotificationPlan, CancellationToken.None).ConfigureAwait(false);
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

        if (failure is not null)
            logger.LogWarning("Character selection failed account={AccountId} roster={RosterIndex} connection={ConnectionId} cause={Cause}",
                accountId, message.RosterIndex, context.Connection.ConnectionId, failure.SourceException.GetType().Name);

        // After the workflow and its compensation have finished: closing runs the connection's own cleanup.
        foreach (var connectionId in pendingCloses.Distinct())
        {
            if (connectionId == context.Connection.ConnectionId)
                await closures.RequestCloseAsync(context.Connection, CancellationToken.None).ConfigureAwait(false);
            else
                await closures.ResolveAndRequestCloseAsync(connectionId, CancellationToken.None).ConfigureAwait(false);
        }

        failure?.Throw();
        return true;
    }

    private async ValueTask ExecuteAcceptedWorkflowAsync(
        CommandExecutionContext execution, SelectedSessionIdentity session,
        SelectCharacterResult prepared, ChannelEntryIdentity channel, bool refreshRoster,
        CancellationToken cancellationToken)
    {
        var friends = await ReadRelationsAsync(execution, session, cancellationToken).ConfigureAwait(false);
        var (record, team) = await ReadPvpEntryAsync(execution, session, cancellationToken).ConfigureAwait(false);
        var messages = WithPvpRecord(stateMessages.CreateValidated(prepared, friends), record, team);
        ValidateSupplementalMessages(BuildPrePublicationMessages(session, prepared, refreshRoster));
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
        var inventory = await DailyRefillAsync(execution, session, prepared, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        prepared = inventory.Result;
        if (SendsFramesAfterTheCompletionMarker && inventory.CommittedUpdates.Count > 0)
        {
            await outbox.RegisterExplicitAsync(
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
            // composed from the older value would be stale by construction. None of these
            // frames reads anything the refill touches today, so this is not a fix -- it is the
            // difference between that being guaranteed and it being a coincidence. They are also
            // validated once above, before anything is sent, so a malformed set still fails before
            // the first frame goes out rather than halfway through.
            await outbox.RegisterExplicitAsync(
                session.ConnectionId,
                new GameExplicitMessageDelivery(
                    BuildPrePublicationMessages(session, prepared, refreshRoster),
                    []),
                cancellationToken).ConfigureAwait(false);
        }
        EnsureCurrent(session);

        var publication = await PublishAsync(session, prepared, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        await units.WriteAsync(execution, new("RecoverDarkElfRuinsRewards", [TicketRecipient(execution, session)]),
            async (_, token) => { await ruins.RecoverRewardsAsync(session, token).ConfigureAwait(false); return true; }, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker)
        {
            // A21 snapshots its global license records into the local seat before the first
            // selection response. A later 02F7 replaces only the global records, so prime them
            // during login as well as refreshing them when selection is requested.
            var selection = await ruins.SelectionAsync(session.CharacterId, cancellationToken).ConfigureAwait(false);
            EnsureCurrent(session);
            await outbox.RegisterExplicitAsync(session.ConnectionId,
                new GameExplicitMessageDelivery(Dfo.Server.Protocol.Game.DarkElfRuins.RuinsProjection.Selection(selection), []),
                cancellationToken).ConfigureAwait(false);
        }
        // Selection can straddle 06:00 while the scheduled snapshot sees it as unpublished.
        // Recheck the current period after publication using the same durable ticket claim.
        foreach (var refill in ticketRefills)
            await units.WriteAsync(execution, new(refill.SelectionWorkUnitName, [TicketRecipient(execution, session)]),
                (_, token) => refill.Service.RefillOnlineTicketAsync(session, token), cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        {
            if (!users.TryGetByConnection(session.ConnectionId, out var current) || current is null)
                throw new InvalidOperationException("Selected hotkey profile has no current connection.");
            var origin = new Dfo.Server.Data.Accounts.ClientSettingsOrigin(current.SessionIdentity, session.AccountId, current.Generation, session);
            // Job 10 is Creator in the server career catalog (70/upstream reference); A21's
            // receiver selects its Creator default resource when 1C7.keyType is 1.
            var keyType = prepared.Character.Identity.Job == 10 ? (byte)1 : (byte)0;
            if (!await ActivateHotkeysAsync(execution, origin, keyType, cancellationToken))
                throw new InvalidOperationException("Selected hotkey profile identity changed during delivery.");
        }
        if (SendsFramesAfterTheCompletionMarker && publication.TownPresence is { } town)
        {
            await PublishCommittedEntryAsync(
                session, town, cancellationToken).ConfigureAwait(false);
        }
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker && channel.Kind == ChannelEntryKind.Raid)
            await projections.ProjectAsync(new NotificationAudience.TheseSessions([session]),
                new AntonRaidTownArrived(session), cancellationToken).ConfigureAwait(false);
        if (SendsFramesAfterTheCompletionMarker)
            await projections.ProjectAsync(new NotificationAudience.TheseSessions([session]),
                new AntonRaidSessionArrived(session), cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        await AnnounceToFriendsAsync(execution, session, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (prepared.CharacterEntry.GuildMember is not null && channel.ChannelId is > 0 and <= byte.MaxValue)
            await AnnounceToGuildAsync(execution, session, (byte)channel.ChannelId, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker && publication.PvpLobby is { } pvp)
        {
            await outbox.RegisterExplicitAsync(
                session.ConnectionId,
                new GameExplicitMessageDelivery([new PvpLobbySnapshotMessage(pvp)], []),
                cancellationToken).ConfigureAwait(false);
            EnsureCurrent(session);
            var pvpPublication = await MarkPvpReadyAsync(session, prepared, cancellationToken).ConfigureAwait(false);
            EnsureCurrent(session);
            if (team?.Representative == session.CharacterId)
            {
                await totalMatchCandidates.ProjectLobbyAsync(session, channel, cancellationToken).ConfigureAwait(false);
                EnsureCurrent(session);
            }
            if (channel.SupportsMatchmaking)
            {
                await dailyMissions.ProjectAsync(session, channel, cancellationToken).ConfigureAwait(false);
                EnsureCurrent(session);
            }
            // The command decided who; telling them is a separate step, and this is the shape
            // that pairing takes everywhere from here on. What this used to do instead was build
            // a delivery plan out of connections the domain service had looked up for it.
            if (pvpPublication.Publication.Audience.Count > 0)
            {
                await projections.ProjectAsync(
                    new NotificationAudience.TheseCharacters(pvpPublication.Publication.Audience),
                    new PvpParticipantPublished(pvpPublication.Publication.Newcomer),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        if (SendsFramesAfterTheCompletionMarker)
        {
            await outbox.RegisterExplicitAsync(
                session.ConnectionId,
                new GameExplicitMessageDelivery(
                    [new TrailingEquipmentLockSnapshotMessage(
                        prepared.CharacterInventory, prepared.AccountInventory)],
                    []),
                cancellationToken).ConfigureAwait(false);
        }

        EnsureCurrent(session);

        var quests = await SynchronizeQuestsAsync(execution, session, prepared, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (quests.Change is { } questChange)
        {
            prepared = questChange.Result;
            if (SendsFramesAfterTheCompletionMarker)
                await outbox.RegisterExplicitAsync(
                session.ConnectionId,
                new GameExplicitMessageDelivery(
                    [new AcceptedQuestProgressMessage(questChange.State.Quests)], []),
                cancellationToken).ConfigureAwait(false);
        }
        EnsureCurrent(session);

        var pet = await RevivePetAsync(execution, session, prepared, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (pet.Change is { } petChange)
        {
            prepared = petChange.Result;
            if (SendsFramesAfterTheCompletionMarker)
                await outbox.RegisterExplicitAsync(
                session.ConnectionId,
                new GameExplicitMessageDelivery(
                    [new PetRevivalStateMessage(petChange.State)], []),
                cancellationToken).ConfigureAwait(false);
        }
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker)
            await ProjectWeaknessAsync(session, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker)
            await outbox.RegisterExplicitAsync(session.ConnectionId,
                new GameExplicitMessageDelivery([new PremiumServiceStateMessage(prepared.Account.Entitlements,
                    prepared.CharacterEntry.Progression.PremiumUsageSlots, prepared.World.PreparedAt)], []),
                cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker)
            await ProjectItemUsageAsync(execution, session, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker)
            _ = await ProjectQuotaAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.DimensionDungeon,
                cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker)
            _ = await ProjectQuotaAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.MirrorDungeon,
                cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker)
            _ = await ProjectQuotaAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.DespairTower,
                cancellationToken).ConfigureAwait(false);
        if (SendsFramesAfterTheCompletionMarker)
            _ = await ProjectQuotaAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.AltarDungeon,
                cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker)
            _ = await ProjectQuotaAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.TournamentDungeon,
                cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker)
            _ = await ProjectQuotaAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.HerasDungeon, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker)
            _ = await ProjectQuotaAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.LostLand, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker)
            _ = await ProjectQuotaAsync(session, Dfo.Server.Abstractions.UsageQuotas.UsageQuotaFamily.GoldSilverHorn, cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker
            && await sao.ReadStatusAsync(cancellationToken).ConfigureAwait(false) is { } saoProgress)
            await outbox.RegisterExplicitAsync(session.ConnectionId,
                new GameExplicitMessageDelivery([new SaoCurrentFloorNotification(saoProgress)], []), cancellationToken).ConfigureAwait(false);
        EnsureCurrent(session);
        if (SendsFramesAfterTheCompletionMarker && lotteryCounterExperiment is { Enabled: true } experiment)
        {
            await outbox.RegisterExplicitAsync(session.ConnectionId,
                new GameExplicitMessageDelivery([new LotteryCounterStateMessage(experiment.Current, experiment.Threshold)], []),
                cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Lottery counter experiment character={CharacterId}: current={Current} threshold={Threshold}",
                session.CharacterId, experiment.Current, experiment.Threshold);
        }

        if (prepared.CharacterEntry.GuildMember is not null)
            await RecordGuildAttendanceAsync(execution, session, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<SupplementalSynchronizationMessage> BuildPrePublicationMessages(
            SelectedSessionIdentity session,
            SelectCharacterResult result, bool refreshRoster)
    {
        var messages = new List<SupplementalSynchronizationMessage>
        {
            new UserVisibilityMessage(
                session.WireUserId,
                result.CharacterEntry.Public.Display?.VisibilityBits ?? checked((byte)result.CharacterEntry.Public.VisibilityBits)),
            new CloneTitleStateMessage(result.CharacterEntry.Public.Display?.TitleAppearanceItemId ?? result.CharacterEntry.Public.CloneTitleItemId),
        };
        if (refreshRoster)
            messages.Add(new AccountRosterStateMessage(result.RosterEntry
                ?? throw new InvalidDataException("Roster state is unavailable.")));
        // Match the honor fields in the selected character's USERINFO snapshot.
        messages.Add(new HonorProgressStateMessage(
            result.CharacterEntry.Public.Honor.Level,
            result.CharacterEntry.Public.Honor.Experience));
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
                outbox.RegisterCmdAsync(connectionId, command, cancellationToken),
            IGameNotiMessage notification =>
                outbox.RegisterNotiAsync(connectionId, notification, cancellationToken),
            _ => throw new InvalidDataException(
                $"Game message '{message.GetType().Name}' has no CMD/NOTI classification."),
        };

    private void EnsureCurrent(SelectedSessionIdentity session)
    {
        if (!selectedSessions.IsSelected(session))
        {
            throw new InvalidOperationException("Selected session is no longer current.");
        }
    }


    private ValueTask<PreparedSelectedCharacterSession> PrepareSessionAsync(CommandExecutionContext execution,
        Dfo.Server.Data.Accounts.AccountId accountId, ushort rosterIndex, ChannelEntryIdentity channel,
        NetworkConnectionId connectionId, long generation, CancellationToken cancellationToken) =>
        units.WriteAsync(execution, new("PrepareSelectedCharacterSession", []), (unit, token) =>
            PrepareSessionCoreAsync(execution, unit, accountId, rosterIndex, channel, connectionId, generation, token), cancellationToken);
    internal async ValueTask<PreparedSelectedCharacterSession> PrepareSessionCoreAsync(CommandExecutionContext execution,
        CommandWorkUnit? unit, AccountId accountId, ushort rosterIndex, ChannelEntryIdentity channel,
        NetworkConnectionId connectionId, long generation, CancellationToken cancellationToken)
    {
        if (!users.TryGetByConnection(connectionId, out var online)
            || online?.AccountId != accountId || online.Generation != generation)
            throw new InvalidOperationException("Select-character connection generation is stale.");
        var selected = await preparation.PreflightAsync(
            accountId, rosterIndex, channel, cancellationToken)
            .ConfigureAwait(false);

        // This must precede every lifecycle side effect. A selected character that cannot be
        // represented by the game protocol is not an accepted session.
        _ = SelectedSessionIdentity.ToWireUserId(selected.Identity.CharacterId);
        var cleanupNotifications = new List<SelectedSessionCleanupNotificationPlan>();
        var previous = selectedSessions.Find(accountId);
        if (previous?.ConnectionId == connectionId)
        {
            try
            {
                cleanupNotifications.Add(await cleanup.CleanupAsync(execution, previous, includeOwner: true, unit, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception error)
            {
                if (error is SelectedSessionCleanupFailure cleanupFailure)
                    cleanupNotifications.Add(cleanupFailure.NotificationPlan);
                _ = selectedSessions.ReleaseExact(previous);
                throw SelectCharacterPhaseFailure.Create(
                    error, [connectionId], cleanupNotifications);
            }
        }

        if (!users.TryGetByConnection(connectionId, out var refreshed)
            || refreshed?.AccountId != accountId
            || (previous?.ConnectionId != connectionId
                && refreshed.Generation != generation))
        {
            throw new InvalidOperationException(
                "Select-character connection changed during cleanup.");
        }

        var session = SelectedSessionIdentity.Create(
            accountId,
            selected.Identity.CharacterId,
            connectionId,
            refreshed.Generation,
            channel.ListenerPort);
        var pendingCloses = new List<NetworkConnectionId>();
        try
        {
            if (previous is not null && previous.ConnectionId != connectionId)
            {
                pendingCloses.Add(previous.ConnectionId);
                try
                {
                    cleanupNotifications.Add(await cleanup.CleanupAsync(execution, previous, includeOwner: false, unit, cancellationToken).ConfigureAwait(false));
                }
                finally
                {
                    _ = selectedSessions.ReleaseExact(previous);
                }
            }

            // Cleanup must finish while the displaced session is still the current owner, otherwise
            // its character artifacts would be indistinguishable from replacement state at the
            // cleanup seam.
            var displaced = selectedSessions.Select(session);
            if (displaced is not null)
                throw new InvalidOperationException(
                    "A selected session changed during account selection.");

            return new PreparedSelectedCharacterSession(
                session, selected, channel, cleanupNotifications, pendingCloses);
        }
        catch (Exception error)
        {
            if (error is SelectedSessionCleanupFailure phaseCleanupFailure)
                cleanupNotifications.Add(phaseCleanupFailure.NotificationPlan);
            try
            {
                cleanupNotifications.Add(await cleanup.CleanupAsync(execution, session, includeOwner: true, unit, CancellationToken.None).ConfigureAwait(false));
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
                _ = selectedSessions.ReleaseExact(session);
            }

            pendingCloses.Add(session.ConnectionId);
            throw SelectCharacterPhaseFailure.Create(
                error, pendingCloses, cleanupNotifications);
        }
    }
    internal ValueTask<bool> CleanupExpiredAsync(CommandExecutionContext execution, SelectedSessionIdentity session,
        Action<Exception> failed, CancellationToken ct) => units.WriteAsync(execution, new("CleanupExpiredCharacterItems", []), async (_, token) =>
        {
            try { return (await expiredItems.CleanupAsync(session.AccountId, session.CharacterId, token).ConfigureAwait(false)).Owned; }
            catch (Exception error) { failed(error); throw; }
        }, ct);
    private ValueTask<bool> EnsurePuppetsAsync(CommandExecutionContext execution, SelectedSessionIdentity session, CancellationToken ct) =>
        units.WriteAsync(execution, new("EnsureDailyContractPuppets", []), (_, token) =>
        {
            if (!selectedSessions.IsSelected(session))
                throw new InvalidOperationException("Selected session changed before puppet delivery.");
            return puppets.EnsureDailyAsync(session.AccountId, session.CharacterId, token);
        }, ct);
    private ValueTask<bool> InitializeHousingAsync(CommandExecutionContext execution, SelectedSessionIdentity session, CancellationToken ct) =>
        units.WriteAsync(execution, new("InitializeHousing", []), async (_, token) =>
        {
            return await InitializeHousingStageAsync(selectedSessions, housingEntry, session, token).ConfigureAwait(false);
        }, ct);
    internal static async ValueTask<bool> InitializeHousingStageAsync(ISelectedSessionDomainService sessions,
        IMarriageHousingDomainService housing, SelectedSessionIdentity session, CancellationToken ct)
    {
        if (!sessions.IsSelected(session)) return false;
        await housing.InitializeAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        return true;
    }
    internal ValueTask<SelectCharacterResult> PrepareStateAsync(CommandExecutionContext execution, SelectedSessionIdentity session,
        ActiveCharacterState selected, ChannelEntryIdentity channel, CancellationToken ct) =>
        units.WriteAsync(execution, new("PrepareSelectedCharacterState", []), async (_, token) =>
        {
            var result = await preparation.PrepareAsync(selected, session.AccountId, channel, token).ConfigureAwait(false);
            result = await preparation.PlaceAtSpawnAsync(session, result, token).ConfigureAwait(false);
            return await inventoryEntry.PrepareLeaseAsync(session, result, token).ConfigureAwait(false);
        }, ct);
    internal ValueTask<EntryInventoryPreparation> DailyRefillAsync(CommandExecutionContext execution, SelectedSessionIdentity session,
        SelectCharacterResult prepared, CancellationToken ct) => units.WriteAsync(execution, new("ApplySelectedCharacterDailyRefill", []),
            (_, token) => inventoryEntry.ApplyDailyRefillAsync(session, prepared, token), ct);
    internal async ValueTask<PublishedSelectedCharacterSession> PublishAsync(SelectedSessionIdentity session,
        SelectCharacterResult prepared, CancellationToken ct)
    {
        selectedSessions.EnterWorld(session);
        sessionEntry.Publish(session, prepared);
        return new(await sessionEntry.PlanTownPublicationAsync(session, prepared, ct).ConfigureAwait(false),
            await sessionEntry.PlanPvpAsync(session, prepared, ct).ConfigureAwait(false));
    }
    private SelectedSessionRecipient TicketRecipient(CommandExecutionContext execution, SelectedSessionIdentity session)
    {
        var request = (CommandRequestContext.ProtocolNonEffect)execution.Request!;
        // Match the old selected-scope capture: current exact session, original physical input and frame metadata.
        var connection = users.TryGetByConnection(session.ConnectionId, out var user) ? user?.Connection : null;
        return new(session, connection, ((ProtocolFrameMetadata.Game)request.Metadata).Value);
    }
    private ValueTask<bool> ActivateHotkeysAsync(CommandExecutionContext execution, ClientSettingsOrigin origin, byte keyType, CancellationToken ct)
    {
        execution.Request!.DemandCurrent();
        if (origin.SelectedSession is { } session)
        {
            if (selectedSessions.StandingOf(session) is not { Fenced: false })
                throw new InvalidOperationException("The selected session is stale or has an unknown settlement outcome.");
            if (users.TryGetByConnection(session.ConnectionId, out var user) && user is not null
                && (user.AccountId != session.AccountId || user.CurrentCharacterId != session.CharacterId))
                throw new InvalidOperationException("The recipient connection no longer owns the selected character.");
        }
        return ValueTask.FromResult(clientSettings.ActivateHotkeyProfile(origin, keyType));
    }
    internal ValueTask<SelectedCharacterPvpPublication> MarkPvpReadyAsync(SelectedSessionIdentity session, SelectCharacterResult prepared, CancellationToken ct)
    {
        var publication = sessionEntry.PlanPvpPublication(session, prepared);
        sessionEntry.MarkPvpReady(session, prepared);
        return ValueTask.FromResult(new SelectedCharacterPvpPublication(publication));
    }
    internal ValueTask<SynchronizedSelectedCharacterQuests> SynchronizeQuestsAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, SelectCharacterResult prepared, CancellationToken ct) =>
        units.WriteAsync(execution, new("SynchronizeSelectedCharacterQuests", []), async (_, token) =>
            new SynchronizedSelectedCharacterQuests(await questEntry.SynchronizeAsync(session, prepared, token).ConfigureAwait(false)), ct);
    internal ValueTask<RevivedSelectedCharacterPet> RevivePetAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, SelectCharacterResult prepared, CancellationToken ct) =>
        units.WriteAsync(execution, new("ReviveSelectedCharacterPet", []), async (_, token) =>
            new RevivedSelectedCharacterPet(await petEntry.ReviveAsync(session, prepared, token).ConfigureAwait(false)), ct);
    internal async ValueTask<SelectedSessionCleanupNotificationPlan> CompensateAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, CancellationToken ct)
    {
        try { return await cleanup.CleanupAsync(execution, session, includeOwner: true, cancellationToken: ct).ConfigureAwait(false); }
        finally { selectedSessions.ReleaseExact(session); }
    }
    private ValueTask DeliverCleanupAsync(SelectedSessionCleanupNotificationPlan plan, CancellationToken ct) =>
        CleanupNotificationStages.DeliverAsync(plan, outbox, projections, projectionState, parties, ordering, raidSquads, ct);
    private async ValueTask PublishCommittedEntryAsync(SelectedSessionIdentity session, TownParticipantPublicationPlan publication, CancellationToken ct)
    {
        if (!selectedSessions.IsInWorld(session)) return;
        await outbox.RegisterExplicitAsync(session.ConnectionId, new(TownPresenceMessages.ForNewcomer(publication.State), []), ct).ConfigureAwait(false);
        if (!selectedSessions.IsInWorld(session)) return;
        await PartyTownProjection.PartyTownRefreshObservedPartiesAsync(townAreas, projections, projectionState, parties, [session], ct).ConfigureAwait(false);
        if (!selectedSessions.IsInWorld(session)) return;
        var registered = await PartyExactNotificationDelivery.PublishWithRetryAsync(projections, publication.Audience,
            new TownParticipantPublished(publication.State), ct).ConfigureAwait(false);
        if (!registered.Registered.IsEmpty)
            await PartyTownProjection.PartyTownRefreshObservedPartiesAsync(townAreas, projections, projectionState, parties, registered.Registered, ct).ConfigureAwait(false);
    }
    private ValueTask ProjectWeaknessAsync(SelectedSessionIdentity session, CancellationToken ct) => selectedSessions.IsInWorld(session)
        ? outbox.RegisterNotiAsync(session.ConnectionId, new WeaknessProjectionNotification(weaknessState.For(session.CharacterId).Value), ct)
        : ValueTask.CompletedTask;
    private async ValueTask ProjectItemUsageAsync(CommandExecutionContext execution, SelectedSessionIdentity session, CancellationToken ct)
    {
        var snapshot = await units.ReadOnlyAsync(execution, new("ReadItemUsageSnapshot", []),
            token => ItemUsageSnapshotReader.ReadAsync(itemUsage, selectedSessions, clock, session, token), ct).ConfigureAwait(false);
        if (!snapshot.IsCurrent || !selectedSessions.IsInWorld(session)) return;
        var messages = ItemUsageCountMessage.FullSnapshot(snapshot.Items).Cast<IGameNotiMessage>()
            .Concat(ItemUsageCooldownProjection.Snapshot(snapshot.Items, snapshot.At));
        foreach (var message in messages)
        {
            if (!selectedSessions.IsInWorld(session)) return;
            await outbox.RegisterNotiAsync(session.ConnectionId, message, ct).ConfigureAwait(false);
        }
    }
    /// <summary>The selected character's record and the account's current total-match team projection.</summary>
    private ValueTask<(PvpRecordView Record, PvpTotalMatchTeamView? Team)> ReadPvpEntryAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, CancellationToken ct) =>
        units.ReadOnlyAsync(execution, new("ReadSelectedCharacterPvpRecord", []),
            async token =>
            {
                var record = await pvpRecords.ViewAsync(session.CharacterId, token).ConfigureAwait(false);
                var team = await pvpTotalMatchTeams.ReadAsync(session.AccountId, token).ConfigureAwait(false);
                return (record, team);
            }, ct);

    /// <summary>
    /// NOTI <c>0x0030</c> right after HOTKEY_OPTION, as upstream <c>NewCharacterInitSequence</c> places it (after both
    /// USERINFO0 and the hotkeys): without it the client's grade bar reads 0/0. Every channel gets it, a duel lobby or not.
    /// </summary>
    internal static IReadOnlyList<IGameServerMessage> WithPvpRecord(
        IReadOnlyList<IGameServerMessage> messages, PvpRecordView record, PvpTotalMatchTeamView? team = null)
    {
        // A batch without the hotkeys (only a test's factory makes one) carries the record last.
        var after = messages.Count;
        for (var index = 0; index < messages.Count; index++)
            if (messages[index] is HotkeyStateMessage) after = index + 1;
        return team is null
            ? [.. messages.Take(after), new PvpBattle.PvpRecordMessage(record), .. messages.Skip(after)]
            : [.. messages.Take(after), new PvpBattle.PvpRecordMessage(record), new PvpTotalMatchTeamInfo(team), .. messages.Skip(after)];
    }

    // The entry batch carries the real friend list and the blacklist behind it (RQ-038). Read here,
    // after preparation committed and before the batch is composed, in a read-only unit of its own:
    // every friend's channel is where that friend is at this moment.
    private async ValueTask<FriendListRefresh> ReadRelationsAsync(CommandExecutionContext execution,
        SelectedSessionIdentity session, CancellationToken ct)
    {
        var refresh = await units.ReadOnlyAsync(execution, new("ReadSelectedCharacterRelations", []),
            token => FriendListRefresh.BuildAsync(session.CharacterId, relations, identities, selectedSessions, channels,
                clock.LocalTimeZone, token), ct).ConfigureAwait(false);
        foreach (var skipped in refresh.SkippedFriends)
            logger.LogWarning("Friend entry left out of entry character={CharacterId} target={Target}: NOTI 0x0111 cannot carry it ({NameBytes}-byte name, level {Level}, grow {Grow}, or a repeated name)",
                session.CharacterId.Value, skipped.Character.Value, skipped.Name.Length, skipped.Level, skipped.GrowType);
        foreach (var skipped in refresh.SkippedBlacklist)
            logger.LogWarning("Blacklist entry left out of entry character={CharacterId} target={Target}: its {NameBytes}-byte name does not fit CMD 0x0078",
                session.CharacterId.Value, skipped.Character.Value, skipped.Name.Length);
        return refresh;
    }
    // RQ-050: the characters keeping this one as a friend are told it came online, with its channel.
    // After publication, so the session the handler reads the channel from is in the world; after
    // any older session's cleanup was delivered, so its friends hear the departure first. Not tied
    // to the post-marker frames: those are the newcomer's own, and this goes to other people.
    private async ValueTask AnnounceToFriendsAsync(CommandExecutionContext execution, SelectedSessionIdentity session,
        CancellationToken ct)
    {
        var observers = await units.ReadOnlyAsync(execution, new("ReadFriendObservers", []),
            token => FriendObservers.InWorldAsync(session.CharacterId, relations, selectedSessions, token), ct).ConfigureAwait(false);
        if (observers.Count == 0 || !selectedSessions.IsInWorld(session)) return;
        _ = await projections.ProjectAsync(new NotificationAudience.TheseCharacters(observers),
            new FriendCameOnline(session.CharacterId), ct).ConfigureAwait(false);
    }
    private ValueTask<NotificationDelivery> ProjectQuotaAsync(SelectedSessionIdentity session, UsageQuotaFamily family, CancellationToken ct) =>
        projections.ProjectAsync(new NotificationAudience.TheseSessions([session]), new UsageQuotaViewChanged(session.CharacterId, family), ct);

    private async ValueTask RecordGuildAttendanceAsync(
        CommandExecutionContext execution, SelectedSessionIdentity session, CancellationToken ct)
    {
        if (!selectedSessions.IsInWorld(session))
            return;

        Exception? actionFailure = null;
        var write = await units.WriteResultAsync(execution, new("RecordGuildAttendance", []), async (unit, token) =>
        {
            try
            {
                return await guildProgress.RecordAttendanceAsync(session.CharacterId, token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
            {
                actionFailure = error;
                unit.RequireRollback(error);
                return false;
            }
        }, ct).ConfigureAwait(false);
        var failure = write is CommandWriteResult<bool>.CommitRolledBack rollback ? rollback.Cause : actionFailure;
        if (failure is not null)
        {
            logger.LogWarning(failure, "Guild attendance could not be recorded for character {Character}", session.CharacterId.Value);
            return;
        }
        if (write is not CommandWriteResult<bool>.Returned { Value: true })
            return;

        var panel = await units.ReadOnlyAsync(execution, new("ReadGuildAttendancePanel", []), async token =>
        {
            try
            {
                return await GuildInfoPanel.ReadAsync(guilds, guildProgress, session.CharacterId, token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "Guild attendance was recorded but the panel could not be read for character {Character}",
                    session.CharacterId.Value);
                return GuildInfoPanel.Empty;
            }
        }, ct).ConfigureAwait(false);
        if (panel.Members.Count != 0)
        {
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters(
                panel.Members.Select(member => member.Character).ToArray()), panel, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask AnnounceToGuildAsync(
        CommandExecutionContext execution, SelectedSessionIdentity session, byte channel, CancellationToken ct)
    {
        var roster = await units.ReadOnlyAsync<IReadOnlyList<GuildRosterEntry>>(execution, new("ReadGuildPresenceObservers", []),
            async token =>
            {
                try
                {
                    return await guilds.ReadRosterEntriesAsync(session.CharacterId, token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    logger.LogWarning(error, "Guild arrival notice dropped for character {Character}: reading its guild failed",
                        session.CharacterId.Value);
                    return [];
                }
            }, ct).ConfigureAwait(false);

        if (GuildPresenceAudience.FromRoster(session.CharacterId, roster) is not { } audience ||
            !selectedSessions.IsInWorld(session))
            return;

        await projections.ProjectAsync(new NotificationAudience.TheseCharacters(audience.OtherMembers),
            new GuildMemberPresenceChanged(audience.Character, audience.Name, channel, session), ct).ConfigureAwait(false);
    }

}
