using Dfo.Server.Protocol.Game.ItemSockets;
using Dfo.Server.Protocol.Game.Rental;
using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Protocol.Game.Achievements;
using Dfo.Server.Protocol.Game.CycleDungeonEntry;
using Dfo.Server.Protocol.Game.Tournament;
using Dfo.Server.Protocol.Game.TitleBook;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Protocol.Game.Joust;
using Dfo.Server.Protocol.Game.Warehouse;
using Dfo.Server.Protocol.Game.Teleport;
using Dfo.Server.Protocol.Game.Creatures;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Protocol.Game.InventorySort;
using Dfo.Server.Protocol.Game.CrystalContract;
using Dfo.Server.Protocol.Game.Mailbox;
using Dfo.Server.Protocol.Game.IncreaseStatus;
using Dfo.Server.Protocol.Game.RecoverStamina;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game.Fatigue;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Protocol.Game.BossTowerInfo;
using Dfo.Server.Protocol.Game.BossTowerMap;
using Dfo.Server.Protocol.Game.BossTowerReward;
using Dfo.Server.Protocol.Game.BossTowerContinue;
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
using Dfo.Server.Protocol.Game.CompoundItem;
using Dfo.Server.Protocol.Game.EpicCraft;
using Dfo.Server.Protocol.Game.ResetItemQuality;
using Dfo.Server.Protocol.Game.ResetRandomOption;
using Dfo.Server.Protocol.Game.AvatarCompound;
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
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.IllusionTowerStage;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.StoryPause;
using Dfo.Server.Protocol.Game.UseStackable;
using Dfo.Server.Protocol.Game.QuestClearCoupon;
using Dfo.Server.Protocol.Game.LevelUpTicket;
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

        services.AddEventDispatching();

        services.RegisterMessageHandler<JoustQueryHandler>(ClientMessageType.GameJoustInfo);
        services.RegisterMessageHandler<JoustQueryHandler>(ClientMessageType.GameJoustHistory);
        services.RegisterMessageHandler<JoustBetHandler>(ClientMessageType.GameJoustBet);
        services.AddGameCmdMessageDecoder<JoustQueryDecoder>(0x04E6,ClientMessageType.GameJoustInfo);
        services.AddGameCmdMessageDecoder<JoustQueryDecoder>(0x04E8,ClientMessageType.GameJoustHistory);
        services.AddGameCmdMessageDecoder<JoustBetDecoder>(0x04E7,ClientMessageType.GameJoustBet);
        services.AddGameCmdMessageEncoder<JoustResponse,JoustResponseEncoder>();
        services.AddGameNotiMessageEncoder<JoustStateMessage,GameNotiStateSynchronizationEncoder<JoustStateMessage>>();
        services.AddGameNotiMessageEncoder<JoustInfoMessage,GameNotiStateSynchronizationEncoder<JoustInfoMessage>>();
        services.AddGameNotiMessageEncoder<JoustBetsMessage,GameNotiStateSynchronizationEncoder<JoustBetsMessage>>();
        services.AddGameNotiMessageEncoder<JoustResultsMessage,GameNotiStateSynchronizationEncoder<JoustResultsMessage>>();
        services.AddNotificationHandler<Dfo.Server.Abstractions.Events.JoustDetailsChanged,JoustDetailsChangedHandler>();
        services.AddNotificationHandler<Dfo.Server.Abstractions.Events.JoustLifecycleNotification,JoustLifecycleNotificationHandler>();
        services.AddCommandHandler<QueryJoustStateCommand,Dfo.Server.Data.Events.JoustState,QueryJoustStateCommandHandler>(CommandExecutionKind.ReadOnly);
        services.AddCommandHandler<QueryJoustHistoryCommand,Dfo.Server.Data.Events.JoustHistoryEntry[],QueryJoustHistoryCommandHandler>(CommandExecutionKind.ReadOnly);
        services.AddCommandHandler<JoustBetCommand,Dfo.Server.Data.Events.JoustBetResult,JoustBetCommandHandler>(CommandExecutionKind.Transactional);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceHandler>(ClientMessageType.GameWeeklyAttendance);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceDecoder>(0x0441,ClientMessageType.GameWeeklyAttendance);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceResponse,Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceResponseEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.EventInformationMessage,GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.EventInformationMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceStateMessage,GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.WeeklyAttendanceStateMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.ScheduledDungeonStateMessage,GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.ScheduledDungeonStateMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.ScheduledQuestStateMessage,GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.WeeklyAttendance.ScheduledQuestStateMessage>>();
        services.AddNotificationHandler<Dfo.Server.Abstractions.Events.EventsChanged,Dfo.Server.Protocol.Game.WeeklyAttendance.EventsChangedHandler>();
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.WeeklyAttendance.AttendanceCompleted,Dfo.Server.Protocol.Game.WeeklyAttendance.AttendanceCompletedHandler>();
        services.AddCommandHandler<Dfo.Server.Protocol.Game.WeeklyAttendance.ClaimWeeklyAttendanceCommand,Dfo.Server.Data.Events.AttendanceClaimResult,Dfo.Server.Protocol.Game.WeeklyAttendance.ClaimWeeklyAttendanceCommandHandler>(CommandExecutionKind.Transactional);
        services.RegisterProtocolMessageHandler<MailboxSendHandler, MailboxSendRequest, GameMailboxCommand>(ClientMessageType.GameMailboxSend);
        services.RegisterProtocolMessageHandler<MailboxSendHandler, MailboxSendRequest, GameMailboxCommand>(ClientMessageType.GameMailboxSendMulti);
        services.AddGameCmdMessageDecoder<MailboxSendDecoder>(0x005E, ClientMessageType.GameMailboxSend);
        services.AddGameCmdMessageDecoder<MailboxSendMultiDecoder>(0x013B, ClientMessageType.GameMailboxSendMulti);
        services.AddGameCmdMessageEncoder<MailboxSendResponse, MailboxSendEncoder>();
        services.AddNotificationHandler<Dfo.Server.Abstractions.Mail.MailDelivered, MailDeliveredHandler>();
        services.AddProtocolCommandHandler<GameMailboxCommand, GameMailboxOutcome, GameMailboxCommandHandler>(CommandSelfSettlementCapability.SelfSettledAction,
            new(ClientMessageType.GameMailboxSend, typeof(MailboxSendRequest)),
            new(ClientMessageType.GameMailboxSendMulti, typeof(MailboxSendRequest)),
            new(ClientMessageType.GameMailboxClaim, typeof(MailboxClaimRequest)),
            new(ClientMessageType.GameMailboxAction, typeof(MailboxActionRequest)),
            new(ClientMessageType.GameMailboxOpen, typeof(MailboxOpenRequest)),
            new(ClientMessageType.GameMailboxQuery, typeof(MailboxQueryRequest)));
        services.AddNotificationHandler<MailboxCompleted, MailboxCompletedHandler>();
        services.AddNotificationHandler<Dfo.Server.Abstractions.Quests.MirrorQuestViewChanged, Dfo.Server.Protocol.Game.QuestProgress.MirrorQuestViewChangedHandler>();
        services.AddNotificationHandler<Dfo.Server.Abstractions.Quests.QuestProgressViewChanged, Dfo.Server.Protocol.Game.QuestProgress.QuestProgressViewChangedHandler>();
        services.RegisterProtocolMessageHandler<MailboxClaimHandler, MailboxClaimRequest, GameMailboxCommand>(ClientMessageType.GameMailboxClaim);
        services.AddGameCmdMessageDecoder<MailboxClaimDecoder>(0x005F, ClientMessageType.GameMailboxClaim);
        services.AddGameCmdMessageEncoder<MailboxClaimResponse, MailboxClaimEncoder>();
        services.RegisterProtocolMessageHandler<MailboxActionHandler, MailboxActionRequest, GameMailboxCommand>(ClientMessageType.GameMailboxAction);
        services.AddGameCmdMessageDecoder<MailboxActionDecoder>(0x0086, ClientMessageType.GameMailboxAction);
        services.AddGameCmdMessageEncoder<MailboxActionResponse, MailboxActionEncoder>();
        // A21 character 000, 2026-09-24: CMD 0060 seq=28 has an empty body.
        services.RegisterProtocolMessageHandler<MailboxOpenHandler, MailboxOpenRequest, GameMailboxCommand>(ClientMessageType.GameMailboxOpen);
        services.AddGameCmdMessageDecoder<MailboxOpenDecoder>(0x0060, ClientMessageType.GameMailboxOpen);
        services.AddGameCmdMessageEncoder<MailboxOpenResponse, MailboxOpenEncoder>();
        services.RegisterProtocolMessageHandler<MailboxQueryHandler, MailboxQueryRequest, GameMailboxCommand>(ClientMessageType.GameMailboxQuery);
        services.AddGameCmdMessageDecoder<MailboxQueryDecoder>(0x0144, ClientMessageType.GameMailboxQuery);
        services.AddGameCmdMessageEncoder<MailboxQueryResponse, MailboxQueryEncoder>();
        services.AddGameNotiMessageEncoder<MailboxAlarm, MailboxAlarmEncoder>();
        services.TryAddSingleton<PartyClientIdRegistry>();
        services.Replace(ServiceDescriptor.Singleton<IPartyService, PartyClientProjectionService>());
        services.AddGameMessageSending();
        services.TryAddSingleton<Dfo.Server.Abstractions.Effects.IReplayStateProjectionSink, ReplayStateProjectionDelivery>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Effects.IReplayStateProjectionDelivery, LegacyReplayStateProjectionDelivery>();
        services.TryAddSingleton<CharacterSelectionDelivery>();
        services.TryAddSingleton<DungeonRunDeliveryRegistry>();
        services.TryAddSingleton<DungeonPartyRecipients>();
        services.AddHostedService<DungeonPartyWipeService>();
        services.AddProtocolCommandHandler<GameRecoverStaminaCommand, RecoverStaminaResult, GameRecoverStaminaCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameRecoverStamina, typeof(RecoverStaminaRequest)));
        services.AddNotificationHandler<StaminaRecovered, StaminaRecoveredHandler>();
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
        services.AddSingleton<UsageQuotaProjectionDelivery>();
        services.AddNotificationHandler<Dfo.Server.Abstractions.UsageQuotas.UsageQuotaViewChanged, UsageQuotaViewChangedHandler>();
        services.TryAddScoped<ClientAppearanceVisibilityProjection>();
        services.AddNotificationHandler<ClientAppearanceVisibilityRefresh, ClientAppearanceVisibilityProjection>();
        services.AddNotificationHandler<ExpiredItemsRefresh, ExpiredItemsRefreshHandler>();
        services.AddNotificationHandler<CharacterUserStateRefresh, CharacterUserStateRefreshHandler>();
        services.AddDomainSubscriber<Dfo.Server.Abstractions.Characters.CharacterNameTagExpired, NameTagExpiredSubscriber>(
            "character.name-tag.expiration.notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<OnlineExpiredItemsRemoved, ExpiredItemsNotificationSubscriber>(
            "inventory.expiration.notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<Dfo.Server.Abstractions.Accounts.ClientAppearanceSettingsChanged,
            ClientAppearanceSettingsSubscriber>("settings.appearance-visibility", Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.LiveOnly,
            fact => $"{fact.AccountId.Value}:{fact.CharacterId.Value}");
        services.AddHostedService<MirrorQuotaDailyRefreshService>();
        services.AddHostedService<DimensionQuotaDailyRefreshService>();
        services.AddHostedService<GoldSilverHornQuotaDailyRefreshService>();
        services.AddHostedService<ReviveCoinQuotaDailyRefreshService>();
        services.AddSingleton<ItemUsageProjectionDelivery>();
        services.AddHostedService<ItemUsageDailyRefreshService>();
        services.AddCommandHandler<ReadItemUsageSnapshotCommand, ItemUsageSnapshotResult, ReadItemUsageSnapshotCommandHandler>(CommandExecutionKind.ReadOnly);
        services.AddCommandHandler<ConsumeDungeonBuffCommand, PremiumSnapshotResult, ConsumeDungeonBuffCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<ReadPremiumSnapshotCommand, PremiumSnapshotResult, ReadPremiumSnapshotCommandHandler>(CommandExecutionKind.ReadOnly);
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
        services.TryAddSingleton<TournamentProjectionDelivery>();
        services.TryAddSingleton<Dfo.Server.Protocol.Game.Altar.AltarWakeSignal>();
        services.TryAddSingleton<IAltarSchedulerWakeSignal>(p => p.GetRequiredService<Dfo.Server.Protocol.Game.Altar.AltarWakeSignal>());
        services.TryAddSingleton<Dfo.Server.Protocol.Game.Altar.AltarProjectionDelivery>();
        services.TryAddSingleton<Dfo.Server.Protocol.Game.Altar.AltarProtocolCoordinator>();
        services.AddHostedService<Dfo.Server.Protocol.Game.Altar.AltarScheduler>();
        services.AddHostedService<Dfo.Server.Protocol.Game.Altar.AltarQuotaDailyRefreshService>();
        services.AddHostedService<Dfo.Server.Protocol.Game.Tournament.TournamentQuotaDailyRefreshService>();
        services.AddHostedService<Dfo.Server.Protocol.Game.Tournament.HerasQuotaDailyRefreshService>();
        services.TryAddSingleton<Dfo.Server.Protocol.Game.MarriageProposal.MarriageWeddingProjection>();
        services.AddHostedService<Dfo.Server.Protocol.Game.MarriageProposal.MarriageWeddingScheduler>();
        services.AddGameCmdMessageDecoder<RentEquipmentDecoder>(0x03CF, ClientMessageType.GameRentEquipment);
        services.AddGameCmdMessageDecoder<ChargeRentPointDecoder>(0x03D0, ClientMessageType.GameChargeRentPoint);
        services.RegisterProtocolMessageHandler<RentEquipmentHandler,RentEquipmentRequest,GameRentEquipmentCommand>(ClientMessageType.GameRentEquipment);
        services.RegisterProtocolMessageHandler<ChargeRentPointHandler,ChargeRentPointRequest,GameChargeRentPointCommand>(ClientMessageType.GameChargeRentPoint);
        services.AddGameCmdMessageEncoder<RentEquipmentResponse, RentEquipmentEncoder>();
        services.AddGameCmdMessageEncoder<ChargeRentPointResponse, ChargeRentPointEncoder>();
        services.AddProtocolCommandHandler<GameRentEquipmentCommand,bool,GameRentEquipmentCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameRentEquipment,typeof(RentEquipmentRequest)));
        services.AddProtocolCommandHandler<GameChargeRentPointCommand,bool,GameChargeRentPointCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameChargeRentPoint,typeof(ChargeRentPointRequest)));
        services.AddDomainSubscriber<LuckyStarChanged, LuckyStarChangedSubscriber>("rental.lucky-star-refresh", DomainEventDeliveryPolicy.LiveOnly, x=>x.AccountId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        services.AddNotificationHandler<LuckyStarPanelRefresh, LuckyStarPanelRefreshHandler>();
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.UseStackable.SharedEffectDecoder>(0x00FB, ClientMessageType.GameSharedEffect);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.UseStackable.SharedEffectHandler, Dfo.Server.Protocol.Game.UseStackable.SharedEffectRequest, Dfo.Server.Protocol.Game.UseStackable.GameSharedEffectCommand>(ClientMessageType.GameSharedEffect);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.UseStackable.SharedEffectAck, Dfo.Server.Protocol.Game.UseStackable.SharedEffectAckEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.UseStackable.SharedEffectNotification, Dfo.Server.Protocol.Game.UseStackable.SharedEffectNotificationEncoder>();
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.UseStackable.GameSharedEffectCommand, bool, Dfo.Server.Protocol.Game.UseStackable.GameSharedEffectCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameSharedEffect, typeof(Dfo.Server.Protocol.Game.UseStackable.SharedEffectRequest)));
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentPostDecoder>(0x03F8, ClientMessageType.GameWeddingCommentPost);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentDeleteDecoder>(0x03F9, ClientMessageType.GameWeddingCommentDelete);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentReadDecoder>(0x03FA, ClientMessageType.GameWeddingCommentRead);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentHandler>(ClientMessageType.GameWeddingCommentPost);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentHandler>(ClientMessageType.GameWeddingCommentDelete);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentHandler>(ClientMessageType.GameWeddingCommentRead);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentEncoder>();
        services.AddCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentCommand, Dfo.Server.Data.Marriage.MarriageGuestbookResult, Dfo.Server.Protocol.Game.MarriageProposal.WeddingCommentCommandHandler>(CommandExecutionKind.Transactional);
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingBeginNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingBeginEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingAvatarNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingAvatarEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingInvitedGuestsNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingInvitedGuestsEncoder>();
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterDecoder>(0x03F6, ClientMessageType.GameWeddingSendLetter);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterHandler>(ClientMessageType.GameWeddingSendLetter);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterAckEncoder>();
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingGiftDecoder>(0x03F2, ClientMessageType.GameWeddingMoneyGift);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingGiftHandler>(ClientMessageType.GameWeddingMoneyGift);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingGiftAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingGiftAckEncoder>();
        services.AddCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.AdvanceMarriageWeddingCommand, Dfo.Server.Data.Marriage.MarriageWeddingAdvanceResult, Dfo.Server.Protocol.Game.MarriageProposal.AdvanceMarriageWeddingCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.CompleteWeddingCleanupCommand, bool, Dfo.Server.Protocol.Game.MarriageProposal.CompleteWeddingCleanupCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterCommand, Dfo.Server.Data.Marriage.MarriageWeddingLetterResult, Dfo.Server.Protocol.Game.MarriageProposal.WeddingLetterCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingGiftCommand, int, Dfo.Server.Protocol.Game.MarriageProposal.WeddingGiftCommandHandler>(CommandExecutionKind.Transactional);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalDecoder>(GameProtocol.WeddingProposalType, ClientMessageType.GameWeddingProposal);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingChurchEnterDecoder>(GameProtocol.WeddingChurchEntryType, ClientMessageType.GameWeddingChurchEntry);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingChurchEnterHandler>(ClientMessageType.GameWeddingChurchEntry);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingChurchEnterAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingChurchEnterEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingChurchPreparationNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingChurchPreparationEncoder>();
        services.AddCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.PrepareMarriageChurchCommand, Dfo.Server.Protocol.Game.MarriageProposal.MarriageChurchPreparationResult,
            Dfo.Server.Protocol.Game.MarriageProposal.PrepareMarriageChurchCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.EnterMarriageChurchCommand, Dfo.Server.Data.Marriage.MarriageChurchEntryResult,
            Dfo.Server.Protocol.Game.MarriageProposal.EnterMarriageChurchCommandHandler>(CommandExecutionKind.Transactional);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxSelectDecoder>(0x03E5, ClientMessageType.GameCollectionBox);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxPutDecoder>(0x03E6, ClientMessageType.GameCollectionBox);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxWithdrawDecoder>(0x03E7, ClientMessageType.GameCollectionBox);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxExtendDecoder>(0x03E8, ClientMessageType.GameCollectionBox);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxHandler, Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxRequest, Dfo.Server.Protocol.Game.CollectionBox.GameCollectionBoxCommand>(ClientMessageType.GameCollectionBox);
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.CollectionBox.GameCollectionBoxCommand, bool, Dfo.Server.Protocol.Game.CollectionBox.GameCollectionBoxCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch, CommandRegistrationExit.SettledInitialBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameCollectionBox, typeof(Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxRequest)));
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxAck, Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxAckEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxContentsMessage, Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxContentsEncoder>();
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleMakeDecoder>(0x0404, ClientMessageType.GameMarriageCapsule);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleGiveDecoder>(0x0405, ClientMessageType.GameMarriageCapsule);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleTakeDecoder>(0x0406, ClientMessageType.GameMarriageCapsule);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleHandler>(ClientMessageType.GameMarriageCapsule);
        services.AddCommandHandler<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleCommand, Dfo.Server.Data.Marriage.MarriageCapsuleResult, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleCommandHandler>(CommandExecutionKind.Transactional);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleAck, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleAckEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleStateMessage, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleStateEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleEnergyMessage, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleEnergyEncoder>();
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleRefresh, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleRefreshHandler>();
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleEnergyRefresh, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleEnergyRefreshHandler>();
        services.AddDomainSubscriber<Dfo.Server.Abstractions.Marriage.MarriageCapsuleEnergyChanged, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleEnergySubscriber>("marriage.capsule-notification", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<Dfo.Server.Abstractions.Marriage.MarriageCapsuleGiftChanged, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleGiftSubscriber>("marriage.capsule-gift-notification", DomainEventDeliveryPolicy.LiveOnly);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingStorageDecoder>(0x03FD, ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingMoveDecoder>(0x03FE, ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingDeleteDecoder>(0x03FF, ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingPlantDecoder>(0x0400, ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingWaterDecoder>(0x0401, ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingHarvestDecoder>(0x0402, ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingClearDecoder>(0x0403, ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingRingDecoder>(0x03F4, ClientMessageType.GameMarriageHousing);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.MarriageHousing.HousingHandler>(ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingAck, Dfo.Server.Protocol.Game.MarriageHousing.HousingAckEncoder>();
        services.AddCommandHandler<Dfo.Server.Protocol.Game.MarriageHousing.HousingCommand, Dfo.Server.Data.Marriage.HousingResult, Dfo.Server.Protocol.Game.MarriageHousing.HousingCommandHandler>(CommandExecutionKind.Transactional);
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.MarriageHousing.HousingChanged, Dfo.Server.Protocol.Game.MarriageHousing.HousingChangedHandler>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.HousingRoomMessage, Dfo.Server.Protocol.Game.HousingRoomEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.HousingStorageMessage, Dfo.Server.Protocol.Game.HousingStorageEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.HousingMoveMessage, Dfo.Server.Protocol.Game.HousingMoveEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.HousingDeleteMessage, Dfo.Server.Protocol.Game.HousingDeleteEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.HousingTreeMessage, Dfo.Server.Protocol.Game.HousingTreeEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.HousingRingMessage, Dfo.Server.Protocol.Game.HousingRingEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.HousingRewardsMessage, Dfo.Server.Protocol.Game.HousingRewardsEncoder>();
        services.AddCommandHandler<Dfo.Server.Protocol.Game.MarriageHousing.InitializeHousingCommand, bool, Dfo.Server.Protocol.Game.MarriageHousing.InitializeHousingCommandHandler>(CommandExecutionKind.Transactional);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointDecoder>(0x03F5, ClientMessageType.GameWeddingLovePoint);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointHandler>(ClientMessageType.GameWeddingLovePoint);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointAckEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointNotificationEncoder>();
        services.AddCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointCommand, Dfo.Server.Abstractions.Marriage.MarriageLovePointResult, Dfo.Server.Protocol.Game.MarriageProposal.WeddingLovePointCommandHandler>(CommandExecutionKind.Transactional);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingPartnerDecoder>(GameProtocol.WeddingPartnerType, ClientMessageType.GameWeddingPartnerQuery);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingPartnerHandler>(ClientMessageType.GameWeddingPartnerQuery);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingPartnerAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingPartnerEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingInfoMessage, Dfo.Server.Protocol.Game.MarriageProposal.WeddingInfoEncoder>();
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseDecoder>(GameProtocol.WeddingResponseType, ClientMessageType.GameWeddingResponse);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalHandler>(ClientMessageType.GameWeddingProposal);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseHandler>(ClientMessageType.GameWeddingResponse);
        services.AddCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.MarriageProposalCommand, Dfo.Server.Data.Marriage.MarriageProposalResult,
            Dfo.Server.Protocol.Game.MarriageProposal.MarriageProposalCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Protocol.Game.MarriageProposal.MarriageCeremonyCommand, Dfo.Server.Data.Marriage.MarriageCeremonyResult,
            Dfo.Server.Protocol.Game.MarriageProposal.MarriageCeremonyCommandHandler>(CommandExecutionKind.Transactional);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalAckEncoder>();
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseAck, Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseAckEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingProposalNotificationEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseNotification, Dfo.Server.Protocol.Game.MarriageProposal.WeddingResponseNotificationEncoder>();
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.Altar.AltarStateChanged,
            Dfo.Server.Protocol.Game.Altar.AltarStateChangedHandler>();
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.Altar.AltarPrepareHandler>(ClientMessageType.GameAltarPrepare);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.Altar.AltarDeathBatchHandler>(ClientMessageType.GameAltarDeathBatch);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.Altar.AltarDifficultyHandler>(ClientMessageType.GameAltarDifficulty);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.Altar.AltarPrepareDecoder>(0x0102, ClientMessageType.GameAltarPrepare);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.Altar.AltarDeathBatchDecoder>(0x00FF, ClientMessageType.GameAltarDeathBatch);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.Altar.AltarDifficultyDecoder>(0x013D, ClientMessageType.GameAltarDifficulty);
        services.AddCommandHandler<Dfo.Server.Protocol.Game.Altar.AltarActionCommand, Dfo.Server.Protocol.Game.Altar.AltarCommandResult,
            Dfo.Server.Protocol.Game.Altar.AltarActionCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Protocol.Game.Altar.AltarDeathBatchCommand, IReadOnlyList<MonsterDeathSettlementResult>,
            Dfo.Server.Protocol.Game.Altar.AltarDeathBatchCommandHandler>(CommandExecutionKind.Transactional);
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarInfoMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarInfoMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarMapMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarMapMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarWaveMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarWaveMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarIntervalMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarIntervalMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarDifficultyPromptMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarDifficultyPromptMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarDifficultyResolvedMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarDifficultyResolvedMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarRankingMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarRankingMessage>>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarRewardMessage, GameNotiStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarRewardMessage>>();
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.Altar.AltarDifficultyResponse, GameCmdStateSynchronizationEncoder<Dfo.Server.Protocol.Game.Altar.AltarDifficultyResponse>>();
        services.TryAddSingleton<TournamentRewardSweepService>();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp => sp.GetRequiredService<TournamentRewardSweepService>());
        services.AddCommandHandler<BeginTournamentRewardsCommand, bool, BeginTournamentRewardsCommandHandler>(
            CommandExecutionKind.Transactional);
        services.RegisterMessageHandler<TournamentRewardHandler>(ClientMessageType.GameTournamentRewardStatus);
        services.RegisterMessageHandler<TournamentRewardHandler>(ClientMessageType.GameTournamentSelectReward);
        services.AddGameCmdMessageDecoder<TournamentStatusDecoder>(0x01CA, ClientMessageType.GameTournamentRewardStatus);
        services.AddGameCmdMessageDecoder<TournamentSelectionDecoder>(0x01CB, ClientMessageType.GameTournamentSelectReward);
        services.AddGameNotiMessageEncoder<TournamentInfoMessage, TournamentInfoEncoder>();
        services.AddGameNotiMessageEncoder<TournamentMapMessage, TournamentMapEncoder>();
        services.AddGameNotiMessageEncoder<TournamentRewardMessage, TournamentRewardEncoder>();
        services.AddGameCmdMessageEncoder<TournamentStatusResponse, TournamentStatusEncoder>();
        services.AddGameCmdMessageEncoder<TournamentSelectionResponse, TournamentSelectionEncoder>();
        services.AddCommandHandler<SelectTournamentRewardCommand, TournamentCommandResult, SelectTournamentRewardCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<EliminateTournamentCommand, TournamentCommandResult, EliminateTournamentCommandHandler>(
            CommandExecutionKind.Transactional);
        services.TryAddSingleton<DungeonClearProjectionDelivery>();
        services.TryAddSingleton(DungeonCardPresentationTiming.Production);
        services.TryAddSingleton<IDungeonCardPresentationDelay,
            SystemDungeonCardPresentationDelay>();
        services.TryAddSingleton<DungeonCardPresentationCoordinator>();
        services.AddSingleton<IDungeonCardPresentationControl>(p => p.GetRequiredService<DungeonCardPresentationCoordinator>());
        services.AddSingleton<Dfo.Server.Abstractions.Execution.IGameplayProducerDrain>(provider => provider.GetRequiredService<DungeonCardPresentationCoordinator>());
        services.TryAddSingleton<IDungeonRunProjectionDomainService>(provider =>
            provider.GetRequiredService<DungeonRunDeliveryRegistry>());
        services.TryAddSingleton<IDungeonRunParticipantDepartureSink>(provider =>
            provider.GetRequiredService<DungeonRunDeliveryRegistry>());
        // Every notification type declared in this assembly must have a handler registered below,
        // and the server refuses to start if one does not. Declared rather than discovered: the
        // check is only worth having while something says which assemblies it covers, and this
        // line is that something.
        services.AddNotificationCatalog(typeof(ProtocolModule).Assembly);
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.RoomMechanisms.ElevatorStateMessage,
            Dfo.Server.Protocol.Game.RoomMechanisms.ElevatorStateEncoder>();
        services.AddNotificationHandler<DungeonRoomMechanismsNotification,
            Dfo.Server.Protocol.Game.RoomMechanisms.RoomMechanismNotificationHandler>();
        services.TryAddSingleton<Dfo.Server.Protocol.Game.RoomMechanisms.RoomMechanismProjectionDelivery>();
        services.AddHostedService<Dfo.Server.Protocol.Game.RoomMechanisms.RoomMechanismMaintenanceService>();
        services.AddNotificationCatalog(typeof(Dfo.Server.Abstractions.Mail.MailDelivered).Assembly);
        services.AddNotificationHandler<NpcShopPurchaseCompleted, NpcShopPurchaseCompletedHandler>();
        services.AddNotificationHandler<WarehouseChanged, WarehouseChangedHandler>();
        services.AddNotificationHandler<CeraShopPurchaseCompleted, CeraShopPurchaseCompletedHandler>();
        services.AddNotificationHandler<Dfo.Server.Abstractions.Accounts.AccountWalletChanged, AccountWalletChangedHandler>();
        services.AddNotificationHandler<OpeningCompleted, OpeningCompletedHandler>();
        services.AddNotificationHandler<EpicCraftCompleted, EpicCraftCompletedHandler>();
        services.AddNotificationHandler<ItemSocketCompleted, ItemSocketCompletedHandler>();
        services.AddNotificationHandler<EquipmentQualityCompleted, EquipmentQualityCompletedHandler>();
        services.AddNotificationHandler<MagicSealGradeCompleted, MagicSealGradeCompletedHandler>();
        services.AddNotificationHandler<AvatarCompoundCompleted, AvatarCompoundCompletedHandler>();
        services.AddNotificationHandler<ShopPurchaseCountsReported, ShopPurchaseCountsReportedHandler>();
        services.AddNotificationHandler<ItemSaleCompleted, ItemSaleCompletedHandler>();
        services.AddNotificationHandler<EquipmentRepairCompleted, EquipmentRepairCompletedHandler>();
        services.AddNotificationHandler<EquipmentDisjointCompleted, EquipmentDisjointCompletedHandler>();
        services.AddNotificationHandler<EquipmentReinforcementCompleted,
            EquipmentReinforcementCompletedHandler>();
        services.AddNotificationHandler<EquipmentEnchantCompleted,
            EquipmentEnchantCompletedHandler>();
        services.AddNotificationHandler<MagicSealCompleted,
            MagicSealCompletedHandler>();
        services.AddNotificationHandler<OutworldCompleted,
            OutworldCompletedHandler>();
        services.AddNotificationHandler<ForgingCompleted,
            ForgingCompletedHandler>();
        services.AddNotificationHandler<SortLockChanged, SortLockChangedHandler>();
        services.AddNotificationHandler<InventorySorted, InventorySortedHandler>();
        services.AddNotificationHandler<EquipmentDurabilityChanged, EquipmentDurabilityChangedHandler>();
        services.AddNotificationHandler<ItemDiscardCompleted, ItemDiscardCompletedHandler>();
        services.AddNotificationHandler<CrystalContractSelected, CrystalContractSelectedHandler>();
        services.AddNotificationHandler<MaterialsConsumed, MaterialsConsumedHandler>();
        services.TryAddSingleton<PartyProjectionStreamRegistry>();
        services.TryAddSingleton<IPartyProjectionStateDomainService>(provider => provider.GetRequiredService<PartyProjectionStreamRegistry>());
        services.TryAddSingleton<PartyNotificationDelivery>();
        services.TryAddSingleton<IPartyTownProjectionDelivery, PartyTownProjectionDelivery>();
        services.TryAddSingleton<TownAreaProjectionDelivery>();
        services.AddNotificationHandler<PartyInviteIssued, PartyInviteIssuedHandler>();
        services.AddNotificationHandler<PartyRealtimeChanged, PartyRealtimeChangedHandler>();
        services.AddNotificationHandler<PartyInviteAccepted, PartyInviteAcceptedHandler>();
        services.AddNotificationHandler<PartyEndpointsChanged, PartyEndpointsChangedHandler>();
        services.AddNotificationHandler<PartyRosterChanged, PartyRosterChangedHandler>();
        services.AddNotificationHandler<PartySettingsChanged, PartySettingsChangedHandler>();
        services.AddNotificationHandler<PartyLeadershipChanged, PartyLeadershipChangedHandler>();
        services.AddNotificationHandler<
            OtherCharacterDetailRequested,
            OtherCharacterDetailRequestedHandler>();
        services.TryAddSingleton<ICleanupNotificationDelivery, CleanupNotificationDelivery>();
        services.AddNotificationHandler<SessionCleanupAnnounced, SessionCleanupAnnouncedHandler>();
        services.AddNotificationHandler<TownParticipantPublished, TownParticipantPublishedHandler>();
        services.AddNotificationHandler<TownPositionChanged, TownPositionChangedHandler>();
        services.AddNotificationHandler<TownAreaRosterReconciled, TownAreaRosterReconciledHandler>();
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
        services.AddCommandHandler<EndClearReturnCommand, EndPartyWipeResult, EndClearReturnCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddCommandHandler<EndPartyWipeCommand, EndPartyWipeResult, EndPartyWipeCommandHandler>(
            CommandExecutionKind.Direct);
        services.AddProtocolCommandHandler<ChannelConnectCommand, ChannelConnectCompleted, ChannelConnectCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.ChannelConnect, typeof(ChannelConnectRequest)));
        services.AddProtocolCommandHandler<ChannelScriptVersionCommand, ChannelScriptVersionCompleted, ChannelScriptVersionCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.ChannelScriptVersion, typeof(ChannelScriptVersionRequest)));
        services.AddProtocolCommandHandler<ChannelScriptCommand, ChannelScriptCompleted, ChannelScriptCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.ChannelScript, typeof(ChannelScriptRequest)));
        services.AddProtocolCommandHandler<ChannelInfoCommand, ChannelInfoCompleted, ChannelInfoCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.ChannelInfo, typeof(ChannelInfoRequest)));
        services.AddProtocolCommandHandler<GameAcceptQuestCommand, QuestAcceptOutcome, GameAcceptQuestCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameAcceptQuest, typeof(AcceptQuestRequest)));
        services.AddProtocolCommandHandler<GameGiveUpQuestCommand, GiveUpQuestOutcome, GameGiveUpQuestCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGiveUpQuest, typeof(GiveUpQuestRequest)));
        services.AddProtocolCommandHandler<GameSetQuestTriggerCommand, QuestTriggerOutcome, GameSetQuestTriggerCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetQuestTrigger, typeof(SetQuestTriggerRequest)));
        services.AddProtocolCommandHandler<GameFinishQuestCommand, QuestFinishOutcome, GameFinishQuestCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameFinishQuest, typeof(FinishQuestRequest)));
        services.AddProtocolCommandHandler<GameMonsterDeathCommand, bool, GameMonsterDeathCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameMonsterDeath, typeof(MonsterDeathRequest)));
        services.AddCommandHandler<PrepareReturnToSelectionCommand, PreparedReturnToSelection, PrepareReturnToSelectionCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<CompleteReturnToSelectionCommand, CompletedReturnToSelection, CompleteReturnToSelectionCommandHandler>(
            CommandExecutionKind.Workflow);
        // Every command below reaches a write that outlives the process, so every one of them
        // opens a use case boundary. A write with no boundary settles on its own: it looks like it
        // worked, it is not taken back by whatever else the command was doing, and no test shows
        // it, because a lone write does succeed. The kind decides only that -- the pipeline treats
        // Direct, Workflow and Transactional identically everywhere else.
        services.AddProtocolCommandHandler<GameLoginCommand, GameLoginCompleted, GameLoginCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameLogin, typeof(GameLoginRequest)));
        // These complete protocol commands own their original explicit write segments; no account gate remains.
        services.AddProtocolCommandHandler<GameAccountRosterCommand, AccountCharacterRoster, GameAccountRosterCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGetAccountCharacterRoster, typeof(GetAccountCharacterRosterRequest)));
        services.AddProtocolCommandHandler<GameCheckCharacterNameCommand, CharacterNameStatus, GameCheckCharacterNameCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCheckCharacterName, typeof(CheckCharacterNameRequest)));
        services.AddProtocolCommandHandler<GameCreateCharacterCommand, CharacterCreationResult, GameCreateCharacterCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCreateCharacter, typeof(CreateCharacterRequest)));
        services.AddProtocolCommandHandler<GameDeleteCharacterCommand, CharacterDeletionResult, GameDeleteCharacterCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameDeleteCharacter, typeof(DeleteCharacterRequest)));
        services.AddCommandHandler<PrepareSelectedCharacterSessionCommand, PreparedSelectedCharacterSession, PrepareSelectedCharacterSessionCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<CleanupExpiredCharacterItemsCommand, bool, CleanupExpiredCharacterItemsCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<EnsureDailyContractPuppetsCommand, bool, EnsureDailyContractPuppetsCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<PrepareSelectedCharacterStateCommand, SelectCharacterResult, PrepareSelectedCharacterStateCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<ApplySelectedCharacterDailyRefillCommand, EntryInventoryPreparation, ApplySelectedCharacterDailyRefillCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<PublishSelectedCharacterSessionCommand, PublishedSelectedCharacterSession, PublishSelectedCharacterSessionCommandHandler>(
            CommandExecutionKind.Workflow);
        services.AddCommandHandler<MarkSelectedCharacterPvpReadyCommand, SelectedCharacterPvpPublication, MarkSelectedCharacterPvpReadyCommandHandler>(
            CommandExecutionKind.Workflow);
        services.AddNotificationHandler<PvpParticipantPublished, PvpParticipantPublishedHandler>();
        // These two change a character's stored state, so they are the phases that need a
        // boundary: the unit of work opens one for Transactional commands and for nothing else.
        // Without it the store would have no transaction to join and the change would land
        // outside any outcome that could take it back.
        services.AddCommandHandler<SynchronizeSelectedCharacterQuestsCommand, SynchronizedSelectedCharacterQuests, SynchronizeSelectedCharacterQuestsCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddCommandHandler<ReviveSelectedCharacterPetCommand, RevivedSelectedCharacterPet, ReviveSelectedCharacterPetCommandHandler>(
            CommandExecutionKind.Transactional);
        // Not transactional, deliberately. Compensation is best effort: it collects what failed
        // and carries on, and it ends by throwing what it collected. A boundary here would take
        // back everything that had worked because something else did not -- the opposite of what
        // it is for. Its one lasting write, where the character was standing, settles on its own.
        services.AddCommandHandler<CompensateSelectedCharacterSessionCommand, CompensatedSelectedCharacterSession, CompensateSelectedCharacterSessionCommandHandler>(
            CommandExecutionKind.Workflow);

        // Both act on one character's own container, which the registry already keeps per
        // character; neither reads or writes anything the account owns, so neither takes it.
        // Transactional, and in the same change that broke the move into separate writes. Marking
        // it afterwards would leave a window in which a move was several writes with nothing to
        // settle them together -- a window every test would pass through green.
        services.AddProtocolCommandHandler<GameMoveItemSpaceCommand, bool, GameMoveItemSpaceCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameMoveItemSpace, typeof(MoveItemSpaceRequest)));
        services.AddProtocolCommandHandler<GameUseStackableCommand, ItemUseResult, GameUseStackableCommandHandler>(
            new[] { CommandRegistrationExit.SettledRequiredExplicit, CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameUseStackable, typeof(UseStackableRequest)));
        services.AddProtocolCommandHandler<GameIncreaseStatusCommand, IncreaseStatusResult, GameIncreaseStatusCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameIncreaseStatus, typeof(IncreaseStatusRequest)));
        services.RegisterProtocolMessageHandler<IncreaseStatusHandler, IncreaseStatusRequest, GameIncreaseStatusCommand>(ClientMessageType.GameIncreaseStatus);
        services.AddGameCmdMessageDecoder<IncreaseStatusDecoder>(0x001E, ClientMessageType.GameIncreaseStatus);
        services.AddGameCmdMessageEncoder<IncreaseStatusResponse, IncreaseStatusEncoder>();
        services.AddProtocolCommandHandler<GameHatchCreatureCommand, bool, GameHatchCreatureCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameHatchCreature, typeof(HatchCreatureRequest)));
        services.AddProtocolCommandHandler<GameRenameCreatureCommand, bool, GameRenameCreatureCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameRenameCreature, typeof(RenameCreatureRequest)));
        services.AddProtocolCommandHandler<GameGenCeraTicketCommand, bool, GameGenCeraTicketCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGenCeraTicket, typeof(GenCeraTicketRequest)));
        services.AddProtocolCommandHandler<GameQuestClearCouponCommand, bool, GameQuestClearCouponCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameQuestClearCoupon, typeof(QuestClearCouponRequest)));
        services.AddCommandHandler<StoreTitleCommand, TitleBookMutationResult, StoreTitleCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<WithdrawTitleCommand, TitleBookMutationResult, WithdrawTitleCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<EquipCarriedTitleReplacingMirrorCommand, TitleBookMutationResult, EquipCarriedTitleReplacingMirrorCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<ReconcileAchievementCommand, AchievementReconciliationResult, ReconcileAchievementCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<QueryTitleBookCommand, IReadOnlyList<CharacterTitleBookCategoryState>, QueryTitleBookCommandHandler>(CommandExecutionKind.ReadOnly);
        services.AddDomainSubscriber<AchievementProgressChanged, AchievementEventSubscriber>("achievement.noti", DomainEventDeliveryPolicy.LiveOnly, f => f.CharacterId.Value.ToString());
        services.AddDomainSubscriber<TitleUnlocked, TitleBookEventSubscriber>("title.noti-unlock", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<TitleStored, TitleBookEventSubscriber>("title.noti-store", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<TitleWithdrawn, TitleBookEventSubscriber>("title.noti-withdraw", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<ItemMirrorRemoved, TitleBookEventSubscriber>("inventory.noti-mirror-removed", DomainEventDeliveryPolicy.LiveOnly);
        services.AddNotificationHandler<AchievementChanged, AchievementChangedHandler>();
        services.AddNotificationHandler<TitleBookChanged, TitleBookChangedHandler>();
        services.AddCommandHandler<SetTitleAppearanceCommand, bool, SetTitleAppearanceCommandHandler>(CommandExecutionKind.Transactional);
        services.RegisterMessageHandler<SetTitleAppearanceHandler>(ClientMessageType.GameSetTitleAppearance);
        services.AddGameCmdMessageDecoder<SetTitleAppearanceDecoder>(0x0239, ClientMessageType.GameSetTitleAppearance);
        services.AddGameCmdMessageEncoder<SetTitleAppearanceResponse, SetTitleAppearanceEncoder>();
        services.AddDomainSubscriber<TitleAppearanceChanged, TitleBookEventSubscriber>("title.noti-appearance", DomainEventDeliveryPolicy.LiveOnly);
        services.RegisterMessageHandler<TitleBookMutationHandler>(ClientMessageType.GameStoreTitle);
        services.RegisterMessageHandler<TitleBookMutationHandler>(ClientMessageType.GameWithdrawTitle);
        services.RegisterMessageHandler<AchievementTriggerHandler>(ClientMessageType.GameAchievementTrigger);
        services.RegisterMessageHandler<QueryTitleBookHandler>(ClientMessageType.GameQueryTitleBook);
        services.AddGameCmdMessageDecoder<StoreTitleDecoder>(0x019C, ClientMessageType.GameStoreTitle);
        services.AddGameCmdMessageDecoder<WithdrawTitleDecoder>(0x019D, ClientMessageType.GameWithdrawTitle);
        services.AddGameCmdMessageDecoder<AchievementTriggerDecoder>(0x01A1, ClientMessageType.GameAchievementTrigger);
        services.AddGameCmdMessageDecoder<QueryTitleBookDecoder>(0x01A8, ClientMessageType.GameQueryTitleBook);
        services.AddGameCmdMessageEncoder<TitleBookMutationResponse, TitleBookMutationEncoder>();
        services.AddGameCmdMessageEncoder<AchievementTriggerResponse, AchievementTriggerEncoder>();
        services.AddGameNotiMessageEncoder<TitleUnlockedMessage, GameNotiStateSynchronizationEncoder<TitleUnlockedMessage>>();
        services.AddProtocolCommandHandler<GameLevelUpTicketCommand, bool, GameLevelUpTicketCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameLevelUpTicket, typeof(LevelUpTicketRequest)));
        services.AddProtocolCommandHandler<GameChangeSkillSlotCommand, bool, GameChangeSkillSlotCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameChangeSkillSlot, typeof(ChangeSkillSlotRequest)));
        services.RegisterProtocolMessageHandler<ChangeSkillSlotHandler, ChangeSkillSlotRequest, GameChangeSkillSlotCommand>(ClientMessageType.GameChangeSkillSlot);
        services.AddGameCmdMessageDecoder<ChangeSkillSlotDecoder>(0x001C, ClientMessageType.GameChangeSkillSlot);
        services.AddGameCmdMessageEncoder<ChangeSkillSlotResponse, ChangeSkillSlotEncoder>();
        services.AddProtocolCommandHandler<GameSwitchSkillBuildCommand, bool, GameSwitchSkillBuildCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSwitchSkillBuild, typeof(SwitchSkillBuildRequest)));
        services.RegisterProtocolMessageHandler<SwitchSkillBuildHandler, SwitchSkillBuildRequest, GameSwitchSkillBuildCommand>(ClientMessageType.GameSwitchSkillBuild);
        services.AddGameCmdMessageDecoder<SwitchSkillBuildDecoder>(0x0104, ClientMessageType.GameSwitchSkillBuild);
        services.AddGameCmdMessageEncoder<SwitchSkillBuildResponse, SwitchSkillBuildEncoder>();
        services.AddProtocolCommandHandler<GameLearnSkillCommand, CharacterSkillLearningResult, GameLearnSkillCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameLearnSkill, typeof(LearnSkillRequest)));
        services.AddProtocolCommandHandler<GameSellItemCommand, bool, GameSellItemCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSellItem, typeof(SellItemRequest)));
        services.AddProtocolCommandHandler<GameRepairItemCommand, bool, GameRepairItemCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameRepairItem, typeof(RepairItemRequest)));
        services.AddProtocolCommandHandler<GameTeleportCommand, bool, GameTeleportCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameTeleport, typeof(TeleportRequest)));
        services.AddProtocolCommandHandler<GameSetUserAreaCommand, bool, GameTownMovementCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetUserArea, typeof(SetUserAreaRequest)));
        services.AddProtocolCommandHandler<GameSetUserPositionCommand, bool, GameTownMovementCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetUserPosition, typeof(SetUserPositionRequest)));
        services.AddProtocolCommandHandler<GameMagicSealCommand, EquipmentMagicSealResult, GameMagicSealCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameUnsealRandomOption, typeof(MagicSealRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameChangeRandomOption, typeof(MagicSealRequest)));
        services.AddProtocolCommandHandler<GameCompoundItemCommand, bool, GameCompoundItemCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameCompoundItem, typeof(CompoundItemRequest)));
        services.AddProtocolCommandHandler<GameAvatarCompoundCommand, AvatarCompoundResult, GameAvatarCompoundCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameAvatarCompound, typeof(AvatarCompoundRequest)));
        services.AddProtocolCommandHandler<GameResetRandomOptionCommand, EquipmentQualityResult, GameResetRandomOptionCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameResetRandomOption, typeof(ResetRandomOptionRequest)));
        services.AddProtocolCommandHandler<GameItemSocketCommand, ItemSocketResult, GameItemSocketCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameAvatarEmblem, typeof(ItemSocketRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameAvatarSocketOpen, typeof(ItemSocketRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameEquipmentEmblem, typeof(ItemSocketRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameEquipmentSocketOpen, typeof(ItemSocketRequest)));
        services.AddProtocolCommandHandler<GameResetItemQualityCommand, EquipmentQualityResult, GameResetItemQualityCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameResetItemQuality, typeof(ResetItemQualityRequest)));
        services.AddProtocolCommandHandler<GameEpicCraftCommand, bool, GameEpicCraftCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameEpicCraft, typeof(EpicCraftRequest)));
        services.AddProtocolCommandHandler<GameOutworldCommand, EquipmentOutworldResult, GameOutworldCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GamePurifyItem, typeof(OutworldRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameInvestAmplifyOption, typeof(OutworldRequest)));
        services.AddProtocolCommandHandler<GameForgingCommand, EquipmentForgingResult, GameForgingCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameSeparateUpgrade, typeof(ForgingRequest)));
        services.AddProtocolCommandHandler<GameSortLockCommand, bool, GameSortLockCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSortLock, typeof(SortLockRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameSortUnlock, typeof(SortLockRequest)));
        services.AddProtocolCommandHandler<GameWarehouseCommand, InventoryExpansionResult, GameWarehouseCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameCreateAccountCargo, typeof(WarehouseRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameUpgradeAccountCargo, typeof(WarehouseRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameDepositAccountCargo, typeof(WarehouseRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameWithdrawAccountCargo, typeof(WarehouseRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameUpgradeCargo, typeof(WarehouseRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameUpgradeInventory, typeof(WarehouseRequest)));
        services.AddProtocolCommandHandler<GameSortItemCommand, bool, GameSortItemCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSortItem, typeof(SortItemRequest)));
        services.AddCommandHandler<DecreaseDurabilityCommand, EquipmentDurabilityResult,
            DecreaseDurabilityCommandHandler>(CommandExecutionKind.Transactional);
        services.AddProtocolCommandHandler<GameCrystalContractCommand, bool, GameCrystalContractCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCrystalContract, typeof(CrystalContractRequest)));
        services.AddProtocolCommandHandler<GameDeleteItemCommand, bool, GameDeleteItemCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameDeleteItem, typeof(DeleteItemRequest)));
        services.AddProtocolCommandHandler<GameBuyItemCommand, BuyItemOutcome, GameBuyItemCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameBuyItem, typeof(BuyItemRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameBuyItem, typeof(HousingBuyItemRequest)));
        services.AddProtocolCommandHandler<GameCeraShopPurchaseCommand, CeraShopPurchaseResult, GameCeraShopPurchaseCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCeraShopPurchase, typeof(CeraShopPurchaseRequest)));
        services.AddProtocolCommandHandler<GameMagicBoxBatchOpeningCommand, MagicBoxOpeningResult, GameMagicBoxBatchOpeningCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameMagicBoxBatchOpening, typeof(MagicBoxBatchOpeningRequest)));
        services.AddProtocolCommandHandler<GameMagicBoxSingleOpeningCommand, MagicBoxOpeningResult, GameMagicBoxSingleOpeningCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameMagicBoxSingleOpening, typeof(MagicBoxSingleOpeningRequest)));
        services.AddProtocolCommandHandler<GameVendingMachineCommand, bool, GameVendingMachineCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameVendingMachine, typeof(VendingMachineRequest)));
        services.AddProtocolCommandHandler<GameLotteryOpeningCommand, bool, GameLotteryOpeningCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameLotteryOpening, typeof(LotteryOpeningRequest)));
        services.AddProtocolCommandHandler<GamePackageOpeningCommand, PackageOpeningResult, GamePackageOpeningCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GamePackageOpening, typeof(PackageOpeningRequest)));
        services.AddProtocolCommandHandler<GameAvatarPackageOpeningCommand, PackageOpeningResult, GameAvatarPackageOpeningCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameAvatarPackageOpening, typeof(AvatarPackageOpeningRequest)));
        services.AddCommandHandler<QueryShopPurchaseCountsCommand, NpcShopPurchaseCountsResult, QueryShopPurchaseCountsCommandHandler>(
            CommandExecutionKind.Direct);
        // A tutorial step's items settle together, and through the same container everything else
        // writes through.
        services.AddCommandHandler<
            GrantTutorialRewardCommand, TutorialRewardResult, GrantTutorialRewardCommandHandler>(
            CommandExecutionKind.Transactional);
        // Taking a piece off a room's floor: the claim on the floor and the row in the bag are one
        // change, and a piece that left the floor without arriving anywhere is the thing to avoid.
        services.AddProtocolCommandHandler<GameGetItemCommand, bool, GameGetItemCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGetItem, typeof(GetItemRequest)));
        // The tutorial's level: one row, written once, and the client is told what it now stands at.
        services.AddProtocolCommandHandler<GameTutorialLevelUpCommand, bool, GameTutorialLevelUpCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameTutorialLevelUp, typeof(TutorialLevelUpRequest)));
        // Turning a card over now hands the piece behind it to the character's bag, which is the
        // moment the note left here foresaw: a claim on a run alone is memory, and a row in a bag
        // is not. Transactional from that change onwards, on the same reading the ground pick-up
        // was given -- the claim and the row are one change, and a position marked taken whose
        // piece never arrived anywhere is the thing to avoid. The bag's own container refuses a
        // write with no boundary open, so this is what makes the arrival possible at all.
        services.AddProtocolCommandHandler<GameLeavePartyCommand, bool, GamePartyMutationCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameLeaveParty, typeof(LeavePartyRequest)));
        services.AddProtocolCommandHandler<GameRemovePartyMemberCommand, bool, GamePartyMutationCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameRemovePartyMember, typeof(RemovePartyMemberRequest)));
        services.AddProtocolCommandHandler<GameChangePartyLeaderCommand, bool, GamePartyMutationCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameChangePartyLeader, typeof(ChangePartyLeaderRequest)));
        services.AddProtocolCommandHandler<GameSetUdpIpPortCommand, bool, GameSetUdpIpPortCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetUdpIpPort, typeof(SetUdpIpPortRequest)));
        services.AddProtocolCommandHandler<GameRequestPeerCommand, bool, GameRequestPeerCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameRequestPeer, typeof(RequestPeerRequest)));
        services.AddProtocolCommandHandler<GameResponsePeerCommand, bool, GameResponsePeerCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameResponsePeer, typeof(ResponsePeerRequest)));
        services.AddProtocolCommandHandler<GameSetPartyInfoCommand, bool, GameSetPartyInfoCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetPartyInfo, typeof(SetPartyInfoRequest)));
        services.AddProtocolCommandHandler<GameCallPartyMemberRealtimeInfoCommand, bool, GameCallPartyMemberRealtimeInfoCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCallPartyMemberRealtimeInfo, typeof(CallPartyMemberRealtimeInfoRequest)));
        services.AddProtocolCommandHandler<GameOtherCharacterDetailCommand, GameOtherCharacterDetailCompleted, GameOtherCharacterDetailCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGetOtherCharacterDetail, typeof(GetOtherCharacterDetailRequest)));
    }

    /// <summary>Binds each catalogue member to the handler that consumes it.</summary>
    private static void RegisterMessageHandlers(IServiceCollection services)
    {
        services.RegisterProtocolMessageHandler<ChannelConnectHandler, ChannelConnectRequest, ChannelConnectCommand>(ClientMessageType.ChannelConnect);
        services.RegisterProtocolMessageHandler<ChannelScriptVersionHandler, ChannelScriptVersionRequest, ChannelScriptVersionCommand>(ClientMessageType.ChannelScriptVersion);
        services.RegisterProtocolMessageHandler<ChannelScriptHandler, ChannelScriptRequest, ChannelScriptCommand>(ClientMessageType.ChannelScript);
        services.RegisterProtocolMessageHandler<ChannelInfoHandler, ChannelInfoRequest, ChannelInfoCommand>(ClientMessageType.ChannelInfo);
        services.RegisterMessageHandler<ReturnToSelectionHandler>(ClientMessageType.GameReturnToSelection);
        services.RegisterMessageHandler<GameExitHandler>(ClientMessageType.GameExit);
        services.RegisterMessageHandler<PremiumServiceUsageReportHandler>(ClientMessageType.GamePremiumServiceUsageReport);
        services.RegisterProtocolMessageHandler<RecoverStaminaHandler, RecoverStaminaRequest, GameRecoverStaminaCommand>(ClientMessageType.GameRecoverStamina);
        services.RegisterProtocolMessageHandler<GameLoginHandler, GameLoginRequest, GameLoginCommand>(ClientMessageType.GameLogin);
        services.RegisterProtocolMessageHandler<GetAccountCharacterRosterHandler, GetAccountCharacterRosterRequest, GameAccountRosterCommand>(ClientMessageType.GameGetAccountCharacterRoster);
        services.RegisterProtocolMessageHandler<GetOtherCharacterDetailHandler, GetOtherCharacterDetailRequest, GameOtherCharacterDetailCommand>(
            ClientMessageType.GameGetOtherCharacterDetail);
        services.RegisterMessageHandler<SelectCharacterHandler>(ClientMessageType.GameSelectCharacter);
        services.RegisterMessageHandler<ClientSpecStatisticHandler>(ClientMessageType.GameClientSpecStatistic);
        services.RegisterMessageHandler<ClientHardwareDetailHandler>(ClientMessageType.GameClientHardwareDetail);
        services.RegisterMessageHandler<ClientSpecProfileHandler>(ClientMessageType.GameClientSpecProfile);
        services.RegisterMessageHandler<SessionMetricsHandler>(ClientMessageType.GameSessionMetrics);
        services.RegisterProtocolMessageHandler<AcceptQuestHandler, AcceptQuestRequest, GameAcceptQuestCommand>(ClientMessageType.GameAcceptQuest);
        services.RegisterProtocolMessageHandler<GiveUpQuestHandler, GiveUpQuestRequest, GameGiveUpQuestCommand>(ClientMessageType.GameGiveUpQuest);
        services.RegisterProtocolMessageHandler<SetQuestTriggerHandler, SetQuestTriggerRequest, GameSetQuestTriggerCommand>(ClientMessageType.GameSetQuestTrigger);
        services.RegisterProtocolMessageHandler<FinishQuestHandler, FinishQuestRequest, GameFinishQuestCommand>(ClientMessageType.GameFinishQuest);
        services.RegisterMessageHandler<TaggedIdReportHandler>(ClientMessageType.GameTaggedIdReport);
        services.RegisterMessageHandler<RosterFollowUpHandler>(ClientMessageType.GameRosterFollowUp);
        services.RegisterProtocolMessageHandler<CheckCharacterNameHandler, CheckCharacterNameRequest, GameCheckCharacterNameCommand>(ClientMessageType.GameCheckCharacterName);
        services.RegisterProtocolMessageHandler<CreateCharacterHandler, CreateCharacterRequest, GameCreateCharacterCommand>(ClientMessageType.GameCreateCharacter);
        services.RegisterProtocolMessageHandler<DeleteCharacterHandler, DeleteCharacterRequest, GameDeleteCharacterCommand>(ClientMessageType.GameDeleteCharacter);
        services.RegisterMessageHandler<MercenaryInfoHandler>(ClientMessageType.GameMercenaryInfo);
        services.RegisterTelemetryMessageHandler<HiddenCharacterInfoHandler>(ClientMessageType.GameHiddenCharacterInfo);
        services.AddCommandHandler<SaveCharacterOptionCommand, bool, SaveCharacterOptionCommandHandler>(CommandExecutionKind.Transactional);
        services.RegisterMessageHandler<SaveCharacterOptionHandler>(ClientMessageType.GameSaveCharacterOption);
        services.AddGameCmdMessageDecoder<SaveCharacterOptionDecoder>(0x01C0, ClientMessageType.GameSaveCharacterOption);
        services.AddCommandHandler<SaveClientSettingsCommand, bool, SaveClientSettingsCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<ActivateClientHotkeyProfileCommand, bool, ActivateClientHotkeyProfileCommandHandler>(CommandExecutionKind.Direct);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.SaveHotkeys.SaveHotkeysHandler>(ClientMessageType.GameSaveHotkeys);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.SaveHotkeys.SaveHotkeysDecoder>(0x00C6, ClientMessageType.GameSaveHotkeys);
        services.RegisterMessageHandler<Dfo.Server.Protocol.Game.SaveQuickChat.SaveQuickChatHandler>(ClientMessageType.GameSaveQuickChat);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.SaveQuickChat.SaveQuickChatDecoder>(0x0170, ClientMessageType.GameSaveQuickChat);
        services.RegisterMessageHandler<SaveGameOption1Handler>(ClientMessageType.GameSaveGameOption1);
        services.RegisterMessageHandler<ContentsPlayInfoHandler>(ClientMessageType.GameContentsPlayInfo);
        services.RegisterMessageHandler<ConnectionCheckHandler>(ClientMessageType.GameConnectionCheck);

        services.RegisterProtocolMessageHandler<SetUdpIpPortHandler, SetUdpIpPortRequest, GameSetUdpIpPortCommand>(ClientMessageType.GameSetUdpIpPort);
        services.RegisterProtocolMessageHandler<RequestPeerHandler, RequestPeerRequest, GameRequestPeerCommand>(ClientMessageType.GameRequestPeer);
        services.RegisterProtocolMessageHandler<ResponsePeerHandler, ResponsePeerRequest, GameResponsePeerCommand>(ClientMessageType.GameResponsePeer);
        services.RegisterProtocolMessageHandler<LeavePartyHandler, LeavePartyRequest, GameLeavePartyCommand>(ClientMessageType.GameLeaveParty);
        services.RegisterProtocolMessageHandler<RemovePartyMemberHandler, RemovePartyMemberRequest, GameRemovePartyMemberCommand>(
            ClientMessageType.GameRemovePartyMember);
        services.RegisterProtocolMessageHandler<ChangePartyLeaderHandler, ChangePartyLeaderRequest, GameChangePartyLeaderCommand>(
            ClientMessageType.GameChangePartyLeader);
        services.RegisterProtocolMessageHandler<CallPartyMemberRealtimeInfoHandler, CallPartyMemberRealtimeInfoRequest, GameCallPartyMemberRealtimeInfoCommand>(
            ClientMessageType.GameCallPartyMemberRealtimeInfo);
        services.RegisterProtocolMessageHandler<SetPartyInfoHandler, SetPartyInfoRequest, GameSetPartyInfoCommand>(ClientMessageType.GameSetPartyInfo);

        // One handler, the remaining wire keys in the map-entry report family.
        services.RegisterMessageHandler<MapEntryReportHandler>(ClientMessageType.GameCeraQuery);
        services.RegisterMessageHandler<NoticeSeenHandler>(ClientMessageType.GameInformNoticeSecond);
        services.RegisterMessageHandler<NoticeSeenHandler>(ClientMessageType.GameNoticeSeen);
        services.AddGameCmdMessageDecoder<NoticeSeenDecoder>(0x01DE, ClientMessageType.GameNoticeSeen);
        services.AddCommandHandler<MarkNoticeSeenCommand, bool, MarkNoticeSeenCommandHandler>(
            CommandExecutionKind.Transactional);
        services.RegisterMessageHandler<SecurityStatusHandler>(ClientMessageType.GameSecurityStatus);
        services.RegisterMessageHandler<MapEntryReportHandler>(ClientMessageType.GameRuntimeTingStatistic);
        services.AddProtocolCommandHandler<GameEnterSelectDungeonCommand, bool, GameDungeonSelectionCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameEnterSelectDungeon, typeof(EnterSelectDungeonRequest)));
        services.RegisterProtocolMessageHandler<EnterSelectDungeonHandler, EnterSelectDungeonRequest, GameEnterSelectDungeonCommand>(ClientMessageType.GameEnterSelectDungeon);
        services.AddProtocolCommandHandler<GameSelectDungeonCommand, bool, GameSelectDungeonCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSelectDungeon, typeof(SelectDungeonRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameMirrorDungeonEntry, typeof(SelectDungeonRequest)));
        services.RegisterProtocolMessageHandler<SelectDungeonHandler, SelectDungeonRequest, GameSelectDungeonCommand>(ClientMessageType.GameSelectDungeon);
        services.RegisterProtocolMessageHandler<SelectDungeonHandler, SelectDungeonRequest, GameSelectDungeonCommand>(ClientMessageType.GameMirrorDungeonEntry);
        services.AddCommandHandler<Dfo.Server.Protocol.Game.MirrorDungeonEntry.ReportDungeonQuestFactsCommand, bool, Dfo.Server.Protocol.Game.MirrorDungeonEntry.ReportDungeonQuestFactsCommandHandler>(
            CommandExecutionKind.Transactional);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MirrorDungeonEntry.MirrorDungeonEntryDecoder>(0x02B6, ClientMessageType.GameMirrorDungeonEntry);
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MirrorDungeonEntry.MirrorDungeonMissionNotification, Dfo.Server.Protocol.Game.MirrorDungeonEntry.MirrorDungeonMissionEncoder>();
        services.RegisterProtocolMessageHandler<PrepareCycleDungeonEntryHandler, PrepareCycleDungeonEntryRequest, GamePrepareCycleDungeonEntryCommand>(ClientMessageType.GamePrepareCycleDungeonEntry);
        services.AddProtocolCommandHandler<GamePrepareCycleDungeonEntryCommand, CycleDungeonPreparation, GamePrepareCycleDungeonEntryCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GamePrepareCycleDungeonEntry, typeof(PrepareCycleDungeonEntryRequest)));
        services.AddGameCmdMessageDecoder<PrepareCycleDungeonEntryDecoder>(0x0308, ClientMessageType.GamePrepareCycleDungeonEntry);
        services.AddGameCmdMessageEncoder<PrepareCycleDungeonEntryResponse, PrepareCycleDungeonEntryEncoder>();
        services.AddProtocolCommandHandler<GameFinishLoadingCommand, bool, GameMapLoadingCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameFinishLoading, typeof(FinishLoadingRequest)));
        services.RegisterProtocolMessageHandler<FinishLoadingHandler, FinishLoadingRequest, GameFinishLoadingCommand>(ClientMessageType.GameFinishLoading);
        services.AddProtocolCommandHandler<GameMoveMapCommand, bool, GameMapLoadingCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameMoveMap, typeof(MoveMapRequest)));
        services.RegisterProtocolMessageHandler<MoveMapHandler, MoveMapRequest, GameMoveMapCommand>(ClientMessageType.GameMoveMap);
        services.RegisterMessageHandler<StoryPauseHandler>(ClientMessageType.GameStoryPause);
        services.AddProtocolCommandHandler<GameUseRevivalCoinCommand, bool, GameUseRevivalCoinCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameUseRevivalCoin, typeof(UseRevivalCoinRequest)));
        services.RegisterProtocolMessageHandler<UseRevivalCoinHandler, UseRevivalCoinRequest, GameUseRevivalCoinCommand>(ClientMessageType.GameUseRevivalCoin);
        services.AddProtocolCommandHandler<GameCharacterDeathCommand, bool, GameCharacterDeathCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCharacterDeath, typeof(CharacterDeathRequest)));
        services.RegisterProtocolMessageHandler<CharacterDeathHandler, CharacterDeathRequest, GameCharacterDeathCommand>(ClientMessageType.GameCharacterDeath);
        services.RegisterProtocolMessageHandler<MonsterDeathHandler, MonsterDeathRequest, GameMonsterDeathCommand>(ClientMessageType.GameMonsterDeath);
        services.TryAddSingleton<BossTowerProjectionDelivery>();
        services.TryAddSingleton<BossTowerPartyCoordinator>();
        services.TryAddSingleton<BossTowerDepartureProjectionQueue>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBossTowerDepartureSink, BossTowerDepartureProjectionSink>());
        services.TryAddSingleton<BossTowerReturnSweepService>();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(provider => provider.GetRequiredService<BossTowerReturnSweepService>());
        services.AddCommandHandler<StartBossTowerCommand, BossTowerActionResult, StartBossTowerCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<LeaveBossTowerCommand, BossTowerActionResult, LeaveBossTowerCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<SynchronizeBossTowerLeaderCommand, BossTowerActionResult, SynchronizeBossTowerLeaderCommandHandler>(CommandExecutionKind.Transactional);
        services.TryAddSingleton<IllusionTowerProjectionDelivery>();
        services.TryAddSingleton<IllusionTowerPartyCoordinator>();
        services.TryAddSingleton<IllusionTowerDepartureProjectionQueue>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IIllusionTowerDepartureSink,
            IllusionTowerDepartureProjectionSink>());
        services.TryAddSingleton<IllusionTowerReturnSweepService>();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(provider =>
            provider.GetRequiredService<IllusionTowerReturnSweepService>());
        services.AddCommandHandler<LeaveIllusionTowerCommand, TowerActionResult,
            LeaveIllusionTowerCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<SynchronizeIllusionTowerLeaderCommand, TowerActionResult,
            SynchronizeIllusionTowerLeaderCommandHandler>(CommandExecutionKind.Transactional);
        services.AddProtocolCommandHandler<GameIllusionTowerStageCommand, bool, GameMapLoadingCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameIllusionTowerStage, typeof(IllusionTowerStageRequest)));
        services.RegisterProtocolMessageHandler<IllusionTowerStageHandler, IllusionTowerStageRequest, GameIllusionTowerStageCommand>(ClientMessageType.GameIllusionTowerStage);
        services.RegisterProtocolMessageHandler<GetItemHandler, GetItemRequest, GameGetItemCommand>(ClientMessageType.GameGetItem);
        services.RegisterProtocolMessageHandler<TutorialLevelUpHandler, TutorialLevelUpRequest, GameTutorialLevelUpCommand>(ClientMessageType.GameTutorialLevelUp);
        services.AddProtocolCommandHandler<GameSetPlayResultCommand, bool, GameSetPlayResultCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetPlayResult, typeof(SetPlayResultRequest)));
        services.RegisterProtocolMessageHandler<SetPlayResultHandler, SetPlayResultRequest, GameSetPlayResultCommand>(ClientMessageType.GameSetPlayResult);
        services.AddProtocolCommandHandler<GameBossDieCheckCommand, bool, GameBossDieCheckCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameBossDieCheck, typeof(BossDieCheckRequest)));
        services.RegisterProtocolMessageHandler<BossDieCheckHandler, BossDieCheckRequest, GameBossDieCheckCommand>(ClientMessageType.GameBossDieCheck);
        services.AddProtocolCommandHandler<GameScoreScrollStateCommand, bool, GameCardCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameScoreScrollState, typeof(ScoreScrollStateRequest)));
        services.RegisterProtocolMessageHandler<ScoreScrollStateHandler, ScoreScrollStateRequest, GameScoreScrollStateCommand>(ClientMessageType.GameScoreScrollState);
        services.AddProtocolCommandHandler<GameCardSelectRightStateCommand, bool, GameCardCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCardSelectRightState, typeof(CardSelectRightStateRequest)));
        services.RegisterProtocolMessageHandler<CardSelectRightStateHandler, CardSelectRightStateRequest, GameCardSelectRightStateCommand>(ClientMessageType.GameCardSelectRightState);
        services.AddProtocolCommandHandler<GameSelectCardCommand, bool, GameCardCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSelectCard, typeof(SelectCardRequest)));
        services.RegisterProtocolMessageHandler<SelectCardHandler, SelectCardRequest, GameSelectCardCommand>(ClientMessageType.GameSelectCard);
        services.AddInternalCommandHandler<DungeonCardLayoutCommand, bool, GameCardCommandHandler>();
        services.AddInternalCommandHandler<DungeonCardAutoFlipCommand, bool, GameCardCommandHandler>();
        services.AddProtocolCommandHandler<GameEplpCommand, bool, GameDungeonExitCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameEplpCommand, typeof(EplpCommandRequest)));
        services.RegisterProtocolMessageHandler<EplpCommandHandler, EplpCommandRequest, GameEplpCommand>(ClientMessageType.GameEplpCommand);
        services.AddProtocolCommandHandler<GameGiveUpGameCommand, bool, GameDungeonExitCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameGiveUpGame, typeof(GiveUpGameRequest)));
        services.RegisterProtocolMessageHandler<GiveUpGameHandler, GiveUpGameRequest, GameGiveUpGameCommand>(ClientMessageType.GameGiveUpGame);
        services.AddProtocolCommandHandler<GameBackToVillageCommand, bool, GameDungeonSelectionCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameBackToVillage, typeof(BackToVillageRequest)));
        services.RegisterProtocolMessageHandler<BackToVillageHandler, BackToVillageRequest, GameBackToVillageCommand>(ClientMessageType.GameBackToVillage);

        // Known reports that this server intentionally does not answer. A durability request is
        // not one of them: it changes a persisted equipment instance and has its own CMD reply.
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameUseSkill);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameSyncItemSpace);
        services.RegisterProtocolMessageHandler<SortLockHandler, SortLockRequest, GameSortLockCommand>(ClientMessageType.GameSortLock);
        services.RegisterProtocolMessageHandler<SortLockHandler, SortLockRequest, GameSortLockCommand>(ClientMessageType.GameSortUnlock);
        services.RegisterProtocolMessageHandler<WarehouseHandler, WarehouseRequest, GameWarehouseCommand>(ClientMessageType.GameCreateAccountCargo);
        services.AddGameCmdMessageDecoder<WarehouseDecoder>(0x0131, ClientMessageType.GameCreateAccountCargo);
        services.RegisterProtocolMessageHandler<WarehouseHandler, WarehouseRequest, GameWarehouseCommand>(ClientMessageType.GameUpgradeAccountCargo);
        services.AddGameCmdMessageDecoder<WarehouseDecoder>(0x0132, ClientMessageType.GameUpgradeAccountCargo);
        services.RegisterProtocolMessageHandler<WarehouseHandler, WarehouseRequest, GameWarehouseCommand>(ClientMessageType.GameDepositAccountCargo);
        services.AddGameCmdMessageDecoder<WarehouseDecoder>(0x0133, ClientMessageType.GameDepositAccountCargo);
        services.RegisterProtocolMessageHandler<WarehouseHandler, WarehouseRequest, GameWarehouseCommand>(ClientMessageType.GameWithdrawAccountCargo);
        services.AddGameCmdMessageDecoder<WarehouseDecoder>(0x0134, ClientMessageType.GameWithdrawAccountCargo);
        services.RegisterProtocolMessageHandler<WarehouseHandler, WarehouseRequest, GameWarehouseCommand>(ClientMessageType.GameUpgradeCargo);
        services.AddGameCmdMessageDecoder<WarehouseDecoder>(0x0198, ClientMessageType.GameUpgradeCargo);
        services.RegisterProtocolMessageHandler<WarehouseHandler, WarehouseRequest, GameWarehouseCommand>(ClientMessageType.GameUpgradeInventory);
        services.AddGameCmdMessageDecoder<WarehouseDecoder>(0x01AF, ClientMessageType.GameUpgradeInventory);
        services.RegisterProtocolMessageHandler<SortItemHandler, SortItemRequest, GameSortItemCommand>(ClientMessageType.GameSortItem);
        services.RegisterMessageHandler<DecreaseDurabilityHandler>(ClientMessageType.GameDecreaseDurability);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameOverflowInfo);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameLogDomainConnect);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameLogConnectProcess);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameCharacterStatistic);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameRequestBlacklist);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameFarmEventAction);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GamePi);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameRequestIgaVersion);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameFpsPerformanceStatistic);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameFrameLagPerDungeon);
        services.RegisterTelemetryMessageHandler<UnansweredReportHandler>(ClientMessageType.GameLagStatistics);
        services.RegisterProtocolMessageHandler<MoveItemSpaceHandler, MoveItemSpaceRequest, GameMoveItemSpaceCommand>(ClientMessageType.GameMoveItemSpace);
        services.RegisterProtocolMessageHandler<UseStackableHandler, UseStackableRequest, GameUseStackableCommand>(ClientMessageType.GameUseStackable);
        services.RegisterProtocolMessageHandler<RenameCreatureHandler, RenameCreatureRequest, GameRenameCreatureCommand>(ClientMessageType.GameRenameCreature);
        services.RegisterProtocolMessageHandler<HatchCreatureHandler, HatchCreatureRequest, GameHatchCreatureCommand>(ClientMessageType.GameHatchCreature);
        services.RegisterMessageHandler<CreatureResponseReportHandler>(ClientMessageType.GameCreatureResponseReport);
        services.RegisterMessageHandler<CreatureScriptMessageReportHandler>(ClientMessageType.GameCreatureScriptMessage);
        services.RegisterProtocolMessageHandler<QuestClearCouponHandler, QuestClearCouponRequest, GameQuestClearCouponCommand>(ClientMessageType.GameQuestClearCoupon);
        services.RegisterProtocolMessageHandler<LevelUpTicketHandler, LevelUpTicketRequest, GameLevelUpTicketCommand>(ClientMessageType.GameLevelUpTicket);
        services.RegisterProtocolMessageHandler<BuyItemHandler, BuyItemRequest, GameBuyItemCommand>(ClientMessageType.GameBuyItem,
            typeof(BuyItemRequest), typeof(HousingBuyItemRequest));
        services.RegisterProtocolMessageHandler<CeraShopPurchaseHandler, CeraShopPurchaseRequest, GameCeraShopPurchaseCommand>(ClientMessageType.GameCeraShopPurchase);
        services.RegisterProtocolMessageHandler<MagicBoxBatchOpeningHandler, MagicBoxBatchOpeningRequest, GameMagicBoxBatchOpeningCommand>(ClientMessageType.GameMagicBoxBatchOpening);
        services.RegisterProtocolMessageHandler<MagicBoxSingleOpeningHandler, MagicBoxSingleOpeningRequest, GameMagicBoxSingleOpeningCommand>(ClientMessageType.GameMagicBoxSingleOpening);
        services.RegisterProtocolMessageHandler<VendingMachineHandler, VendingMachineRequest, GameVendingMachineCommand>(ClientMessageType.GameVendingMachine);
        services.RegisterProtocolMessageHandler<LotteryOpeningHandler, LotteryOpeningRequest, GameLotteryOpeningCommand>(ClientMessageType.GameLotteryOpening);
        services.RegisterProtocolMessageHandler<PackageOpeningHandler, PackageOpeningRequest, GamePackageOpeningCommand>(ClientMessageType.GamePackageOpening);
        services.RegisterProtocolMessageHandler<AvatarPackageOpeningHandler, AvatarPackageOpeningRequest, GameAvatarPackageOpeningCommand>(ClientMessageType.GameAvatarPackageOpening);
        services.RegisterProtocolMessageHandler<GenCeraTicketHandler, GenCeraTicketRequest, GameGenCeraTicketCommand>(ClientMessageType.GameGenCeraTicket);
        services.RegisterProtocolMessageHandler<LearnSkillHandler, LearnSkillRequest, GameLearnSkillCommand>(ClientMessageType.GameLearnSkill);
        services.RegisterProtocolMessageHandler<SellItemHandler, SellItemRequest, GameSellItemCommand>(ClientMessageType.GameSellItem);
        services.RegisterProtocolMessageHandler<RepairItemHandler, RepairItemRequest, GameRepairItemCommand>(ClientMessageType.GameRepairItem);
        services.RegisterProtocolMessageHandler<DisjointItemHandler, DisjointItemRequest, GameDisjointItemCommand>(ClientMessageType.GameDisjointItem);
        services.RegisterProtocolMessageHandler<ReinforceItemHandler, ReinforceItemRequest, GameReinforceItemCommand>(ClientMessageType.GameReinforceItem);
        services.RegisterProtocolMessageHandler<EnchantByBeadHandler, EnchantByBeadRequest, GameEnchantByBeadCommand>(ClientMessageType.GameEnchantByBead);
        services.RegisterProtocolMessageHandler<TeleportHandler, TeleportRequest, GameTeleportCommand>(ClientMessageType.GameTeleport);
        services.RegisterProtocolMessageHandler<MagicSealHandler, MagicSealRequest, GameMagicSealCommand>(ClientMessageType.GameUnsealRandomOption);
        services.RegisterProtocolMessageHandler<MagicSealHandler, MagicSealRequest, GameMagicSealCommand>(ClientMessageType.GameChangeRandomOption);
        services.RegisterProtocolMessageHandler<CompoundItemHandler, CompoundItemRequest, GameCompoundItemCommand>(ClientMessageType.GameCompoundItem);
        services.RegisterProtocolMessageHandler<AvatarCompoundHandler, AvatarCompoundRequest, GameAvatarCompoundCommand>(ClientMessageType.GameAvatarCompound);
        services.RegisterProtocolMessageHandler<EpicCraftHandler, EpicCraftRequest, GameEpicCraftCommand>(ClientMessageType.GameEpicCraft);
        services.RegisterProtocolMessageHandler<ItemSocketHandler, ItemSocketRequest, GameItemSocketCommand>(ClientMessageType.GameEquipmentSocketOpen);
        services.RegisterProtocolMessageHandler<ItemSocketHandler, ItemSocketRequest, GameItemSocketCommand>(ClientMessageType.GameEquipmentEmblem);
        services.RegisterProtocolMessageHandler<ItemSocketHandler, ItemSocketRequest, GameItemSocketCommand>(ClientMessageType.GameAvatarSocketOpen);
        services.RegisterProtocolMessageHandler<ItemSocketHandler, ItemSocketRequest, GameItemSocketCommand>(ClientMessageType.GameAvatarEmblem);
        services.RegisterProtocolMessageHandler<ResetItemQualityHandler, ResetItemQualityRequest, GameResetItemQualityCommand>(ClientMessageType.GameResetItemQuality);
        services.RegisterProtocolMessageHandler<ResetRandomOptionHandler, ResetRandomOptionRequest, GameResetRandomOptionCommand>(ClientMessageType.GameResetRandomOption);
        services.RegisterProtocolMessageHandler<OutworldHandler, OutworldRequest, GameOutworldCommand>(ClientMessageType.GamePurifyItem);
        services.RegisterProtocolMessageHandler<OutworldHandler, OutworldRequest, GameOutworldCommand>(ClientMessageType.GameInvestAmplifyOption);
        services.RegisterProtocolMessageHandler<ForgingHandler, ForgingRequest, GameForgingCommand>(ClientMessageType.GameSeparateUpgrade);
        services.RegisterProtocolMessageHandler<CrystalContractHandler, CrystalContractRequest, GameCrystalContractCommand>(ClientMessageType.GameCrystalContract);
        services.RegisterProtocolMessageHandler<DeleteItemHandler, DeleteItemRequest, GameDeleteItemCommand>(ClientMessageType.GameDeleteItem);
        services.RegisterMessageHandler<ShopPurchaseCountHandler>(ClientMessageType.GameShopPurchaseCount);

        // Position and area: the only inputs that move a published character, and so the only
        // way the town its next entry reports can change.
        services.RegisterProtocolMessageHandler<SetUserPositionHandler, SetUserPositionRequest, GameSetUserPositionCommand>(ClientMessageType.GameSetUserPosition);
        services.RegisterProtocolMessageHandler<SetUserAreaHandler, SetUserAreaRequest, GameSetUserAreaCommand>(ClientMessageType.GameSetUserArea);
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
        services.AddGameNotiMessageEncoder<BossTowerInfoNotification, BossTowerInfoEncoder>();
        services.AddGameNotiMessageEncoder<BossTowerMapNotification, BossTowerMapEncoder>();
        services.AddGameNotiMessageEncoder<BossTowerRewardNotification, BossTowerRewardEncoder>();
        services.AddGameNotiMessageEncoder<BossTowerContinueNotification, BossTowerContinueEncoder>();
        services.AddGameNotiMessageEncoder<IllusionTowerInfoNotification, IllusionTowerInfoEncoder>();
        services.AddGameNotiMessageEncoder<IllusionTowerMapNotification, IllusionTowerMapEncoder>();
        services.AddGameNotiMessageEncoder<IllusionTowerRankingNotification, IllusionTowerRankingEncoder>();
        services.AddGameNotiMessageEncoder<IllusionTowerRewardNotification, IllusionTowerRewardEncoder>();
        services.AddGameNotiMessageEncoder<IllusionTowerContinueNotification, IllusionTowerContinueEncoder>();
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
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.EplpRechallenge.EplpRechallengeNotification,
            Dfo.Server.Protocol.Game.EplpRechallenge.EplpRechallengeEncoder>();
        services.AddGameNotiMessageEncoder<SecretShopNpcNotification, SecretShopNpcEncoder>();
        services.AddGameNotiMessageEncoder<DespairClearRewardNotification, DespairClearRewardEncoder>();
        services.AddGameNotiMessageEncoder<SaoCurrentFloorNotification, SaoCurrentFloorEncoder>();
        services.AddDomainSubscriber<SaoTowerCleared, SaoTowerNotificationSubscriber>("sao-tower.notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<SouthernDaleInventoryChanged, Dfo.Server.Protocol.Game.SouthernDale.SouthernInventoryNotificationSubscriber>(
            "southern-dale.inventory-notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<SouthernDaleDailyRefillCompleted, Dfo.Server.Protocol.Game.SouthernDale.SouthernDailyRefillNotificationSubscriber>(
            "southern-dale.daily-refill-notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.SouthernDale.SouthernInventoryRefresh,
            Dfo.Server.Protocol.Game.SouthernDale.SouthernInventoryRefreshHandler>();
        services.AddDomainSubscriber<BossTowerTicketRefilled, Dfo.Server.Protocol.Game.BossTowerTicket.BossTowerTicketNotificationSubscriber>(
            "boss-tower.ticket-notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.BossTowerTicket.BossTowerTicketRefresh,
            Dfo.Server.Protocol.Game.BossTowerTicket.BossTowerTicketRefreshHandler>();
        services.AddDomainSubscriber<TauKingdomTicketRefilled, Dfo.Server.Protocol.Game.TauKingdom.TauTicketNotificationSubscriber>(
            "tau-kingdom.ticket-notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.TauKingdom.TauTicketRefresh,
            Dfo.Server.Protocol.Game.TauKingdom.TauTicketRefreshHandler>();
        services.AddNotificationHandler<SaoTowerRefresh, SaoTowerRefreshHandler>();
        services.AddGameNotiMessageEncoder<DespairApcInfoNotification, DespairApcInfoEncoder>();
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
        services.AddGameCmdMessageDecoder<IllusionTowerStageDecoder>(
            GameProtocol.IllusionTowerStageType, ClientMessageType.GameIllusionTowerStage);
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
        services.AddProtocolCommandHandler<GameDisjointItemCommand, EquipmentDisjointResult, GameDisjointItemCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameDisjointItem, typeof(DisjointItemRequest)));
        services.AddGameCmdMessageDecoder<DisjointItemMessageDecoder>(
            GameProtocol.DisjointItemType, ClientMessageType.GameDisjointItem);
        services.AddProtocolCommandHandler<GameReinforceItemCommand, EquipmentReinforcementResult, GameReinforceItemCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameReinforceItem, typeof(ReinforceItemRequest)));
        services.AddGameCmdMessageDecoder<ReinforceItemMessageDecoder>(
            GameProtocol.ReinforceItemType, ClientMessageType.GameReinforceItem);
        services.AddProtocolCommandHandler<GameEnchantByBeadCommand, EquipmentEnchantResult, GameEnchantByBeadCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameEnchantByBead, typeof(EnchantByBeadRequest)));
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
        services.AddGameCmdMessageDecoder<CompoundItemDecoder>(GameProtocol.CompoundItemType, ClientMessageType.GameCompoundItem);
        services.AddGameCmdMessageDecoder<EpicCraftDecoder>(GameProtocol.EpicCraftType, ClientMessageType.GameEpicCraft);
        services.AddGameCmdMessageDecoder<ItemSocketDecoder>(0x037A, ClientMessageType.GameEquipmentSocketOpen);
        services.AddGameCmdMessageDecoder<ItemSocketDecoder>(0x0379, ClientMessageType.GameEquipmentEmblem);
        services.AddGameCmdMessageDecoder<ItemSocketDecoder>(0x00CE, ClientMessageType.GameAvatarSocketOpen);
        services.AddGameCmdMessageDecoder<ItemSocketDecoder>(0x00C9, ClientMessageType.GameAvatarEmblem);
        services.AddGameCmdMessageDecoder<ResetItemQualityDecoder>(0x0051, ClientMessageType.GameResetItemQuality);
        services.AddGameCmdMessageDecoder<ResetRandomOptionDecoder>(0x01C8, ClientMessageType.GameResetRandomOption);
        services.AddGameCmdMessageDecoder<AvatarCompoundDecoder>(0x0063, ClientMessageType.GameAvatarCompound);
        services.AddGameCmdMessageDecoder<PackageOpeningDecoder>(0x00A0, ClientMessageType.GamePackageOpening);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.ReviveCoin.DailyReviveCoinHandler, Dfo.Server.Protocol.Game.ReviveCoin.DailyReviveCoinRequest, Dfo.Server.Protocol.Game.ReviveCoin.GameDailyReviveCoinCommand>(ClientMessageType.GameDailyReviveCoin);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.ReviveCoin.DailyReviveCoinDecoder>(0x00CF, ClientMessageType.GameDailyReviveCoin);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.ReviveCoin.DailyReviveCoinResponse, Dfo.Server.Protocol.Game.ReviveCoin.DailyReviveCoinEncoder>();
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.ReviveCoin.GameDailyReviveCoinCommand, DailyReviveCoinResult, Dfo.Server.Protocol.Game.ReviveCoin.GameDailyReviveCoinCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameDailyReviveCoin, typeof(Dfo.Server.Protocol.Game.ReviveCoin.DailyReviveCoinRequest)));
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
        services.AddGameCmdMessageDecoder<TeleportDecoder>(0x00ED, ClientMessageType.GameTeleport);
        services.AddGameCmdMessageDecoder<QuestClearCouponDecoder>(
            GameProtocol.QuestClearCouponType, ClientMessageType.GameQuestClearCoupon);
        services.AddGameCmdMessageDecoder<LevelUpTicketDecoder>(GameProtocol.LevelUpTicketType, ClientMessageType.GameLevelUpTicket);
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
        services.AddGameNotiMessageEncoder<ForcedTownPositionNotification, ForcedTownPositionEncoder>();
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
        services.AddGameCmdMessageEncoder<TeleportResponse, TeleportEncoder>();
        services.AddGameNotiMessageEncoder<TeleportCooldownNotification, TeleportCooldownEncoder>();
        services.AddGameCmdMessageEncoder<QuestClearCouponResponse, QuestClearCouponEncoder>();
        services.AddGameCmdMessageEncoder<LevelUpTicketResponse, LevelUpTicketEncoder>();
        services.AddGameCmdMessageEncoder<BuyItemResponse, BuyItemResponseEncoder>();
        services.AddGameCmdMessageEncoder<CeraShopPurchaseResponse, CeraShopPurchaseResponseEncoder>();
        services.AddGameCmdMessageEncoder<MagicBoxBatchOpeningResponse, MagicBoxBatchOpeningEncoder>();
        services.AddGameCmdMessageEncoder<MagicBoxSingleOpeningResponse, MagicBoxSingleOpeningEncoder>();
        services.AddGameCmdMessageEncoder<VendingMachineResponse, VendingMachineEncoder>();
        services.AddGameCmdMessageEncoder<LotteryOpeningResponse, LotteryOpeningEncoder>();
        services.AddGameCmdMessageEncoder<CompoundItemResponse, CompoundItemEncoder>();
        services.AddGameCmdMessageEncoder<EpicCraftResponse, EpicCraftEncoder>();
        services.AddGameCmdMessageEncoder<ItemSocketResponse, ItemSocketEncoder>();
        services.AddGameCmdMessageEncoder<ResetItemQualityResponse, ResetItemQualityEncoder>();
        services.AddGameCmdMessageEncoder<ResetRandomOptionResponse, ResetRandomOptionEncoder>();
        services.AddGameCmdMessageEncoder<AvatarCompoundResponse, AvatarCompoundEncoder>();
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
        services.AddGameCmdMessageEncoder<WarehouseResponse, WarehouseResponseEncoder>();
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
        services.AddGameNotiMessageEncoder<HellPartyMonsterInfoNotification, HellPartyMonsterInfoEncoder>();
        services.AddGameCmdMessageEncoder<BackToVillageResponse, BackToVillageResponseEncoder>();
        services.AddGameNotiMessageEncoder<LoadingCompleteNotification, LoadingCompleteEncoder>();
        services.AddGameCmdMessageEncoder<HiddenCharacterInfoResponse, HiddenCharacterInfoResponseEncoder>();
        services.AddGameCmdMessageEncoder<RosterFollowUpResponse, RosterFollowUpResponseEncoder>();
    }
}
