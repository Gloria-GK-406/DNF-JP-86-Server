using Dfo.Server.Application.UsageQuotas;
using Dfo.Server.Application.Marriage;
using Dfo.Server.Application.Events;
using Dfo.Server.Application.Rental;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Initialization;
using Dfo.Server.Application.Scheduling;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Application.Mail;
using Dfo.Server.Application.Configuration;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Effects;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Settlement;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World.IllusionTower;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Application.World.BossTower;
using Dfo.Server.Abstractions;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Channel;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Creatures;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.HttpOperations;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Settlement;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Channel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application;

public sealed class ApplicationModule : IServerModule
{
    public string Name => "Application";

    /// <summary>Reads <c>Experience:PremiumRates</c> as premium type to added rate.</summary>
    /// <remarks>
    /// A malformed entry is skipped rather than failing startup: this is an operator's dial, and a
    /// server that will not boot because one rate was mistyped is worse than one that boots paying
    /// the rates it could read.
    /// </remarks>
    private static ExperienceEntitlementPolicy ExperienceEntitlementPolicyFrom(
        IConfiguration configuration)
    {
        var rates = new Dictionary<int, double>();
        foreach (var entry in configuration.GetSection("Experience:PremiumRates").GetChildren())
        {
            if (int.TryParse(entry.Key, out var premiumType)
                && double.TryParse(entry.Value, out var rate)
                && rate > 0)
            {
                rates[premiumType] = rate;
            }
        }

        return rates.Count == 0 ? ExperienceEntitlementPolicy.None : new(rates);
    }

    private static AltarDungeonPolicy AltarPolicyFrom(IConfiguration configuration)
    {
        var windows = configuration.GetSection("AltarDungeons:OpeningWindows").GetChildren().Select(row =>
        {
            if (!int.TryParse(row["DungeonId"], out var id) || id is not (11006 or 11007)
                || !TimeOnly.TryParse(row["Opens"], System.Globalization.CultureInfo.InvariantCulture, out var opens)
                || !TimeOnly.TryParse(row["Closes"], System.Globalization.CultureInfo.InvariantCulture, out var closes))
                throw new InvalidDataException("Invalid altar opening window configuration.");
            return new AltarOpeningWindow(id, opens, closes);
        }).ToArray();
        return new() { OpeningWindows = windows };
    }

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.AddTaskScheduling();
        services.AddOnlineExpiredItemCleanup();
        services.AddCharacterNameTags();
        services.AddCharacters(configuration);
        services.AddEventDispatching();
        services.AddUsageQuotas();
        services.TryAddSingleton(AltarPolicyFrom(configuration));
        services.TryAddSingleton<IAltarDungeonRuleService, AltarDungeonRuleService>();
        services.TryAddSingleton<IGuardianAltarRuleService, GuardianAltarRuleService>();
        services.TryAddSingleton<GuardianAltarRuntimeStore>();
        services.TryAddSingleton<GuardianAltarDomainService>();
        services.TryAddSingleton<IGuardianAltarDomainService>(sp => sp.GetRequiredService<GuardianAltarDomainService>());
        services.AddDomainSubscriber<DungeonParticipantLeft, GuardianAltarDomainService>("guardian-altar.departure", Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.Reliable);
        services.TryAddSingleton<AltarDungeonEngine>();
        services.TryAddSingleton<EndlessAltarDomainService>();
        services.TryAddSingleton<UltimateAltarDomainService>();
        services.TryAddSingleton<AltarDungeonDomainService>();
        services.TryAddSingleton<IAltarDungeonDomainService>(sp => sp.GetRequiredService<AltarDungeonDomainService>());
        services.AddDomainEvent<AltarChallengeCompleted>("altar.challenge-completed", 1);
        services.AddDomainSubscriber<AltarChallengeCompleted, AltarDungeonQuestSubscriber>("quest.altar-complete", Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.Reliable);
        services.AddDomainSubscriber<DungeonParticipantLeft, AltarDungeonDomainService>("altar.departure", Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.Reliable);
        services.AddMarriage(configuration);
        services.AddInventory();
        services.TryAddSingleton<CoupleDungeonRule>();
        services.TryAddSingleton<ICoupleDungeonDomainService, CoupleDungeonDomainService>();
        services.TryAddSingleton<IHerasRuleService, HerasRuleService>();
        services.TryAddSingleton(new LostLandRuntimePolicy(configuration.GetValue<int?>("LostLand:LimitSecondsOverride"),
            configuration.GetValue<bool>("LostLand:UnlimitedEntriesForTesting")));
        services.TryAddSingleton<ILostLandRuleService, LostLandRuleService>();
        services.TryAddSingleton<ILostLandFeverRuleService, LostLandFeverRuleService>();
        services.TryAddSingleton<ILostLandDropRuleService, LostLandDropRuleService>();
        services.TryAddSingleton<ILostLandDungeonDomainService, LostLandDungeonDomainService>();
        services.TryAddSingleton<ILostLandConsumableRuleService, LostLandConsumableRuleService>();
        services.TryAddSingleton<ILostLandConsumableDomainService, LostLandConsumableDomainService>();
        services.TryAddSingleton<IHerasDungeonDomainService, HerasDungeonDomainService>();
        services.TryAddSingleton<IHerasQuotaDomainService, HerasQuotaDomainService>();
        services.TryAddSingleton<IGiantDragonRuleService, GiantDragonRuleService>();
        services.TryAddSingleton<IGiantDragonDomainService, GiantDragonDomainService>();
        services.TryAddSingleton<ISouthernDaleRuleService, SouthernDaleRuleService>();
        services.TryAddSingleton<IDarkElfRuinsRuleService, DarkElfRuinsRuleService>();
        services.TryAddSingleton<IDarkElfRuinsDomainService, DarkElfRuinsDomainService>();
        services.AddSingleton<IDungeonBossClearHook>(sp => sp.GetRequiredService<IDarkElfRuinsDomainService>());
        services.AddDomainEvent<DarkElfRuinsInventoryChanged>("darkelf-ruins.inventory-changed", 1);
        services.TryAddSingleton<ISealedTreasureRuleService, SealedTreasureRuleService>();
        services.TryAddSingleton<IGoldSilverHornRuleService, GoldSilverHornRuleService>();
        services.TryAddSingleton<IBossTowerRuleService, BossTowerRuleService>();
        services.TryAddSingleton<IBossTowerTicketDomainService, BossTowerTicketDomainService>();
        services.AddDomainEvent<BossTowerTicketRefilled>("boss-tower.ticket-refilled", 1);
        services.AddOnlineTicketRefill<IBossTowerTicketDomainService, IBossTowerRuleService>("boss-tower.ticket-refill.online");
        services.TryAddSingleton<IGoldSilverHornDomainService, GoldSilverHornDomainService>();
        services.TryAddSingleton<ISealedTreasureDomainService, SealedTreasureDomainService>();
        services.TryAddSingleton<ISouthernDaleDomainService, SouthernDaleDomainService>();
        services.AddSingleton<IDungeonBossClearHook>(sp => sp.GetRequiredService<ISouthernDaleDomainService>());
        services.TryAddSingleton<ISouthernDaleQuestDomainService, SouthernDaleQuestDomainService>();
        services.AddDomainEvent<SouthernDaleInventoryChanged>("southern-dale.inventory-changed", 1);
        services.AddDomainEvent<SouthernDaleDailyRefillCompleted>("southern-dale.daily-refill-completed", 1);
        services.AddOnlineTicketRefill<ISouthernDaleDomainService, ISouthernDaleRuleService>("southern-dale.ticket-refill.online");
        services.TryAddSingleton<ITauKingdomRuleService, TauKingdomRuleService>();
        services.TryAddSingleton<ITauKingdomDomainService, TauKingdomDomainService>();
        services.AddDomainEvent<TauKingdomTicketRefilled>("tau-kingdom.ticket-refilled", 1);
        services.AddOnlineTicketRefill<ITauKingdomDomainService, ITauKingdomRuleService>("tau-kingdom.ticket-refill.online");
        services.TryAddSingleton<ISaoTowerRuleService, SaoTowerRuleService>();
        services.TryAddSingleton<ISaoTowerDomainService, SaoTowerDomainService>();
        services.AddSingleton<IDungeonBossClearHook>(sp => sp.GetRequiredService<ISaoTowerDomainService>());
        services.AddDomainEvent<SaoTowerCleared>("sao-tower.cleared", 1);
        services.TryAddSingleton<IDespairTowerRuleService, DespairTowerRuleService>();
        services.TryAddSingleton<IDespairTowerDomainService, DespairTowerDomainService>();
        services.TryAddSingleton<IDimensionDungeonRuleService, DimensionDungeonRuleService>();
        services.TryAddSingleton<IDimensionDungeonDomainService, DimensionDungeonDomainService>();
        services.AddMirrorAradQuests();
        services.AddMapClearQuests();
        Dfo.Server.Application.Achievements.AchievementRegistration.AddAchievements(services);
        services.AddDomainEvent<MailCreated>("mail.created", 1);
        services.AddMail();
        services.AddEvents(configuration);
        services.TryAddSingleton(new ChannelListenerOptions(
            configuration.GetSection("Networking:GamePorts")
                .GetChildren()
                .Select(port => int.TryParse(port.Value, out var value) ? value : 0)
                .ToArray(),
            AllowAdvertisedPortFallback:
                !bool.TryParse(configuration["LauncherAuth:Enabled"], out var launcherEnabled)
                || !launcherEnabled));

        services.TryAddSingleton(ExperienceTuningSource.From(configuration));

        services.TryAddSingleton(ExperienceEntitlementPolicyFrom(configuration));

        // What the channel adds is not registered here at all any more. It is declared in
        // channel_info.etc, per channel and per dungeon group, and is read from there -- it was
        // an operator dial only for as long as this server believed the archive was silent
        // about it.
        // A measurement switch, off unless an operator asks for it. See
        // ExperienceDiagnostics: it makes a kill pay a distinct amount into
        // every named line of the frame so the client labels them itself.
        services.TryAddSingleton(
            new ExperienceDiagnostics(
                bool.TryParse(configuration["Experience:LabelProbe"], out var probe) && probe));
        // What a kill, a clear and a channel are worth, and how much of it a level costs. The
        // one reader of the four archive declarations and the two operator dials behind them.
        services.TryAddSingleton<IExperienceRuleService, ExperienceRuleService>();
        services.TryAddSingleton<IExperienceBonusDomainService, ExperienceBonusDomainService>();
        services.AddUsers();
        services.AddQuests();
        services.TryAddSingleton<IQuestHelperGroundBonusDomainService, QuestHelperGroundBonusDomainService>();
        services.TryAddSingleton<IQuestCompletionSettlementDomainService, QuestCompletionSettlementService>();
        services.TryAddSingleton<IDungeonStructureRuleService, DungeonStructureRuleService>();
        services.TryAddSingleton<IDungeonEntryDecisionDomainService, DungeonEntryDecisionDomainService>();
        services.TryAddSingleton<IDungeonEntryRoutingDomainService, DungeonEntryRoutingDomainService>();
        services.TryAddSingleton<IHellDungeonEntryRuleService, HellDungeonEntryRuleService>();
        services.TryAddSingleton<IHellRewardRuleService, HellRewardRuleService>();
        services.TryAddSingleton<IHellRewardDomainService, HellRewardDomainService>();
        services.TryAddSingleton(new HellRewardOptions
        {
            EquipmentRatePercent = int.TryParse(configuration["HellRewards:EquipmentRatePercent"], out var hellRate)
                ? hellRate : 100,
        });
        services.TryAddSingleton<IHellDungeonEntryDomainService, HellDungeonEntryDomainService>();
        services.TryAddSingleton<IDungeonEntryIntentDomainService, DungeonEntryIntentDomainService>();
        services.TryAddSingleton<IDungeonLifecycleDomainService, DungeonLifecycleDomainService>();
        services.TryAddSingleton<IMirrorDungeonRuleService, MirrorDungeonRuleService>();
        services.TryAddSingleton<IMirrorDungeonEntryDomainService, MirrorDungeonEntryDomainService>();
        services.AddDomainEvent<DungeonParticipantLeft>("dungeon.participant-left", 1);
        services.TryAddSingleton<ITournamentDungeonRuleService, TournamentDungeonRuleService>();
        services.TryAddSingleton<TournamentDungeonDomainService>();
        services.TryAddSingleton<ITournamentDungeonDomainService>(sp => sp.GetRequiredService<TournamentDungeonDomainService>());
        services.AddDomainSubscriber<DungeonParticipantLeft, TournamentDungeonDomainService>("tournament.departure", Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.Reliable);
        services.TryAddSingleton<ICycleDungeonRuleService, CycleDungeonRuleService>();
        services.TryAddSingleton<ICycleDungeonEntryDomainService, CycleDungeonEntryDomainService>();
        services.TryAddSingleton<IDungeonSettlementRuleService, DungeonSettlementRuleService>();
        services.TryAddSingleton<IDungeonClearDomainService, DungeonClearDomainService>();
        services.AddChannel();
        services.TryAddSingleton<ITutorialRewardRuleService, TutorialRewardRuleService>();
        // What a room declares, and what a run standing in it makes of that: the actor numbering,
        // the monster-or-ally boundary, and which of the declared pieces are still on the floor.
        services.TryAddSingleton<IDungeonRoomRuleService, DungeonRoomRuleService>();
        services.TryAddSingleton<IDungeonRoomMechanismRuleService, DungeonRoomMechanismRuleService>();
        services.AddDungeonDeadlineScheduler();
        services.AddRuntimeEventSubscriber<DungeonRoomMechanismsChanged, DungeonRoomMechanismNotificationSubscriber>();
        services.TryAddSingleton<IDungeonParticipationDomainService, DungeonParticipationDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardRuleService, Dfo.Server.Application.World.BossTower.BossTowerRewardRuleService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.World.BossTower.IBossTowerRewardDomainService, Dfo.Server.Application.World.BossTower.BossTowerRewardDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.World.BossTower.IBossTowerDomainService, Dfo.Server.Application.World.BossTower.BossTowerDomainService>();
        services.AddIllusionTower();
        services.TryAddSingleton<IObjectDropRuleService, ObjectDropRuleService>();
        services.AddCreatures();
        services.AddAccounts();
        services.TryAddSingleton<Dfo.Server.Abstractions.Configuration.IDailyResetRuleService, DailyResetRuleService>();
        services.AddRental();
        services.AddDungeonRuns();
        services.TryAddSingleton<IDungeonRevivalDomainService, DungeonRevivalDomainService>();

        // The compensation seam and its first-phase implementation, which never compensates. It is
        // registered rather than left out so that everything upstream is written against the real
        // shape from the first day.
        // What an occasion drops: the one reader of the three drop tables and the only caller of
        // DropRules. The compensation seam below stays outside it, because what a character is
        // owed is state that crosses runs.
        services.TryAddSingleton<IDropRuleService, DropRuleService>();
        services.TryAddSingleton<IIndependentDropRuleService, IndependentDropRuleService>();
        services.TryAddSingleton(new IndependentDropOptions
        {
            DebugGuaranteeGrowthBossEquipment = bool.Parse(configuration["IndependentDrops:DebugGuaranteeGrowthBossEquipment"] ?? "false"),
        });
        services.TryAddSingleton<IExtraDropRuleService, ExtraDropRuleService>();
        services.TryAddSingleton<IWorldDropRuleService, WorldDropRuleService>();
        services.TryAddSingleton<IMonsterDeclaredDropRuleService, MonsterDeclaredDropRuleService>();
        services.TryAddSingleton(new WorldDropOptions
        {
            ProbabilityBasis = configuration["WorldDrops:ProbabilityBasis"] is { } configuredWorldBasis
                ? int.Parse(configuredWorldBasis, System.Globalization.CultureInfo.InvariantCulture) : 100000,
            DebugClearCubeOnMiss = bool.Parse(configuration["WorldDrops:DebugClearCubeOnMiss"] ?? "false"),
        });
        services.TryAddSingleton<IDropRateAdjuster, NoDropRateAdjustment>();
        // The one number the archive does not declare. See DropRateOptions for the two candidate
        // readings and why neither could be chosen from the files.
        services.TryAddSingleton(new DropRateOptions
        {
            RateBasis = int.TryParse(configuration["DropRates:RateBasis"], out var basis)
                && basis > 0
                ? basis
                : new DropRateOptions().RateBasis,
        });
        // What a clear's card face holds: the one reader of the clear-reward rate table and the
        // only caller of ClearRewardRules. It is handed three content sources and a basis, and
        // nothing that could tell it anything about the world. The third joined when a dungeon
        // became able to switch its own paid card off; the count here said two until then.
        services.TryAddSingleton<IClearRewardRuleService, ClearRewardRuleService>();
        // The card flip's own undeclared number, kept apart from the monster side's rather than
        // shared with it: the two files declare different sections and there is nothing saying
        // their numerators are over the same thing. See ClearRewardRateOptions for the
        // composition this basis belongs to and why neither could be read out of the archive.
        services.TryAddSingleton(new ClearRewardRateOptions
        {
            DebugGuaranteePaidEquipment = bool.TryParse(
                configuration["ClearRewardRates:DebugGuaranteePaidEquipment"], out var guaranteedCard)
                && guaranteedCard,
            RateBasis =
                int.TryParse(configuration["ClearRewardRates:RateBasis"], out var cardBasis)
                && cardBasis > 0
                    ? cardBasis
                    : new ClearRewardRateOptions().RateBasis,
        });
        // Where a clear's two faces are decided and where each one's seed is prepared. It sits
        // above the rules because how many faces a clear has, and which facts each is drawn from,
        // is a question about the world -- which the rules are not allowed to ask.
        services.TryAddSingleton<ICardRewardDomainService, CardRewardDomainService>();
        services.TryAddSingleton<ICardFlipCostDomainService, CardFlipCostDomainService>();
        // Entering a dungeon and walking between its rooms.
        services.TryAddSingleton<IDungeonSelectionRuleService, DungeonSelectionRuleService>();
        services.TryAddSingleton<IDungeonEntryMaterialsDomainService, DungeonEntryMaterialsDomainService>();
        services.TryAddSingleton<IDungeonAdmissionRuleService, DungeonAdmissionRuleService>();
        services.TryAddSingleton<IDungeonAdmissionDomainService, DungeonAdmissionDomainService>();
        services.TryAddSingleton<DungeonVisitDomainService>();
        services.TryAddSingleton<IDungeonVisitDomainService, TransactionalDungeonVisitDomainService>();
        services.TryAddSingleton<IDungeonDifficultyRuleService, DungeonDifficultyRuleService>();
        // Runtime kill interpretation reads only the published Cell snapshot.
        services.TryAddSingleton<IMonsterKillDomainService, MonsterKillDomainService>();
        services.TryAddSingleton<IQuestItemDropDomainService, QuestItemDropDomainService>();
        services.TryAddSingleton<ICardFlipDomainService, CardFlipDomainService>();
        services.TryAddSingleton<IMonsterKillRewardsDomainService, MonsterKillRewardsDomainService>();
        services.TryAddSingleton<IMonsterDeathDomainService, MonsterDeathDomainService>();
        services.TryAddSingleton<IDungeonPremiumBuffDomainService, DungeonPremiumBuffDomainService>();
        // Whether a character may stand where they say, and what a tutorial step declares.
        services.TryAddSingleton<ITownMovementDomainService, TownMovementDomainService>();
        services.TryAddSingleton<ITownTransitDomainService, TownTransitDomainService>();
        services.TryAddSingleton<ITutorialRewardDomainService, TutorialRewardDomainService>();
        // The seams the command handlers reach the rules through, so that none of them holds a
        // rule service of its own.
        services.TryAddSingleton<IGroundPickupDomainService, GroundPickupDomainService>();
        services.AddDomainEvent<ItemMirrorRemoved>("inventory.mirror-removed", 1);
        services.AddEffects();
        services.TryAddSingleton<IQuestClearCouponSettlementDomainService, QuestClearCouponSettlementService>();
        services.TryAddSingleton<
            IExperienceValuationDomainService, ExperienceValuationDomainService>();
        services.TryAddSingleton<IExperienceCalculationDomainService, ExperienceCalculationDomainService>();
        services.TryAddSingleton<MonsterDropDomainService>();
        services.TryAddSingleton<IMonsterDropDomainService>(services =>
            services.GetRequiredService<MonsterDropDomainService>());
        services.TryAddSingleton<IDungeonCellFactory, DungeonCellFactory>();
        services.TryAddSingleton<IQuestEnemyDropDomainService, QuestEnemyDropDomainService>();
        services.TryAddSingleton<IQuestEnemyDropRuleService, QuestEnemyDropRuleService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Notices.INoticeStateDomainService, Dfo.Server.Application.Notices.NoticeStateDomainService>();
        services.AddDomainEvent<ClientAppearanceSettingsChanged>("settings.appearance-changed", 1);
        services.TryAddSingleton<Dfo.Server.Application.World.IllusionTower.TowerInventoryDomainService>();
        services.AddAdministration();
        services.AddInitialization();
        services.AddSingleton<IItemCatalogSelectionRuleService>(p => p.GetRequiredService<IDailyRefillConfigurationRuleService>());
        services.TryAddSingleton<IWorldContentEntryRuleService, WorldContentEntryRuleService>();
        services.TryAddSingleton<IEntryPlacementRuleService, EntryPlacementRuleService>();
        services.AddSessions();
        services.TryAddSingleton<IRoomMechanismMaintenanceDomainService, RoomMechanismMaintenanceDomainService>();
        services.AddAuthentication(configuration);
        services.AddParties();
        services.TryAddSingleton<OnlineCharacterLocationDomainService>();
        services.TryAddSingleton<ITownAreaDomainService>(provider =>
            provider.GetRequiredService<OnlineCharacterLocationDomainService>());
        services.AddCommandDispatching();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Dfo.Server.Application.Commands.INetworkConnectionLifecycleHandler, LoginSessionConnectionObserver>());
        services.AddHttpOperationCommands();
        services.AddNetworkMessageDispatching();
        services.AddNotificationDispatching();
        AddContentWarmups(services);
    }

    /// <summary>Rules that load on first use and are warmed at startup (spec appendix B "启动预热").</summary>
    private static void AddContentWarmups(IServiceCollection services)
    {
        services.AddContentWarmup<IPremiumServiceRuleService>()
            .AddContentWarmup<IQuestHelperPuppetRuleService>()
            .AddContentWarmup<IExperienceRuleService>()
            .AddContentWarmup<Dfo.Server.Abstractions.Events.IJoustRuleService>()
            .AddContentWarmup<ITournamentDungeonRuleService>()
            .AddContentWarmup<IEpicBookRuleService>()
            .AddContentWarmup<IDropRuleService>()
            .AddContentWarmup<ILostLandDropRuleService>();
        services.AddSingleton<Dfo.Server.Abstractions.Content.IContentWarmup, CharacterSkillRuleWarmup>();
    }
}
