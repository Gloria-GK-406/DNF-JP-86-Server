using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
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
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.BuyItem;
using Dfo.Server.Protocol.Game.ShopPurchaseCount;
using Dfo.Server.Protocol.Game.TutorialLevelUp;
using Dfo.Server.Protocol.Game.UnansweredReports;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StoryPause;
using Dfo.Server.Protocol.Game.UseStackable;
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
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.SessionMetrics;
using Dfo.Server.Protocol.Game.QuestAcceptance;
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

        services.AddGameMessageSending();
        services.TryAddSingleton<CharacterSelectionDelivery>();
        // Every notification type declared in this assembly must have a handler registered below,
        // and the server refuses to start if one does not. Declared rather than discovered: the
        // check is only worth having while something says which assemblies it covers, and this
        // line is that something.
        services.AddNotificationCatalog(typeof(ProtocolModule).Assembly);
        // BUY_ITEM adds to the client inventory. Replaying its acknowledgement could duplicate that addition.
        services.AddNotificationHandler<NpcShopPurchaseCompleted, NpcShopPurchaseCompletedHandler>(retryLimit: 0);
        services.AddNotificationHandler<ShopPurchaseCountsReported, ShopPurchaseCountsReportedHandler>(retryLimit: 0);
        services.TryAddSingleton<ICleanupNotificationDelivery, CleanupNotificationDelivery>();
        services.AddNotificationHandler<SessionCleanupAnnounced, SessionCleanupAnnouncedHandler>(
            // Nothing. A departure notice that misses somebody is corrected by the next thing that
            // redraws who is standing where, and by the time a retry ran the person it describes
            // has been gone for a second -- the notice would arrive as news about nobody.
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
        services.AddCommandHandler<GetChannelScriptVersionCommand, GetChannelScriptVersionResult, GetChannelScriptVersionCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<GetChannelScriptCommand, GetChannelScriptResult, GetChannelScriptCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<GetChannelInfoCommand, GetChannelInfoResult, GetChannelInfoCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<AcceptQuestCommand, QuestAcceptOutcome, AcceptQuestCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<SetQuestTriggerCommand, QuestTriggerOutcome, SetQuestTriggerCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<FinishQuestCommand, QuestFinishOutcome, FinishQuestCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<GrantKillExperienceCommand, KillExperienceResult, GrantKillExperienceCommandHandler>(
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
        services.AddCommandHandler<LoginGameUserCommand, LoginGameUserResult, LoginGameUserCommandHandler>(
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
        services.AddCommandHandler<BuyItemCommand, NpcShopPurchaseResult, BuyItemCommandHandler>(
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
        services.RegisterMessageHandler<GameLoginHandler>(ClientMessageType.GameLogin);
        services.RegisterMessageHandler<GetAccountCharacterRosterHandler>(
            ClientMessageType.GameGetAccountCharacterRoster);
        services.RegisterMessageHandler<SelectCharacterHandler>(ClientMessageType.GameSelectCharacter);
        services.RegisterMessageHandler<ClientSpecStatisticHandler>(ClientMessageType.GameClientSpecStatistic);
        services.RegisterMessageHandler<ClientHardwareDetailHandler>(ClientMessageType.GameClientHardwareDetail);
        services.RegisterMessageHandler<ClientSpecProfileHandler>(ClientMessageType.GameClientSpecProfile);
        services.RegisterMessageHandler<SessionMetricsHandler>(ClientMessageType.GameSessionMetrics);
        services.RegisterMessageHandler<AcceptQuestHandler>(ClientMessageType.GameAcceptQuest);
        services.RegisterMessageHandler<SetQuestTriggerHandler>(ClientMessageType.GameSetQuestTrigger);
        services.RegisterMessageHandler<FinishQuestHandler>(ClientMessageType.GameFinishQuest);
        services.RegisterMessageHandler<TaggedIdReportHandler>(ClientMessageType.GameTaggedIdReport);
        services.RegisterMessageHandler<RosterFollowUpHandler>(ClientMessageType.GameRosterFollowUp);
        services.RegisterMessageHandler<CheckCharacterNameHandler>(ClientMessageType.GameCheckCharacterName);
        services.RegisterMessageHandler<CreateCharacterHandler>(ClientMessageType.GameCreateCharacter);
        services.RegisterMessageHandler<DeleteCharacterHandler>(ClientMessageType.GameDeleteCharacter);
        services.RegisterMessageHandler<MercenaryInfoHandler>(ClientMessageType.GameMercenaryInfo);
        services.RegisterMessageHandler<HiddenCharacterInfoHandler>(ClientMessageType.GameHiddenCharacterInfo);
        services.RegisterMessageHandler<SaveGameOption1Handler>(ClientMessageType.GameSaveGameOption1);
        services.RegisterMessageHandler<ContentsPlayInfoHandler>(ClientMessageType.GameContentsPlayInfo);
        services.RegisterMessageHandler<ConnectionCheckHandler>(ClientMessageType.GameConnectionCheck);

        // One handler, six wire keys: the map-entry report family.
        services.RegisterMessageHandler<MapEntryReportHandler>(ClientMessageType.GameSetUdpIpPort);
        services.RegisterMessageHandler<MapEntryReportHandler>(ClientMessageType.GameCeraQuery);
        services.RegisterMessageHandler<MapEntryReportHandler>(ClientMessageType.GameInformNoticeSecond);
        services.RegisterMessageHandler<SecurityStatusHandler>(ClientMessageType.GameSecurityStatus);
        services.RegisterMessageHandler<MapEntryReportHandler>(ClientMessageType.GameRuntimeTingStatistic);
        services.RegisterMessageHandler<EnterSelectDungeonHandler>(ClientMessageType.GameEnterSelectDungeon);
        services.RegisterMessageHandler<SelectDungeonHandler>(ClientMessageType.GameSelectDungeon);
        services.RegisterMessageHandler<FinishLoadingHandler>(ClientMessageType.GameFinishLoading);
        services.RegisterMessageHandler<MoveMapHandler>(ClientMessageType.GameMoveMap);
        services.RegisterMessageHandler<StoryPauseHandler>(ClientMessageType.GameStoryPause);
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

        // One handler, six wire keys: the reports nothing answers. Registered so that the log can
        // tell a protocol this server has decided about from one it has never heard of.
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameUseSkill);
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameSyncItemSpace);
        services.RegisterMessageHandler<UnansweredReportHandler>(ClientMessageType.GameDecreaseDurability);
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
        services.RegisterMessageHandler<BuyItemHandler>(ClientMessageType.GameBuyItem);
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
            GameProtocol.GetUserInfoType, ClientMessageType.GameGetAccountCharacterRoster);
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
        services.AddGameCmdMessageDecoder<SetQuestTriggerMessageDecoder>(
            GameProtocol.SetQuestTriggerType, ClientMessageType.GameSetQuestTrigger);
        services.AddGameCmdMessageDecoder<FinishQuestMessageDecoder>(
            GameProtocol.FinishQuestType, ClientMessageType.GameFinishQuest);
        services.AddGameCmdMessageDecoder<TaggedIdReportMessageDecoder>(
            GameProtocol.TaggedIdReportType, ClientMessageType.GameTaggedIdReport);
        services.AddGameCmdMessageEncoder<AcceptQuestAcknowledgement, AcceptQuestAcknowledgementEncoder>();
        services.AddGameCmdMessageEncoder<SetQuestTriggerAcknowledgement, SetQuestTriggerAcknowledgementEncoder>();
        services.AddGameCmdMessageEncoder<FinishQuestAcknowledgement, FinishQuestAcknowledgementEncoder>();
        services.AddGameCmdMessageEncoder<TutorialFlagAcknowledgement, TutorialFlagAcknowledgementEncoder>();
        services.AddGameNotiMessageEncoder<StoryPauseNotification, StoryPauseEncoder>();
        services.AddGameNotiMessageEncoder<ExperienceNotification, ExperienceEncoder>();
        services.AddGameNotiMessageEncoder<MonsterDeathNotification, MonsterDeathEncoder>();
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
        services.AddGameCmdMessageDecoder<DecreaseDurabilityDecoder>(
            GameProtocol.DecreaseDurabilityType, ClientMessageType.GameDecreaseDurability);
        services.AddGameCmdMessageDecoder<OverflowInfoDecoder>(
            GameProtocol.OverflowInfoType, ClientMessageType.GameOverflowInfo);
        services.AddGameCmdMessageDecoder<LogDomainConnectDecoder>(
            GameProtocol.LogDomainConnectType, ClientMessageType.GameLogDomainConnect);
        services.AddGameCmdMessageDecoder<LogConnectProcessDecoder>(
            GameProtocol.LogConnectProcessType, ClientMessageType.GameLogConnectProcess);
        services.AddGameCmdMessageDecoder<MoveItemSpaceDecoder>(
            GameProtocol.MoveItemSpaceType, ClientMessageType.GameMoveItemSpace);
        services.AddGameCmdMessageDecoder<BuyItemMessageDecoder>(
            GameProtocol.BuyItemType, ClientMessageType.GameBuyItem);
        services.AddGameCmdMessageDecoder<ShopPurchaseCountDecoder>(
            GameProtocol.ShopPurchaseCountType, ClientMessageType.GameShopPurchaseCount);
        services.AddGameCmdMessageDecoder<UseStackableDecoder>(
            GameProtocol.UseStackableType, ClientMessageType.GameUseStackable);
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
        services.AddGameNotiMessageEncoder<HonorLevelInfoNotice, HonorLevelInfoNoticeEncoder>();
        services.AddGameNotiMessageEncoder<AuctionServiceStateNotice, AuctionServiceStateNoticeEncoder>();
        services.AddGameNotiMessageEncoder<InformNoticeNotice, InformNoticeNoticeEncoder>();
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
        services.AddGameCmdMessageEncoder<BuyItemResponse, BuyItemResponseEncoder>();
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
