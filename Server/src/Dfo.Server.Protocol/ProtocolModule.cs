using Dfo.Server.Protocol.Game.Creatures;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Protocol.Game.InventorySort;
using Dfo.Server.Protocol.Game.CrystalContract;
using Dfo.Server.Protocol.Game.Mailbox;
using Dfo.Server.Protocol.Game.RecoverStamina;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game.Fatigue;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Protocol.Channel;
using Dfo.Server.Protocol.Channel.Connect;
using Dfo.Server.Protocol.Channel.Info;
using Dfo.Server.Protocol.Channel.Script;
using Dfo.Server.Protocol.Channel.ScriptVersion;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.AccountRoster;
using Dfo.Server.Protocol.Game.AuctionService;
using Dfo.Server.Protocol.Game.CharacterName;
using Dfo.Server.Protocol.Game.InformNotice;
using Dfo.Server.Protocol.Game.ClientHardwareDetail;
using Dfo.Server.Protocol.Game.ClientSpecProfile;
using Dfo.Server.Protocol.Game.ClientSpecStatistic;
using Dfo.Server.Protocol.Game.ConnectionCheck;
using Dfo.Server.Protocol.Game.ContentsPlayInfo;
using Dfo.Server.Protocol.Game.CreateCharacter;
using Dfo.Server.Protocol.Game.DeleteCharacter;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.BuyItem;
using Dfo.Server.Protocol.Game.CeraShopPurchase;
using Dfo.Server.Protocol.Game.MagicBoxBatchOpening;
using Dfo.Server.Protocol.Game.MagicBoxSingleOpening;
using Dfo.Server.Protocol.Game.PackageOpening;
using Dfo.Server.Protocol.Game.LotteryOpening;
using Dfo.Server.Protocol.Game.VendingMachine;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.AvatarPackageOpening;
using Dfo.Server.Protocol.Game.GenCeraTicket;
using Dfo.Server.Protocol.Game.SellItem;
using Dfo.Server.Protocol.Game.RepairItem;
using Dfo.Server.Protocol.Game.DisjointItem;
using Dfo.Server.Protocol.Game.ReinforceItem;
using Dfo.Server.Protocol.Game.EnchantByBead;
using Dfo.Server.Protocol.Game.MagicSeal;
using Dfo.Server.Protocol.Game.Outworld;
using Dfo.Server.Protocol.Game.Forging;
using Dfo.Server.Protocol.Game.EquipmentDurability;
using Dfo.Server.Protocol.Game.LearnSkill;
using Dfo.Server.Protocol.Game.ShopPurchaseCount;
using Dfo.Server.Protocol.Game.TutorialLevelUp;
using Dfo.Server.Protocol.Game.UnansweredReports;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.StoryPause;
using Dfo.Server.Protocol.Game.UseStackable;
using Dfo.Server.Protocol.Game.QuestClearCoupon;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Exit;
using Dfo.Server.Protocol.Game.ReturnToSelection;
using Dfo.Server.Protocol.Game.HiddenCharacterInfo;
using Dfo.Server.Protocol.Game.HonorLevel;
using Dfo.Server.Protocol.Game.InitialConnection;
using Dfo.Server.Protocol.Game.Login;
using Dfo.Server.Protocol.Game.MapEntryReports;
using Dfo.Server.Protocol.Game.MercenaryInfo;
using Dfo.Server.Protocol.Game.RosterFollowUp;
using Dfo.Server.Protocol.Game.SaveGameOption;
using Dfo.Server.Protocol.Game.SaveCharacterOption;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.SessionMetrics;
using Dfo.Server.Protocol.Game.QuestAcceptance;
using Dfo.Server.Protocol.Game.QuestGiveUp;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Dfo.Server.Protocol.Game.QuestProgress;
using Dfo.Server.Protocol.Game.TaggedIdReport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Protocol;

/// <summary>
/// Registers the protocol slices: every wire key that can be decoded and every outbound
/// message that can be encoded.
/// </summary>
/// <remarks>
/// This module is the only place a protocol is wired. Every artifact a protocol needs -- its
/// messages, its decoder, its encoder, its message handler, its command and that command's
/// handler -- lives in that protocol's own folder, and the tables below are where those artifacts
/// become reachable.
/// </remarks>
public sealed class ProtocolModule : IServerModule
{
    public string Name => "Protocol";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<MailboxDelivery>();
        services.RegisterMessageHandler<MailboxSendHandler>(ClientMessageType.GameMailboxSend);
        services.RegisterMessageHandler<MailboxSendHandler>(ClientMessageType.GameMailboxSendMulti);
        services.AddGameCmdMessageDecoder<MailboxSendDecoder>(0x005E, ClientMessageType.GameMailboxSend);
        services.AddGameCmdMessageDecoder<MailboxSendMultiDecoder>(0x013B, ClientMessageType.GameMailboxSendMulti);
        services.AddGameCmdMessageEncoder<MailboxSendResponse, MailboxSendEncoder>();
        services.AddNotificationHandler<MailboxArrived, MailboxArrivedHandler>(retryLimit: 0);
        services.AddCommandHandler<MailboxCommand, MailboxResult, MailboxCommandHandler>(CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddNotificationHandler<MailboxCompleted, MailboxCompletedHandler>(retryLimit: 0);
        services.RegisterMessageHandler<MailboxClaimHandler>(ClientMessageType.GameMailboxClaim);
        services.AddGameCmdMessageDecoder<MailboxClaimDecoder>(0x005F, ClientMessageType.GameMailboxClaim);
        services.AddGameCmdMessageEncoder<MailboxClaimResponse, MailboxClaimEncoder>();
        services.RegisterMessageHandler<MailboxActionHandler>(ClientMessageType.GameMailboxAction);
        services.AddGameCmdMessageDecoder<MailboxActionDecoder>(0x0086, ClientMessageType.GameMailboxAction);
        services.AddGameCmdMessageEncoder<MailboxActionResponse, MailboxActionEncoder>();
        // A21 character 000, 2026-09-24: CMD 0060 seq=28 has an empty body.
        services.RegisterMessageHandler<MailboxOpenHandler>(ClientMessageType.GameMailboxOpen);
        services.AddGameCmdMessageDecoder<MailboxOpenDecoder>(0x0060, ClientMessageType.GameMailboxOpen);
        services.AddGameCmdMessageEncoder<MailboxOpenResponse, MailboxOpenEncoder>();
        services.RegisterMessageHandler<MailboxQueryHandler>(ClientMessageType.GameMailboxQuery);
        services.AddGameCmdMessageDecoder<MailboxQueryDecoder>(0x0144, ClientMessageType.GameMailboxQuery);
        services.AddGameCmdMessageEncoder<MailboxQueryResponse, MailboxQueryEncoder>();
        services.AddGameNotiMessageEncoder<MailboxAlarm, MailboxAlarmEncoder>();
        services.TryAddSingleton<PartyClientIdRegistry>();
        services.Replace(ServiceDescriptor.Singleton<IPartyService, PartyClientProjectionService>());
        services.AddGameMessageSending();
        services.TryAddSingleton<CharacterSelectionDelivery>();
        services.TryAddSingleton<DungeonRunDeliveryRegistry>();
        services.TryAddSingleton<DungeonPartyRecipients>();
        services.TryAddSingleton<DungeonProjectionGate>();
        services.AddHostedService<DungeonPartyWipeService>();
        services.AddCommandHandler<RecoverStaminaCommand, RecoverStaminaResult, RecoverStaminaCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddNotificationHandler<StaminaRecovered, StaminaRecoveredHandler>(retryLimit: 0);
        services.TryAddSingleton<FatigueProjectionDelivery>();
        services.AddHostedService<FatigueDailyRefreshService>();
        services.AddGameNotiMessageEncoder<FatigueNotification, FatigueEncoder>();
        services.TryAddSingleton<WeaknessProjectionDelivery>();
        services.AddHostedService<WeaknessRecoveryService>();
        services.AddHostedService<CreatureClockService>();
        services.TryAddSingleton<CreatureProjectionDelivery>();
        services.AddCommandHandler<AdvanceCreatureClockCommand, IReadOnlyList<CreatureRuntimeChange>, AdvanceCreatureClockCommandHandler>(CommandExecutionKind.Transactional);
        services.AddGameNotiMessageEncoder<CreatureExperienceNotification, CreatureExperienceEncoder>();
        services.AddGameNotiMessageEncoder<CreatureStateNotification, CreatureStateEncoder>();
        services.AddGameNotiMessageEncoder<CreatureDeathNotification, CreatureDeathEncoder>();
        services.AddGameNotiMessageEncoder<CreatureRevivalNotification, CreatureRevivalEncoder>();
        services.AddGameNotiMessageEncoder<CreatureRenameNotification, CreatureRenameEncoder>();
        services.AddGameNotiMessageEncoder<CreatureEvolutionNotification, CreatureEvolutionEncoder>();
        services.AddGameNotiMessageEncoder<CreatureResponseNotification, CreatureResponseEncoder>();
        services.AddHostedService<PremiumDailyRefreshService>();
        services.AddSingleton<ItemUsageProjectionDelivery>();
        services.AddHostedService<ItemUsageDailyRefreshService>();
        services.AddCommandHandler<ReadItemUsageSnapshotCommand, ItemUsageSnapshotResult, ReadItemUsageSnapshotCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<ConsumeDungeonBuffCommand, PremiumSnapshotResult, ConsumeDungeonBuffCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<ReadPremiumSnapshotCommand, PremiumSnapshotResult, ReadPremiumSnapshotCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<AdvanceWeaknessRecoveryCommand, IReadOnlyList<CharacterId>, AdvanceWeaknessRecoveryCommandHandler>(CommandExecutionKind.Transactional);
        var lotteryCounterExperiment = new LotteryCounterExperimentOptions(
            configuration.GetValue<bool>("LotteryCounterExperiment:Enabled"),
            configuration.GetValue<byte>("LotteryCounterExperiment:Current", 0),
            configuration.GetValue<byte>("LotteryCounterExperiment:Threshold", 30));
        if (lotteryCounterExperiment.Current > 127 || lotteryCounterExperiment.Threshold > 127)
            throw new InvalidOperationException("LotteryCounterExperiment values must be in 0..127.");
        services.TryAddSingleton(lotteryCounterExperiment);
        var revivalFlag = configuration.GetValue<byte>("Dungeon:RevivalAckFlag", 1);
        if (revivalFlag > 1) throw new InvalidOperationException("RevivalAckFlag must be 0 or 1.");
        services.TryAddSingleton(new RevivalOptions(revivalFlag));
        services.TryAddSingleton<DungeonEntryProjectionDelivery>();
        services.TryAddSingleton<DungeonPickupProjectionDelivery>();
        services.TryAddSingleton<DungeonPickupPostCommitCoordinator>();
        services.TryAddSingleton<DungeonClearProjectionDelivery>();
        services.TryAddSingleton<DungeonCardSelectionProjectionDelivery>();
        services.TryAddSingleton(DungeonCardPresentationTiming.Production);
        services.TryAddSingleton<IDungeonCardPresentationDelay,
            SystemDungeonCardPresentationDelay>();
        services.TryAddSingleton<DungeonCardPresentationCoordinator>();
        services.TryAddSingleton<IDungeonLoadingProjectionDelivery, DungeonLoadingProjectionDelivery>();
        services.TryAddSingleton<IDungeonRunParticipantDepartureSink>(provider =>
            provider.GetRequiredService<DungeonRunDeliveryRegistry>());
        // Every notification type declared in this assembly must have a handler registered below,
        // and the server refuses to start if one does not. Declared rather than discovered: the
        // check is only worth having while something says which assemblies it covers, and this
        // line is that something.
        services.AddNotificationCatalog(typeof(ProtocolModule).Assembly);
        // BUY_ITEM adds to the client inventory. Replaying its acknowledgement could duplicate that addition.
        services.AddNotificationHandler<NpcShopPurchaseCompleted, NpcShopPurchaseCompletedHandler>(retryLimit: 0);
        services.AddNotificationHandler<CeraShopPurchaseCompleted, CeraShopPurchaseCompletedHandler>(retryLimit: 0);
        services.AddNotificationHandler<OpeningCompleted, OpeningCompletedHandler>(retryLimit: 0);
        services.AddNotificationHandler<ShopPurchaseCountsReported, ShopPurchaseCountsReportedHandler>(retryLimit: 0);
        services.AddNotificationHandler<ItemSaleCompleted, ItemSaleCompletedHandler>(retryLimit: 0);
        services.AddNotificationHandler<EquipmentRepairCompleted, EquipmentRepairCompletedHandler>(retryLimit: 0);
        services.AddNotificationHandler<EquipmentDisjointCompleted, EquipmentDisjointCompletedHandler>(retryLimit: 0);
        services.AddNotificationHandler<EquipmentReinforcementCompleted,
            EquipmentReinforcementCompletedHandler>(retryLimit: 0);
        services.AddNotificationHandler<EquipmentEnchantCompleted,
            EquipmentEnchantCompletedHandler>(retryLimit: 0);
        services.AddNotificationHandler<MagicSealCompleted,
            MagicSealCompletedHandler>(retryLimit: 0);
        services.AddNotificationHandler<OutworldCompleted,
            OutworldCompletedHandler>(retryLimit: 0);
        services.AddNotificationHandler<ForgingCompleted,
            ForgingCompletedHandler>(retryLimit: 0);
        services.AddNotificationHandler<SortLockChanged, SortLockChangedHandler>(retryLimit: 0);
        services.AddNotificationHandler<InventorySorted, InventorySortedHandler>(retryLimit: 0);
        services.AddNotificationHandler<EquipmentDurabilityChanged, EquipmentDurabilityChangedHandler>(retryLimit: 0);
        services.AddNotificationHandler<ItemDiscardCompleted, ItemDiscardCompletedHandler>(retryLimit: 0);
        services.AddNotificationHandler<CrystalContractSelected, CrystalContractSelectedHandler>(retryLimit: 0);
        services.AddNotificationHandler<MaterialsConsumed, MaterialsConsumedHandler>(retryLimit: 0);
        services.TryAddSingleton<PartyProjectionStreamRegistry>();
        services.TryAddSingleton<PartyNotificationDelivery>();
        services.TryAddSingleton<IPartyTownProjectionDelivery, PartyTownProjectionDelivery>();
        services.TryAddSingleton<TownAreaProjectionGate>();
        services.TryAddSingleton<TownAreaProjectionDelivery>();
        services.AddNotificationHandler<PartyInviteIssued, PartyInviteIssuedHandler>(retryLimit: 0);
        services.AddNotificationHandler<PartyRealtimeChanged, PartyRealtimeChangedHandler>(retryLimit: 0);
        services.AddNotificationHandler<PartyInviteAccepted, PartyInviteAcceptedHandler>(retryLimit: 0);
        services.AddNotificationHandler<PartyEndpointsChanged, PartyEndpointsChangedHandler>(retryLimit: 0);
        services.AddNotificationHandler<PartyRosterChanged, PartyRosterChangedHandler>(retryLimit: 0);
        services.AddNotificationHandler<PartySettingsChanged, PartySettingsChangedHandler>(retryLimit: 0);
        services.AddNotificationHandler<PartyLeadershipChanged, PartyLeadershipChangedHandler>(retryLimit: 0);
        services.AddNotificationHandler<
            OtherCharacterDetailRequested,
            OtherCharacterDetailRequestedHandler>(retryLimit: 0);
        services.TryAddSingleton<ICleanupNotificationDelivery, CleanupNotificationDelivery>();
        services.AddNotificationHandler<SessionCleanupAnnounced, SessionCleanupAnnouncedHandler>(
            // Nothing. A departure notice that misses somebody is corrected by the next thing that
            // redraws who is standing where, and by the time a retry ran the person it describes
            // has been gone for a second -- the notice would arrive as news about nobody.
            retryLimit: 0);
        services.AddNotificationHandler<TownParticipantPublished, TownParticipantPublishedHandler>(
            // Party projection must follow the successful 0x0002 attempt. Its orchestrator retries
            // both exact phases together; a detached generic retry could only resend 0x0002.
            retryLimit: 0);
        services.AddNotificationHandler<TownPositionChanged, TownPositionChangedHandler>(
            retryLimit: 0);
        services.AddNotificationHandler<TownAreaRosterReconciled, TownAreaRosterReconciledHandler>(
            retryLimit: 0);
        services.TryAddSingleton<ChannelProtocolKeyProvider>();
        services.TryAddSingleton(SelectCharacterDeliveryOptions.Everything);
        services.TryAddSingleton<ISelectedCharacterStateMessageFactory, SelectedCharacterStateMessageFactoryService>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<INetworkConnectionObserver, OnlineUserConnectionObserver>());
        RegisterInboundDecoders(services);
        RegisterOutboundEncoders(services);
        RegisterCommands(services);
        RegisterMessageHandlers(services);
    }

    /// <summary>The commands a protocol slice owns, with their execution kind and access modes.</summary>
    private static void RegisterCommands(IServiceCollection services)
    {
        services.AddCommandHandler<UseRevivalCoinCommand, DungeonRevivalResult, UseRevivalCoinCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<EndClearReturnCommand, EndPartyWipeResult, EndClearReturnCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<EndPartyWipeCommand, EndPartyWipeResult, EndPartyWipeCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<MarkDungeonParticipantDeadCommand, MarkDungeonParticipantDeadResult, MarkDungeonParticipantDeadCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<MarkDungeonParticipantReadyCommand, MarkDungeonParticipantReadyResult, MarkDungeonParticipantReadyCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<GetChannelScriptVersionCommand, GetChannelScriptVersionResult, GetChannelScriptVersionCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<GetChannelScriptCommand, GetChannelScriptResult, GetChannelScriptCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<GetChannelInfoCommand, GetChannelInfoResult, GetChannelInfoCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<AcceptQuestCommand, QuestAcceptOutcome, AcceptQuestCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<GiveUpQuestCommand, GiveUpQuestOutcome, GiveUpQuestCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<SetQuestTriggerCommand, QuestTriggerOutcome, SetQuestTriggerCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<FinishQuestCommand, QuestFinishOutcome, FinishQuestCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<GrantKillExperienceCommand, KillExperienceResult, GrantKillExperienceCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<AdvanceHuntEnemyQuestCommand, AdvanceHuntEnemyQuestResult, AdvanceHuntEnemyQuestCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<ApplyObjectDestructionCommand, ObjectDestructionResult, ApplyObjectDestructionCommandHandler>(
            CommandExecutionKind.Transactional, accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<ApplyMonsterKillCommand, ApplyMonsterKillResult, ApplyMonsterKillCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<GrantClearExperienceCommand, ClearExperienceResult, GrantClearExperienceCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<PrepareReturnToSelectionCommand, PreparedReturnToSelection, PrepareReturnToSelectionCommandHandler>(
            CommandExecutionKind.Transactional, accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<CompleteReturnToSelectionCommand, CompletedReturnToSelection, CompleteReturnToSelectionCommandHandler>(
            CommandExecutionKind.Workflow, accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<AcknowledgeGameExitCommand, AcknowledgeGameExitResult, AcknowledgeGameExitCommandHandler>(
            CommandExecutionKind.Direct);
        // Every command below reaches a write that outlives the process, so every one of them
        // opens a use case boundary. A write with no boundary settles on its own: it looks like it
        // worked, it is not taken back by whatever else the command was doing, and no test shows
        // it, because a lone write does succeed. The kind decides only that -- the pipeline treats
        // Direct, Workflow and Transactional identically everywhere else.
        services.AddCommandHandler<RedeemGameLoginCommand, RedeemGameLoginResult, RedeemGameLoginCommandHandler>(
            CommandExecutionKind.Transactional,
            UserAccessMode.Exclusive);
        services.AddCommandHandler<GetAccountCharacterRosterCommand, AccountCharacterRoster, GetAccountCharacterRosterCommandHandler>(
            CommandExecutionKind.Direct,
            accountAccessMode: UserAccessMode.Shared);
        services.AddCommandHandler<CheckCharacterNameCommand, CharacterNameStatus, CheckCharacterNameCommandHandler>(
            CommandExecutionKind.Direct,
            accountAccessMode: UserAccessMode.Shared);

        // Creation and deletion both derive a slot from the account's current roster and then write
        // against it, so they take the account exclusively. Two concurrent creations sharing access
        // would compute the same next slot.
        services.AddCommandHandler<CreateCharacterCommand, CharacterCreationResult, CreateCharacterCommandHandler>(
            CommandExecutionKind.Transactional,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<DeleteCharacterCommand, CharacterDeletionResult, DeleteCharacterCommandHandler>(
            CommandExecutionKind.Transactional,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<PrepareSelectedCharacterSessionCommand, PreparedSelectedCharacterSession, PrepareSelectedCharacterSessionCommandHandler>(
            CommandExecutionKind.Transactional,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<CleanupExpiredCharacterItemsCommand, bool, CleanupExpiredCharacterItemsCommandHandler>(
            CommandExecutionKind.Transactional,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<EnsureDailyContractPuppetsCommand, bool, EnsureDailyContractPuppetsCommandHandler>(
            CommandExecutionKind.Transactional,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<PrepareSelectedCharacterStateCommand, SelectCharacterResult, PrepareSelectedCharacterStateCommandHandler>(
            CommandExecutionKind.Transactional,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<ApplySelectedCharacterDailyRefillCommand, EntryInventoryPreparation, ApplySelectedCharacterDailyRefillCommandHandler>(
            CommandExecutionKind.Transactional,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<PublishSelectedCharacterSessionCommand, PublishedSelectedCharacterSession, PublishSelectedCharacterSessionCommandHandler>(
            CommandExecutionKind.Workflow,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<MarkSelectedCharacterPvpReadyCommand, SelectedCharacterPvpPublication, MarkSelectedCharacterPvpReadyCommandHandler>(
            CommandExecutionKind.Workflow,
            accountAccessMode: UserAccessMode.Exclusive);
        // One further go, decided by this notification rather than for it. A failure here is not
        // only a throw: a recipient who runs out of the per-recipient deadline counts too, and by
        // then the frame may already be with the transport -- so the second attempt can arrive as
        // a duplicate rather than as a rescue. That is the trade this particular notification is
        // making, and it makes it knowingly: a lobby roster that missed a newcomer stays wrong
        // until something else redraws it, and the owner's standing premise for this whole path is
        // that hearing something twice is the cheaper of those two. What the client does with a
        // repeated publication is not something this repository can show. A notification where
        // arriving twice would not be harmless should declare zero.
        services.AddNotificationHandler<PvpParticipantPublished, PvpParticipantPublishedHandler>(
            retryLimit: 1);
        // These two change a character's stored state, so they are the phases that need a
        // boundary: the unit of work opens one for Transactional commands and for nothing else.
        // Without it the store would have no transaction to join and the change would land
        // outside any outcome that could take it back.
        services.AddCommandHandler<SynchronizeSelectedCharacterQuestsCommand, SynchronizedSelectedCharacterQuests, SynchronizeSelectedCharacterQuestsCommandHandler>(
            CommandExecutionKind.Transactional,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<ReviveSelectedCharacterPetCommand, RevivedSelectedCharacterPet, ReviveSelectedCharacterPetCommandHandler>(
            CommandExecutionKind.Transactional,
            accountAccessMode: UserAccessMode.Exclusive);
        // Not transactional, deliberately. Compensation is best effort: it collects what failed
        // and carries on, and it ends by throwing what it collected. A boundary here would take
        // back everything that had worked because something else did not -- the opposite of what
        // it is for. Its one lasting write, where the character was standing, settles on its own.
        services.AddCommandHandler<CompensateSelectedCharacterSessionCommand, CompensatedSelectedCharacterSession, CompensateSelectedCharacterSessionCommandHandler>(
            CommandExecutionKind.Workflow,
            accountAccessMode: UserAccessMode.Exclusive);

        // Both act on one character's own container, which the registry already keeps per
        // character; neither reads or writes anything the account owns, so neither takes it.
        // Transactional, and in the same change that broke the move into separate writes. Marking
        // it afterwards would leave a window in which a move was several writes with nothing to
        // settle them together -- a window every test would pass through green.
        services.AddCommandHandler<MoveItemSpaceCommand, ItemMoveResult, MoveItemSpaceCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<UseStackableCommand, ItemUseResult, UseStackableCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<HatchCreatureCommand, HatchCreatureResult, HatchCreatureCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<RenameCreatureCommand, RenameCreatureResult, RenameCreatureCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<QuestClearCouponCommand, QuestClearCouponResult, QuestClearCouponCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<UseFatiguePotionCommand, OpeningInventoryReceipt, UseFatiguePotionCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddSingleton<FatiguePotionHandler>();
        services.AddCommandHandler<ChangeSkillSlotCommand, bool, ChangeSkillSlotCommandHandler>(CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.RegisterMessageHandler<ChangeSkillSlotHandler>(ClientMessageType.GameChangeSkillSlot);
        services.AddGameCmdMessageDecoder<ChangeSkillSlotDecoder>(0x001C, ClientMessageType.GameChangeSkillSlot);
        services.AddGameCmdMessageEncoder<ChangeSkillSlotResponse, ChangeSkillSlotEncoder>();
        services.AddCommandHandler<SwitchSkillBuildCommand, SwitchSkillBuildResponse, SwitchSkillBuildCommandHandler>(CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.RegisterMessageHandler<SwitchSkillBuildHandler>(ClientMessageType.GameSwitchSkillBuild);
        services.AddGameCmdMessageDecoder<SwitchSkillBuildDecoder>(0x0104, ClientMessageType.GameSwitchSkillBuild);
        services.AddGameCmdMessageEncoder<SwitchSkillBuildResponse, SwitchSkillBuildEncoder>();
        services.AddCommandHandler<LearnSkillCommand, CharacterSkillLearningResult, LearnSkillCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<SellItemCommand, ItemSaleResult, SellItemCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<RepairItemCommand, EquipmentRepairResult, RepairItemCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<NpcDisjointItemCommand, EquipmentDisjointResult,
            NpcDisjointItemCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<PortableDisjointItemCommand, EquipmentDisjointResult,
            PortableDisjointItemCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<ReinforceItemCommand, EquipmentReinforcementResult,
            ReinforceItemCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<EnchantByBeadCommand, EquipmentEnchantResult,
            EnchantByBeadCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<MagicSealCommand, EquipmentMagicSealResult,
            MagicSealCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<OutworldCommand, EquipmentOutworldResult,
            OutworldCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<ForgingCommand, EquipmentForgingResult,
            ForgingCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<SortLockCommand, SortLockResult, SortLockCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<SortItemCommand, SortItemResult, SortItemCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<DecreaseDurabilityCommand, EquipmentDurabilityResult,
            DecreaseDurabilityCommandHandler>(CommandExecutionKind.Transactional,
            UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<DiscardAccountMaterialCommand, DiscardAccountMaterialResult, DiscardAccountMaterialCommandHandler>(CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<SelectCrystalContractCommand, bool, SelectCrystalContractCommandHandler>(CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<ConsumeSkillMaterialsCommand, MaterialConsumptionResult, ConsumeSkillMaterialsCommandHandler>(CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<ConsumeEffectMaterialsCommand, MaterialConsumptionResult, ConsumeEffectMaterialsCommandHandler>(CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<DiscardItemCommand, DeleteItemOutcome, DiscardItemCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<BuyItemCommand, BuyItemOutcome, BuyItemCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<CeraShopPurchaseCommand, CeraShopPurchaseResult, CeraShopPurchaseCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<MagicBoxBatchOpeningCommand, MagicBoxOpeningResult, MagicBoxBatchOpeningCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<MagicBoxSingleOpeningCommand, MagicBoxOpeningResult, MagicBoxSingleOpeningCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<VendingMachineCommand, VendingMachineResult, VendingMachineCommandHandler>(CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddSingleton<LotteryOpeningDelivery>();
        services.AddCommandHandler<LotteryOpeningCommand, LotteryOpeningResult, LotteryOpeningCommandHandler>(CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<PackageOpeningCommand, PackageOpeningResult, PackageOpeningCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<AvatarPackageOpeningCommand, PackageOpeningResult, AvatarPackageOpeningCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.AddCommandHandler<QueryShopPurchaseCountsCommand, NpcShopPurchaseCountsResult, QueryShopPurchaseCountsCommandHandler>(
            CommandExecutionKind.Direct, UserAccessMode.Shared, UserAccessMode.Shared);
        // A tutorial step's items settle together, and through the same container everything else
        // writes through.
        services.AddCommandHandler<
            GrantTutorialRewardCommand, TutorialRewardResult, GrantTutorialRewardCommandHandler>(
            CommandExecutionKind.Transactional);
        // Taking a piece off a room's floor: the claim on the floor and the row in the bag are one
        // change, and a piece that left the floor without arriving anywhere is the thing to avoid.
        services.AddCommandHandler<
            PickUpGroundDropCommand, GroundPickupResult, PickUpGroundDropCommandHandler>(
            CommandExecutionKind.Transactional);
        // The tutorial's level: one row, written once, and the client is told what it now stands at.
        services.AddCommandHandler<
            GrantTutorialLevelCommand, TutorialLevelUpResult, GrantTutorialLevelCommandHandler>(
            CommandExecutionKind.Transactional);
        // Turning a card over now hands the piece behind it to the character's bag, which is the
        // moment the note left here foresaw: a claim on a run alone is memory, and a row in a bag
        // is not. Transactional from that change onwards, on the same reading the ground pick-up
        // was given -- the claim and the row are one change, and a position marked taken whose
        // piece never arrived anywhere is the thing to avoid. The bag's own container refuses a
        // write with no boundary open, so this is what makes the arrival possible at all.
        services.AddCommandHandler<SelectCardCommand, SelectCardResult, SelectCardCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<
            RequestPartyInviteCommand, PartyOperationResult, RequestPartyInviteCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<
            AcceptPartyInviteCommand, PartyOperationResult, AcceptPartyInviteCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<LeavePartyCommand, PartyOperationResult, LeavePartyCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<
            TransferPartyLeadershipCommand,
            PartyOperationResult,
            TransferPartyLeadershipCommandHandler>(CommandExecutionKind.Direct);
        services.AddCommandHandler<
            RemovePartyMemberCommand,
            PartyOperationResult,
            RemovePartyMemberCommandHandler>(CommandExecutionKind.Direct);
        services.AddCommandHandler<
            QueryPartyRealtimeCommand, PartyRealtimeQueryResult, QueryPartyRealtimeCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<
            SetPartyInfoCommand, PartyOperationResult, SetPartyInfoCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<
            QueryOtherCharacterDetailCommand,
            CharacterDetailQueryResult,
            QueryOtherCharacterDetailCommandHandler>(CommandExecutionKind.Direct);
    }

    /// <summary>Binds each catalogue member to the handler that consumes it.</summary>
    private static void RegisterMessageHandlers(IServiceCollection services)
    {
        services.RegisterMessageHandler<ChannelConnectHandler>(ClientMessageType.ChannelConnect);
        services.RegisterMessageHandler<ChannelScriptVersionHandler>(ClientMessageType.ChannelScriptVersion);
        services.RegisterMessageHandler<ChannelScriptHandler>(ClientMessageType.ChannelScript);
        services.RegisterMessageHandler<ChannelInfoHandler>(ClientMessageType.ChannelInfo);
        services.RegisterMessageHandler<ReturnToSelectionHandler>(ClientMessageType.GameReturnToSelection);
        services.RegisterMessageHandler<GameExitHandler>(ClientMessageType.GameExit);
        services.RegisterMessageHandler<PremiumServiceUsageReportHandler>(ClientMessageType.GamePremiumServiceUsageReport);
        services.RegisterMessageHandler<RecoverStaminaHandler>(ClientMessageType.GameRecoverStamina);
        services.RegisterMessageHandler<GameLoginHandler>(ClientMessageType.GameLogin);
        services.RegisterMessageHandler<GetAccountCharacterRosterHandler>(
            ClientMessageType.GameGetAccountCharacterRoster);
        services.RegisterMessageHandler<GetOtherCharacterDetailHandler>(
            ClientMessageType.GameGetOtherCharacterDetail);
        services.RegisterMessageHandler<SelectCharacterHandler>(ClientMessageType.GameSelectCharacter);
        services.RegisterMessageHandler<ClientSpecStatisticHandler>(ClientMessageType.GameClientSpecStatistic);
        services.RegisterMessageHandler<ClientHardwareDetailHandler>(ClientMessageType.GameClientHardwareDetail);
        services.RegisterMessageHandler<ClientSpecProfileHandler>(ClientMessageType.GameClientSpecProfile);
        services.RegisterMessageHandler<SessionMetricsHandler>(ClientMessageType.GameSessionMetrics);
        services.RegisterMessageHandler<AcceptQuestHandler>(ClientMessageType.GameAcceptQuest);
        services.RegisterMessageHandler<GiveUpQuestHandler>(ClientMessageType.GameGiveUpQuest);
        services.RegisterMessageHandler<SetQuestTriggerHandler>(ClientMessageType.GameSetQuestTrigger);
        services.RegisterMessageHandler<FinishQuestHandler>(ClientMessageType.GameFinishQuest);
        services.RegisterMessageHandler<TaggedIdReportHandler>(ClientMessageType.GameTaggedIdReport);
        services.RegisterMessageHandler<RosterFollowUpHandler>(ClientMessageType.GameRosterFollowUp);
        services.RegisterMessageHandler<CheckCharacterNameHandler>(ClientMessageType.GameCheckCharacterName);
        services.RegisterMessageHandler<CreateCharacterHandler>(ClientMessageType.GameCreateCharacter);
        services.RegisterMessageHandler<DeleteCharacterHandler>(ClientMessageType.GameDeleteCharacter);
        services.RegisterMessageHandler<MercenaryInfoHandler>(ClientMessageType.GameMercenaryInfo);
        services.RegisterMessageHandler<HiddenCharacterInfoHandler>(ClientMessageType.GameHiddenCharacterInfo);
        services.AddCommandHandler<SaveCharacterOptionCommand, bool, SaveCharacterOptionCommandHandler>(CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.RegisterMessageHandler<SaveCharacterOptionHandler>(ClientMessageType.GameSaveCharacterOption);
        services.AddGameCmdMessageDecoder<SaveCharacterOptionDecoder>(0x01C0, ClientMessageType.GameSaveCharacterOption);
        services.RegisterMessageHandler<SaveGameOption1Handler>(ClientMessageType.GameSaveGameOption1);
        services.RegisterMessageHandler<ContentsPlayInfoHandler>(ClientMessageType.GameContentsPlayInfo);
        services.RegisterMessageHandler<ConnectionCheckHandler>(ClientMessageType.GameConnectionCheck);

        services.RegisterMessageHandler<SetUdpIpPortHandler>(ClientMessageType.GameSetUdpIpPort);
        services.RegisterMessageHandler<RequestPeerHandler>(ClientMessageType.GameRequestPeer);
        services.RegisterMessageHandler<ResponsePeerHandler>(ClientMessageType.GameResponsePeer);
        services.RegisterMessageHandler<LeavePartyHandler>(ClientMessageType.GameLeaveParty);
        services.RegisterMessageHandler<RemovePartyMemberHandler>(
            ClientMessageType.GameRemovePartyMember);
        services.RegisterMessageHandler<ChangePartyLeaderHandler>(
            ClientMessageType.GameChangePartyLeader);
        services.RegisterMessageHandler<CallPartyMemberRealtimeInfoHandler>(
            ClientMessageType.GameCallPartyMemberRealtimeInfo);
        services.RegisterMessageHandler<SetPartyInfoHandler>(ClientMessageType.GameSetPartyInfo);

        // One handler, the remaining wire keys in the map-entry report family.
        services.RegisterMessageHandler<MapEntryReportHandler>(ClientMessageType.GameCeraQuery);
        services.RegisterMessageHandler<NoticeSeenHandler>(ClientMessageType.GameInformNoticeSecond);
        services.RegisterMessageHandler<NoticeSeenHandler>(ClientMessageType.GameNoticeSeen);
        services.AddGameCmdMessageDecoder<NoticeSeenDecoder>(0x01DE, ClientMessageType.GameNoticeSeen);
        services.AddCommandHandler<MarkNoticeSeenCommand, bool, MarkNoticeSeenCommandHandler>(
            CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive);
        services.RegisterMessageHandler<SecurityStatusHandler>(ClientMessageType.GameSecurityStatus);
        services.RegisterMessageHandler<MapEntryReportHandler>(ClientMessageType.GameRuntimeTingStatistic);
        services.RegisterMessageHandler<EnterSelectDungeonHandler>(ClientMessageType.GameEnterSelectDungeon);
        services.RegisterMessageHandler<SelectDungeonHandler>(ClientMessageType.GameSelectDungeon);
        services.RegisterMessageHandler<FinishLoadingHandler>(ClientMessageType.GameFinishLoading);
        services.RegisterMessageHandler<MoveMapHandler>(ClientMessageType.GameMoveMap);
        services.RegisterMessageHandler<StoryPauseHandler>(ClientMessageType.GameStoryPause);
        services.RegisterMessageHandler<UseRevivalCoinHandler>(ClientMessageType.GameUseRevivalCoin);
        services.RegisterMessageHandler<CharacterDeathHandler>(ClientMessageType.GameCharacterDeath);
        services.RegisterMessageHandler<MonsterDeathHandler>(ClientMessageType.GameMonsterDeath);
        services.RegisterMessageHandler<GetItemHandler>(ClientMessageType.GameGetItem);
        services.RegisterMessageHandler<TutorialLevelUpHandler>(ClientMessageType.GameTutorialLevelUp);
        services.RegisterMessageHandler<SetPlayResultHandler>(ClientMessageType.GameSetPlayResult);
        services.RegisterMessageHandler<BossDieCheckHandler>(ClientMessageType.GameBossDieCheck);
        services.RegisterMessageHandler<ScoreScrollStateHandler>(
            ClientMessageType.GameScoreScrollState);
        services.RegisterMessageHandler<CardSelectRightStateHandler>(
            ClientMessageType.GameCardSelectRightState);
        services.RegisterMessageHandler<SelectCardHandler>(ClientMessageType.GameSelectCard);
        services.RegisterMessageHandler<EplpCommandHandler>(ClientMessageType.GameEplpCommand);
        services.RegisterMessageHandler<GiveUpGameHandler>(ClientMessageType.GameGiveUpGame);
        services.RegisterMessageHandler<BackToVillageHandler>(ClientMessageType.GameBackToVillage);

        // Known reports that this server intentionally does not answer. A durability request is
        // not one of them: it changes a persisted equipment instance and has its own CMD reply.
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameUseSkill);
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameSyncItemSpace);
        services.RegisterMessageHandler<SortLockHandler>(ClientMessageType.GameSortLock);
        services.RegisterMessageHandler<SortLockHandler>(ClientMessageType.GameSortUnlock);
        services.RegisterMessageHandler<SortItemHandler>(ClientMessageType.GameSortItem);
        services.RegisterMessageHandler<DecreaseDurabilityHandler>(ClientMessageType.GameDecreaseDurability);
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameOverflowInfo);
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameLogDomainConnect);
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameLogConnectProcess);
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameCharacterStatistic);
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameRequestBlacklist);
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameFarmEventAction);
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GamePi);
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameRequestIgaVersion);
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameFpsPerformanceStatistic);
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameFrameLagPerDungeon);
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameLagStatistics);
        services.RegisterMessageHandler<MoveItemSpaceHandler>(ClientMessageType.GameMoveItemSpace);
        services.RegisterMessageHandler<UseStackableHandler>(ClientMessageType.GameUseStackable);
        services.RegisterMessageHandler<RenameCreatureHandler>(ClientMessageType.GameRenameCreature);
        services.RegisterMessageHandler<HatchCreatureHandler>(ClientMessageType.GameHatchCreature);
        services.RegisterMessageHandler<CreatureResponseReportHandler>(ClientMessageType.GameCreatureResponseReport);
        services.RegisterMessageHandler<CreatureScriptMessageReportHandler>(ClientMessageType.GameCreatureScriptMessage);
        services.RegisterMessageHandler<QuestClearCouponHandler>(ClientMessageType.GameQuestClearCoupon);
        services.RegisterMessageHandler<BuyItemHandler>(ClientMessageType.GameBuyItem);
        services.RegisterMessageHandler<CeraShopPurchaseHandler>(ClientMessageType.GameCeraShopPurchase);
        services.RegisterMessageHandler<MagicBoxBatchOpeningHandler>(ClientMessageType.GameMagicBoxBatchOpening);
        services.RegisterMessageHandler<MagicBoxSingleOpeningHandler>(ClientMessageType.GameMagicBoxSingleOpening);
        services.RegisterMessageHandler<VendingMachineHandler>(ClientMessageType.GameVendingMachine);
        services.RegisterMessageHandler<LotteryOpeningHandler>(ClientMessageType.GameLotteryOpening);
        services.RegisterMessageHandler<PackageOpeningHandler>(ClientMessageType.GamePackageOpening);
        services.RegisterMessageHandler<AvatarPackageOpeningHandler>(ClientMessageType.GameAvatarPackageOpening);
        services.RegisterMessageHandler<GenCeraTicketHandler>(ClientMessageType.GameGenCeraTicket);
        services.RegisterMessageHandler<LearnSkillHandler>(ClientMessageType.GameLearnSkill);
        services.RegisterMessageHandler<SellItemHandler>(ClientMessageType.GameSellItem);
        services.RegisterMessageHandler<RepairItemHandler>(ClientMessageType.GameRepairItem);
        services.RegisterMessageHandler<DisjointItemHandler>(ClientMessageType.GameDisjointItem);
        services.TryAddSingleton<ReinforceItemReplayGuard>();
        services.RegisterMessageHandler<ReinforceItemHandler>(ClientMessageType.GameReinforceItem);
        services.TryAddSingleton<EnchantByBeadReplayGuard>();
        services.RegisterMessageHandler<EnchantByBeadHandler>(ClientMessageType.GameEnchantByBead);
        services.TryAddSingleton<MagicSealReplayGuard>();
        services.RegisterMessageHandler<MagicSealHandler>(ClientMessageType.GameUnsealRandomOption);
        services.RegisterMessageHandler<MagicSealHandler>(ClientMessageType.GameChangeRandomOption);
        services.TryAddSingleton<OutworldReplayGuard>();
        services.RegisterMessageHandler<OutworldHandler>(ClientMessageType.GamePurifyItem);
        services.RegisterMessageHandler<OutworldHandler>(ClientMessageType.GameInvestAmplifyOption);
        services.TryAddSingleton<ForgingReplayGuard>();
        services.RegisterMessageHandler<ForgingHandler>(ClientMessageType.GameSeparateUpgrade);
        services.RegisterMessageHandler<CrystalContractHandler>(ClientMessageType.GameCrystalContract);
        services.RegisterMessageHandler<DeleteItemHandler>(ClientMessageType.GameDeleteItem);
        services.RegisterMessageHandler<ShopPurchaseCountHandler>(ClientMessageType.GameShopPurchaseCount);

        // Position and area: the only inputs that move a published character, and so the only
        // way the town its next entry reports can change.
        services.RegisterMessageHandler<SetUserPositionHandler>(ClientMessageType.GameSetUserPosition);
        services.RegisterMessageHandler<SetUserAreaHandler>(ClientMessageType.GameSetUserArea);
    }

    /// <summary>
    /// The inbound routing table: one line per protocol, binding its wire key to its decoder and
    /// declaring which catalogue members that decoder can produce.
    /// </summary>
    /// <remarks>
    /// Registration is unconditional on purpose. The declared set is what the startup coverage
    /// check reads, and that check must hold whether or not this process is listening.
    ///
    /// Most lines declare the one catalogue member named after their wire key. GET_USERINFO does
    /// not: type 0x0008 produces the account roster request, which carries a synthesised catalogue
    /// value with no wire number of its own.
    ///
    /// Every client-originated Game line below is explicitly CMD. The selector remains part of the
    /// wire key, so an unregistered selector/type pair matches nothing and is silently ignored.
    /// </remarks>
    private static void RegisterInboundDecoders(IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GameLoginMessageDecoder>(
            GameProtocol.LoginType, ClientMessageType.GameLogin);
        services.AddGameCmdMessageDecoder<ReturnToSelectionMessageDecoder>(
            GameProtocol.ReturnToSelectionType, ClientMessageType.GameReturnToSelection);
        services.AddGameCmdMessageDecoder<GameExitMessageDecoder>(
            GameProtocol.ExitType, ClientMessageType.GameExit);
        services.AddGameCmdMessageDecoder<SelectCharacterMessageDecoder>(
            GameProtocol.SelectCharacterType, ClientMessageType.GameSelectCharacter);
        services.AddGameCmdMessageDecoder<GetUserInfoMessageDecoder>(
            GameProtocol.GetUserInfoType,
            ClientMessageType.GameGetAccountCharacterRoster,
            ClientMessageType.GameGetOtherCharacterDetail);
        services.AddGameCmdMessageDecoder<ClientSpecStatisticMessageDecoder>(
            GameProtocol.ClientSpecStatisticType, ClientMessageType.GameClientSpecStatistic);
        services.AddGameCmdMessageDecoder<ClientHardwareDetailMessageDecoder>(
            GameProtocol.ClientHardwareDetailType, ClientMessageType.GameClientHardwareDetail);
        services.AddGameCmdMessageDecoder<ClientSpecProfileMessageDecoder>(
            GameProtocol.ClientSpecProfileType, ClientMessageType.GameClientSpecProfile);
        services.AddGameCmdMessageDecoder<SessionMetricsMessageDecoder>(
            GameProtocol.SessionMetricsType, ClientMessageType.GameSessionMetrics);
        services.AddGameCmdMessageDecoder<AcceptQuestMessageDecoder>(
            GameProtocol.AcceptQuestType, ClientMessageType.GameAcceptQuest);
        services.AddGameCmdMessageDecoder<GiveUpQuestMessageDecoder>(
            GameProtocol.GiveUpQuestType, ClientMessageType.GameGiveUpQuest);
        services.AddGameCmdMessageDecoder<SetQuestTriggerMessageDecoder>(
            GameProtocol.SetQuestTriggerType, ClientMessageType.GameSetQuestTrigger);
        services.AddGameCmdMessageDecoder<FinishQuestMessageDecoder>(
            GameProtocol.FinishQuestType, ClientMessageType.GameFinishQuest);
        services.AddGameCmdMessageDecoder<TaggedIdReportMessageDecoder>(
            GameProtocol.TaggedIdReportType, ClientMessageType.GameTaggedIdReport);
        services.AddGameCmdMessageEncoder<AcceptQuestAcknowledgement, AcceptQuestAcknowledgementEncoder>();
        services.AddGameCmdMessageEncoder<GiveUpQuestAcknowledgement, GiveUpQuestAcknowledgementEncoder>();
        services.AddGameCmdMessageEncoder<SetQuestTriggerAcknowledgement, SetQuestTriggerAcknowledgementEncoder>();
        services.AddGameCmdMessageEncoder<FinishQuestAcknowledgement, FinishQuestAcknowledgementEncoder>();
        services.AddGameCmdMessageEncoder<TutorialFlagAcknowledgement, TutorialFlagAcknowledgementEncoder>();
        services.AddGameNotiMessageEncoder<StoryPauseNotification, StoryPauseEncoder>();
        services.AddGameNotiMessageEncoder<ExperienceNotification, ExperienceEncoder>();
        services.AddGameNotiMessageEncoder<MonsterDeathNotification, MonsterDeathEncoder>();
        services.AddGameCmdMessageDecoder<PremiumServiceUsageReportDecoder>(0x036F, ClientMessageType.GamePremiumServiceUsageReport);
        services.AddGameCmdMessageDecoder<RecoverStaminaDecoder>(0x0009, ClientMessageType.GameRecoverStamina);
        services.AddGameCmdMessageEncoder<RecoverStaminaResponse, RecoverStaminaEncoder>();
        services.AddGameNotiMessageEncoder<WeaknessProjectionNotification, WeaknessProjectionEncoder>();
        services.AddGameNotiMessageEncoder<CharacterLifeStateNotification, CharacterLifeStateEncoder>();
        services.AddGameCmdMessageEncoder<CharacterDeathResponse, CharacterDeathResponseEncoder>();
        services.AddGameCmdMessageEncoder<UseRevivalCoinResponse, UseRevivalCoinEncoder>();
        services.AddGameNotiMessageEncoder<GroundPickupNotification, GroundPickupEncoder>();
        services.AddGameNotiMessageEncoder<
            GroundGoldPickupNotification, GroundGoldPickupEncoder>();
        services.AddGameNotiMessageEncoder<EnableClearDungeonNotification, EnableClearDungeonEncoder>();
        services.AddGameNotiMessageEncoder<SecretShopNpcNotification, SecretShopNpcEncoder>();
        services.AddGameNotiMessageEncoder<PlayResultNotification, PlayResultEncoder>();
        services.AddGameNotiMessageEncoder<ClearDungeonRewardNotification, ClearDungeonRewardEncoder>();
        services.AddGameCmdMessageDecoder<RosterFollowUpMessageDecoder>(
            GameProtocol.RosterFollowUpType, ClientMessageType.GameRosterFollowUp);
        services.AddGameCmdMessageDecoder<CheckCharacterNameMessageDecoder>(
            GameProtocol.CheckCharacterNameType, ClientMessageType.GameCheckCharacterName);
        services.AddGameCmdMessageDecoder<CreateCharacterMessageDecoder>(
            GameProtocol.CreateCharacterType, ClientMessageType.GameCreateCharacter);
        services.AddGameCmdMessageDecoder<DeleteCharacterMessageDecoder>(
            GameProtocol.DeleteCharacterType, ClientMessageType.GameDeleteCharacter);
        services.AddGameCmdMessageDecoder<MercenaryInfoMessageDecoder>(
            GameProtocol.MercenaryInfoType, ClientMessageType.GameMercenaryInfo);
        services.AddGameCmdMessageDecoder<SaveGameOption1MessageDecoder>(
            GameProtocol.SaveGameOption1Type, ClientMessageType.GameSaveGameOption1);
        services.AddGameCmdMessageDecoder<ContentsPlayInfoMessageDecoder>(
            GameProtocol.ContentsPlayInfoType, ClientMessageType.GameContentsPlayInfo);
        services.AddGameCmdMessageDecoder<ConnectionCheckMessageDecoder>(
            GameProtocol.ConnectionCheckType, ClientMessageType.GameConnectionCheck);
        services.AddGameCmdMessageDecoder<SetUdpIpPortDecoder>(
            GameProtocol.SetUdpIpPortType, ClientMessageType.GameSetUdpIpPort);
        services.AddGameCmdMessageDecoder<RequestPeerDecoder>(
            GameProtocol.RequestPeerType, ClientMessageType.GameRequestPeer);
        services.AddGameCmdMessageDecoder<ResponsePeerDecoder>(
            GameProtocol.ResponsePeerType, ClientMessageType.GameResponsePeer);
        services.AddGameCmdMessageDecoder<LeavePartyDecoder>(
            GameProtocol.LeavePartyType, ClientMessageType.GameLeaveParty);
        services.AddGameCmdMessageDecoder<RemovePartyMemberDecoder>(
            GameProtocol.RemovePartyMemberType, ClientMessageType.GameRemovePartyMember);
        services.AddGameCmdMessageDecoder<ChangePartyLeaderDecoder>(
            GameProtocol.ChangePartyLeaderType, ClientMessageType.GameChangePartyLeader);
        services.AddGameCmdMessageDecoder<CallPartyMemberRealtimeInfoDecoder>(
            GameProtocol.CallPartyMemberRealtimeInfoType,
            ClientMessageType.GameCallPartyMemberRealtimeInfo);
        services.AddGameCmdMessageDecoder<SetPartyInfoDecoder>(
            GameProtocol.SetPartyInfoType,
            ClientMessageType.GameSetPartyInfo);
        services.AddGameCmdMessageDecoder<CeraQueryDecoder>(
            GameProtocol.CeraQueryType, ClientMessageType.GameCeraQuery);
        services.AddGameCmdMessageDecoder<SetUserPositionDecoder>(
            GameProtocol.SetUserPositionType, ClientMessageType.GameSetUserPosition);
        services.AddGameCmdMessageDecoder<SetUserAreaDecoder>(
            GameProtocol.SetUserAreaType, ClientMessageType.GameSetUserArea);
        services.AddGameCmdMessageDecoder<InformNoticeSecondDecoder>(
            GameProtocol.InformNoticeSecondType, ClientMessageType.GameInformNoticeSecond);
        services.AddGameCmdMessageDecoder<SecurityStatusDecoder>(
            GameProtocol.SecurityStatusType, ClientMessageType.GameSecurityStatus);
        services.AddGameCmdMessageDecoder<RuntimeTingStatisticDecoder>(
            GameProtocol.RuntimeTingStatisticType, ClientMessageType.GameRuntimeTingStatistic);
        services.AddGameCmdMessageDecoder<EnterSelectDungeonDecoder>(
            GameProtocol.EnterSelectDungeonType, ClientMessageType.GameEnterSelectDungeon);
        services.AddGameCmdMessageDecoder<SelectDungeonDecoder>(
            GameProtocol.SelectDungeonType, ClientMessageType.GameSelectDungeon);
        services.AddGameCmdMessageDecoder<FinishLoadingDecoder>(
            GameProtocol.FinishLoadingType, ClientMessageType.GameFinishLoading);
        services.AddGameCmdMessageDecoder<MoveMapDecoder>(
            GameProtocol.MoveMapType, ClientMessageType.GameMoveMap);
        services.AddGameCmdMessageDecoder<StoryPauseDecoder>(
            GameProtocol.StoryPauseType, ClientMessageType.GameStoryPause);
        services.AddGameCmdMessageDecoder<UseRevivalCoinDecoder>(0x0029, ClientMessageType.GameUseRevivalCoin);
        services.AddGameCmdMessageDecoder<CharacterDeathDecoder>(GameProtocol.CharacterDeathType, ClientMessageType.GameCharacterDeath);
        services.AddGameCmdMessageDecoder<MonsterDeathDecoder>(
            GameProtocol.MonsterDeathType, ClientMessageType.GameMonsterDeath);
        services.AddGameCmdMessageDecoder<GetItemDecoder>(
            GameProtocol.GetItemType, ClientMessageType.GameGetItem);
        services.AddGameCmdMessageDecoder<TutorialLevelUpDecoder>(
            GameProtocol.TutorialLevelUpType, ClientMessageType.GameTutorialLevelUp);
        services.AddGameCmdMessageDecoder<SetPlayResultDecoder>(
            GameProtocol.SetPlayResultType, ClientMessageType.GameSetPlayResult);
        services.AddGameCmdMessageDecoder<BossDieCheckDecoder>(
            GameProtocol.BossDieCheckType, ClientMessageType.GameBossDieCheck);
        services.AddGameCmdMessageDecoder<ScoreScrollStateDecoder>(
            GameProtocol.ScoreScrollStateType, ClientMessageType.GameScoreScrollState);
        services.AddGameCmdMessageDecoder<CardSelectRightStateDecoder>(
            GameProtocol.CardSelectRightStateType, ClientMessageType.GameCardSelectRightState);
        services.AddGameCmdMessageDecoder<SelectCardDecoder>(
            GameProtocol.SelectCardType, ClientMessageType.GameSelectCard);
        services.AddGameCmdMessageDecoder<EplpCommandDecoder>(
            GameProtocol.EplpCommandType, ClientMessageType.GameEplpCommand);
        services.AddGameCmdMessageDecoder<GiveUpGameDecoder>(
            GameProtocol.GiveUpGameType, ClientMessageType.GameGiveUpGame);
        services.AddGameCmdMessageDecoder<BackToVillageDecoder>(
            GameProtocol.BackToVillageType, ClientMessageType.GameBackToVillage);
        services.AddGameCmdMessageDecoder<CharacterStatisticDecoder>(
            GameProtocol.CharacterStatisticType, ClientMessageType.GameCharacterStatistic);
        services.AddGameCmdMessageDecoder<RequestBlacklistDecoder>(
            GameProtocol.RequestBlacklistType, ClientMessageType.GameRequestBlacklist);
        services.AddGameCmdMessageDecoder<FarmEventActionDecoder>(
            GameProtocol.FarmEventActionType, ClientMessageType.GameFarmEventAction);
        services.AddGameCmdMessageDecoder<PiDecoder>(
            GameProtocol.PiType, ClientMessageType.GamePi);
        services.AddGameCmdMessageDecoder<RequestIgaVersionDecoder>(
            GameProtocol.RequestIgaVersionType, ClientMessageType.GameRequestIgaVersion);
        services.AddGameCmdMessageDecoder<FpsPerformanceStatisticDecoder>(
            GameProtocol.FpsPerformanceStatisticType, ClientMessageType.GameFpsPerformanceStatistic);
        services.AddGameCmdMessageDecoder<FrameLagPerDungeonDecoder>(
            GameProtocol.FrameLagPerDungeonType, ClientMessageType.GameFrameLagPerDungeon);
        services.AddGameCmdMessageDecoder<LagStatisticsDecoder>(
            GameProtocol.LagStatisticsType, ClientMessageType.GameLagStatistics);
        services.AddGameCmdMessageDecoder<UseSkillDecoder>(
            GameProtocol.UseSkillType, ClientMessageType.GameUseSkill);
        services.AddGameCmdMessageDecoder<SyncItemSpaceDecoder>(
            GameProtocol.SyncItemSpaceType, ClientMessageType.GameSyncItemSpace);
        services.AddGameCmdMessageDecoder<SortLockDecoder>(GameProtocol.SortLockType, ClientMessageType.GameSortLock);
        services.AddGameCmdMessageDecoder<SortUnlockDecoder>(GameProtocol.SortUnlockType, ClientMessageType.GameSortUnlock);
        services.AddGameCmdMessageDecoder<SortItemMessageDecoder>(GameProtocol.SortItemType, ClientMessageType.GameSortItem);
        services.AddGameCmdMessageDecoder<DecreaseDurabilityMessageDecoder>(
            GameProtocol.DecreaseDurabilityType, ClientMessageType.GameDecreaseDurability);
        services.AddGameCmdMessageDecoder<OverflowInfoDecoder>(
            GameProtocol.OverflowInfoType, ClientMessageType.GameOverflowInfo);
        services.AddGameCmdMessageDecoder<LogDomainConnectDecoder>(
            GameProtocol.LogDomainConnectType, ClientMessageType.GameLogDomainConnect);
        services.AddGameCmdMessageDecoder<LogConnectProcessDecoder>(
            GameProtocol.LogConnectProcessType, ClientMessageType.GameLogConnectProcess);
        services.AddGameCmdMessageDecoder<MoveItemSpaceDecoder>(
            GameProtocol.MoveItemSpaceType, ClientMessageType.GameMoveItemSpace);
        services.AddGameCmdMessageDecoder<LearnSkillMessageDecoder>(GameProtocol.LearnSkillType, ClientMessageType.GameLearnSkill);
        services.AddGameCmdMessageDecoder<SellItemMessageDecoder>(GameProtocol.SellItemType, ClientMessageType.GameSellItem);
        services.AddGameCmdMessageDecoder<RepairItemMessageDecoder>(GameProtocol.RepairItemType, ClientMessageType.GameRepairItem);
        services.AddGameCmdMessageDecoder<DisjointItemMessageDecoder>(
            GameProtocol.DisjointItemType, ClientMessageType.GameDisjointItem);
        services.AddGameCmdMessageDecoder<ReinforceItemMessageDecoder>(
            GameProtocol.ReinforceItemType, ClientMessageType.GameReinforceItem);
        services.AddGameCmdMessageDecoder<EnchantByBeadDecoder>(
            GameProtocol.EnchantByBeadType, ClientMessageType.GameEnchantByBead);
        services.AddGameCmdMessageDecoder<UnsealRandomOptionDecoder>(
            GameProtocol.UnsealRandomOptionType, ClientMessageType.GameUnsealRandomOption);
        services.AddGameCmdMessageDecoder<ChangeRandomOptionDecoder>(
            GameProtocol.ChangeRandomOptionType, ClientMessageType.GameChangeRandomOption);
        services.AddGameCmdMessageDecoder<PurifyItemDecoder>(
            GameProtocol.PurifyItemType, ClientMessageType.GamePurifyItem);
        services.AddGameCmdMessageDecoder<InvestAmplifyOptionDecoder>(
            GameProtocol.InvestAmplifyOptionType, ClientMessageType.GameInvestAmplifyOption);
        services.AddGameCmdMessageDecoder<ForgingDecoder>(
            GameProtocol.SeparateUpgradeType, ClientMessageType.GameSeparateUpgrade);
        services.AddGameCmdMessageDecoder<CrystalContractDecoder>(0x0218, ClientMessageType.GameCrystalContract);
        services.AddGameCmdMessageDecoder<DeleteItemMessageDecoder>(
            GameProtocol.DeleteItemType, ClientMessageType.GameDeleteItem);
        services.AddGameCmdMessageDecoder<BuyItemMessageDecoder>(
            GameProtocol.BuyItemType, ClientMessageType.GameBuyItem);
        services.AddGameCmdMessageDecoder<CeraShopPurchaseMessageDecoder>(0x0040, ClientMessageType.GameCeraShopPurchase);
        services.AddGameCmdMessageDecoder<MagicBoxBatchOpeningDecoder>(0x0450, ClientMessageType.GameMagicBoxBatchOpening);
        services.AddGameCmdMessageDecoder<MagicBoxSingleOpeningDecoder>(0x00D0, ClientMessageType.GameMagicBoxSingleOpening);
        services.AddGameCmdMessageDecoder<VendingMachineDecoder>(0x00D7, ClientMessageType.GameVendingMachine);
        services.AddGameCmdMessageDecoder<LotteryOpeningDecoder>(0x001B, ClientMessageType.GameLotteryOpening);
        services.AddGameCmdMessageDecoder<PackageOpeningDecoder>(0x00A0, ClientMessageType.GamePackageOpening);
        services.AddGameCmdMessageDecoder<AvatarPackageOpeningDecoder>(0x0207, ClientMessageType.GameAvatarPackageOpening);
        services.AddGameCmdMessageDecoder<GenCeraTicketMessageDecoder>(0x0041, ClientMessageType.GameGenCeraTicket);
        services.AddGameCmdMessageDecoder<ShopPurchaseCountDecoder>(
            GameProtocol.ShopPurchaseCountType, ClientMessageType.GameShopPurchaseCount);
        services.AddGameCmdMessageDecoder<UseStackableDecoder>(
            GameProtocol.UseStackableType, ClientMessageType.GameUseStackable);
        services.AddGameCmdMessageDecoder<HatchCreatureMessageDecoder>(
            CreaturePayloadCodec.HatchCommandType, ClientMessageType.GameHatchCreature);
        services.AddGameCmdMessageDecoder<RenameCreatureMessageDecoder>(
            CreaturePayloadCodec.RenameCommandType, ClientMessageType.GameRenameCreature);
        services.AddGameCmdMessageDecoder<CreatureResponseReportDecoder>(
            0x0065, ClientMessageType.GameCreatureResponseReport);
        services.AddGameCmdMessageDecoder<CreatureScriptMessageReportDecoder>(
            0x007A, ClientMessageType.GameCreatureScriptMessage);
        services.AddGameCmdMessageDecoder<QuestClearCouponDecoder>(
            GameProtocol.QuestClearCouponType, ClientMessageType.GameQuestClearCoupon);
        services.AddGameCmdMessageDecoder<HiddenCharacterInfoMessageDecoder>(
            GameProtocol.HiddenCharacterInfoType, ClientMessageType.GameHiddenCharacterInfo);
        services.AddChannelMessageDecoder<ChannelScriptVersionMessageDecoder>(
            ChannelProtocol.ScriptVersionMessageNumber, ClientMessageType.ChannelScriptVersion);
        services.AddChannelMessageDecoder<ChannelScriptMessageDecoder>(
            ChannelProtocol.ScriptMessageNumber, ClientMessageType.ChannelScript);
        services.AddChannelMessageDecoder<ChannelConnectMessageDecoder>(
            ChannelProtocol.ConnectMessageNumber, ClientMessageType.ChannelConnect);
        services.AddChannelMessageDecoder<ChannelInfoMessageDecoder>(
            ChannelProtocol.InfoMessageNumber, ClientMessageType.ChannelInfo);
    }

    /// <summary>
    /// The outbound routing table: one line per message, binding it to its encoder and to the
    /// endpoint allowed to receive it.
    /// </summary>
    /// <remarks>
    /// Keyed by message CLR type rather than by catalogue member. An outbound message is built by
    /// server code, so its type is known at the call site.
    ///
    /// The endpoint column is the whole endpoint rule. It used to be nine `when` clauses spliced
    /// into the encoding switch, where no reader could check the routing without reading the bytes.
    /// </remarks>
    private static void RegisterOutboundEncoders(IServiceCollection services)
    {
        StateSynchronizationEncoderRegistration.AddStateSynchronizationEncoders(services);
        SupplementalSynchronizationEncoderRegistration.AddSupplementalSynchronizationEncoders(services);
        services.AddServerMessageEncoder<ChannelConnectResponse, ChannelConnectResponseEncoder>(
            NetworkEndpointKind.Channel);
        services.AddServerMessageEncoder<ChannelScriptVersionResponse, ChannelScriptVersionResponseEncoder>(
            NetworkEndpointKind.Channel);
        services.AddServerMessageEncoder<ChannelScriptResponse, ChannelScriptResponseEncoder>(
            NetworkEndpointKind.Channel);
        services.AddServerMessageEncoder<ChannelInfoResponse, ChannelInfoResponseEncoder>(
            NetworkEndpointKind.Channel);
        services.AddGameCmdMessageEncoder<GameExitResponse, GameExitResponseEncoder>();
        services.AddGameCmdMessageEncoder<ReturnToSelectionResponse, ReturnToSelectionResponseEncoder>();
        services.AddGameNotiMessageEncoder<GameInitialConnectionNotice, GameInitialConnectionNoticeEncoder>();
        services.AddGameCmdMessageEncoder<GameLoginResponse, GameLoginResponseEncoder>();
        services.AddGameNotiMessageEncoder<AccountCharacterRosterResponse, AccountCharacterRosterResponseEncoder>();
        services.AddGameNotiMessageEncoder<
            OtherCharacterDetailResponse,
            OtherCharacterDetailResponseEncoder>();
        services.AddGameNotiMessageEncoder<HonorLevelInfoNotice, HonorLevelInfoNoticeEncoder>();
        services.AddGameNotiMessageEncoder<AuctionServiceStateNotice, AuctionServiceStateNoticeEncoder>();
        services.AddGameNotiMessageEncoder<InformNoticeNotice, InformNoticeNoticeEncoder>();
        services.AddGameNotiMessageEncoder<PartyInviteRequestMessage, PartyInviteRequestEncoder>();
        services.AddGameNotiMessageEncoder<PartyInviteAcceptedMessage, PartyInviteAcceptedEncoder>();
        services.AddGameNotiMessageEncoder<PartyRealtimeMessage, PartyRealtimeEncoder>();
        services.AddGameNotiMessageEncoder<PartyEndpointsMessage, PartyEndpointsEncoder>();
        services.AddGameNotiMessageEncoder<PartyRosterMessage, PartyRosterEncoder>();
        services.AddGameNotiMessageEncoder<PartySettingsMessage, PartySettingsEncoder>();
        services.AddGameCmdMessageEncoder<
            RemovePartyMemberRefusal,
            RemovePartyMemberRefusalEncoder>();
        services.AddGameCmdMessageEncoder<CheckCharacterNameResponse, CheckCharacterNameResponseEncoder>();
        services.AddGameCmdMessageEncoder<CreateCharacterResponse, CreateCharacterResponseEncoder>();
        services.AddGameCmdMessageEncoder<DeleteCharacterResponse, DeleteCharacterResponseEncoder>();
        services.AddGameCmdMessageEncoder<MercenaryInfoResponse, MercenaryInfoResponseEncoder>();
        services.AddGameCmdMessageEncoder<ContentsPlayInfoResponse, ContentsPlayInfoResponseEncoder>();
        services.AddGameCmdMessageEncoder<ConnectionCheckResponse, ConnectionCheckResponseEncoder>();
        services.AddGameNotiMessageEncoder<UserAreaNotification, UserAreaEncoder>();
        services.AddGameNotiMessageEncoder<AreaUsersNotification, AreaUsersEncoder>();
        services.AddGameNotiMessageEncoder<UserPositionNotification, UserPositionEncoder>();
        services.AddGameNotiMessageEncoder<UserStateNotification, UserStateEncoder>();
        services.AddGameNotiMessageEncoder<UdpHostNotification, UdpHostEncoder>();
        services.AddGameCmdMessageEncoder<SecurityStatusAcknowledgement, SecurityStatusAcknowledgementEncoder>();

        services.AddGameCmdMessageEncoder<EnterSelectDungeonAcknowledgement, EnterSelectDungeonAcknowledgementEncoder>();

        services.AddGameNotiMessageEncoder<EnterSelectDungeonNotification, EnterSelectDungeonEncoder>();
        services.AddGameNotiMessageEncoder<DungeonInfoNotification, DungeonInfoEncoder>();
        services.AddGameNotiMessageEncoder<StartMapNotification, StartMapEncoder>();
        services.AddGameNotiMessageEncoder<SupportTagNotification, SupportTagEncoder>();
        services.AddGameCmdMessageEncoder<FinishLoadingResponse, FinishLoadingResponseEncoder>();
        services.AddGameCmdMessageEncoder<MoveItemSpaceResponse, MoveItemSpaceResponseEncoder>();
        services.AddGameCmdMessageEncoder<UseStackableResponse, UseStackableResponseEncoder>();
        services.AddGameCmdMessageEncoder<RenameCreatureResponse, RenameCreatureResponseEncoder>();
        services.AddGameCmdMessageEncoder<HatchCreatureResponse, HatchCreatureResponseEncoder>();
        services.AddGameCmdMessageEncoder<QuestClearCouponResponse, QuestClearCouponEncoder>();
        services.AddGameCmdMessageEncoder<BuyItemResponse, BuyItemResponseEncoder>();
        services.AddGameCmdMessageEncoder<CeraShopPurchaseResponse, CeraShopPurchaseResponseEncoder>();
        services.AddGameCmdMessageEncoder<MagicBoxBatchOpeningResponse, MagicBoxBatchOpeningEncoder>();
        services.AddGameCmdMessageEncoder<MagicBoxSingleOpeningResponse, MagicBoxSingleOpeningEncoder>();
        services.AddGameCmdMessageEncoder<VendingMachineResponse, VendingMachineEncoder>();
        services.AddGameCmdMessageEncoder<LotteryOpeningResponse, LotteryOpeningEncoder>();
        services.AddGameCmdMessageEncoder<PackageOpeningResponse, PackageOpeningEncoder>();
        services.AddGameCmdMessageEncoder<AvatarPackageOpeningResponse, AvatarPackageOpeningEncoder>();
        services.AddGameCmdMessageEncoder<OpeningRewardPopup, OpeningRewardPopupEncoder>();
        services.AddGameCmdMessageEncoder<GenCeraTicketUnavailable, GenCeraTicketResponseEncoder>();
        services.AddGameNotiMessageEncoder<GameServerNoticeMessage, GameServerNoticeEncoder>();
        GameInstanceDelivery.AddEncoders(services);
        services.AddGameCmdMessageEncoder<SellItemResponse, SellItemResponseEncoder>();
        services.AddGameCmdMessageEncoder<RepairItemResponse, RepairItemResponseEncoder>();
        services.AddGameCmdMessageEncoder<DisjointItemResponse, DisjointItemResponseEncoder>();
        services.AddGameCmdMessageEncoder<ReinforceItemResponse, ReinforceItemResponseEncoder>();
        services.AddGameCmdMessageEncoder<EnchantByBeadResponse, EnchantByBeadEncoder>();
        services.AddGameCmdMessageEncoder<ChangeRandomOptionResponse, ChangeRandomOptionEncoder>();
        services.AddGameCmdMessageEncoder<PurifyItemResponse, PurifyItemEncoder>();
        services.AddGameCmdMessageEncoder<InvestAmplifyOptionResponse, InvestAmplifyOptionEncoder>();
        services.AddGameCmdMessageEncoder<ForgingResponse, ForgingEncoder>();
        services.AddGameCmdMessageEncoder<SortLockResponse, SortLockResponseEncoder>();
        services.AddGameCmdMessageEncoder<SortItemResponse, SortItemResponseEncoder>();
        services.AddGameCmdMessageEncoder<DecreaseDurabilityResponse, DecreaseDurabilityResponseEncoder>();
        services.AddGameCmdMessageEncoder<CrystalContractResponse, CrystalContractEncoder>();
        services.AddGameCmdMessageEncoder<DeleteItemResponse, DeleteItemResponseEncoder>();
        services.AddGameCmdMessageEncoder<LearnSkillResponse, LearnSkillResponseEncoder>();
        services.AddGameCmdMessageEncoder<ShopPurchaseCountResponse, ShopPurchaseCountEncoder>();
        services.AddGameCmdMessageEncoder<GetItemResponse, GetItemResponseEncoder>();
        services.AddGameCmdMessageEncoder<TutorialLevelUpResponse, TutorialLevelUpResponseEncoder>();
        services.AddGameCmdMessageEncoder<
            ScoreScrollStateResponse, ScoreScrollStateResponseEncoder>();
        services.AddGameCmdMessageEncoder<
            CardSelectRightStateResponse, CardSelectRightStateResponseEncoder>();
        services.AddGameCmdMessageEncoder<SelectCardResponse, SelectCardResponseEncoder>();
        services.AddGameCmdMessageEncoder<EplpCommandResponse, EplpCommandResponseEncoder>();
        services.AddGameCmdMessageEncoder<GiveUpGameResponse, GiveUpGameResponseEncoder>();
        services.AddGameCmdMessageEncoder<SelectDungeonRefusal, SelectDungeonRefusalEncoder>();
        services.AddGameCmdMessageEncoder<BackToVillageResponse, BackToVillageResponseEncoder>();
        services.AddGameNotiMessageEncoder<LoadingCompleteNotification, LoadingCompleteEncoder>();
        services.AddGameCmdMessageEncoder<HiddenCharacterInfoResponse, HiddenCharacterInfoResponseEncoder>();
        services.AddGameCmdMessageEncoder<RosterFollowUpResponse, RosterFollowUpResponseEncoder>();
    }
}
