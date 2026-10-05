using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Application.Mail;
using Dfo.Server.Application.Configuration;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Application.Effects;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.World.IllusionTower;
using Dfo.Server.Abstractions;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Channel;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Creatures;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.OnlineUsers;
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

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new ChannelListenerOptions(
            configuration.GetSection("Networking:GamePorts")
                .GetChildren()
                .Select(port => int.TryParse(port.Value, out var value) ? value : 0)
                .ToArray(),
            AllowAdvertisedPortFallback:
                !bool.TryParse(configuration["LauncherAuth:Enabled"], out var launcherEnabled)
                || !launcherEnabled));
        services.TryAddSingleton<ICharacterStatRuleService, CharacterStatRuleService>();
        services.TryAddSingleton<CharacterSkillRuleService>();
        services.TryAddSingleton<ICharacterSkillRuleService>(sp => sp.GetRequiredService<CharacterSkillRuleService>());
        services.TryAddSingleton<ISkillPointDefinitionSource>(sp => sp.GetRequiredService<CharacterSkillRuleService>());

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
        services.TryAddSingleton<SpiritPotionDomainService>();
        services.TryAddSingleton<ISpiritPotionDomainService>(sp => sp.GetRequiredService<SpiritPotionDomainService>());
        services.TryAddSingleton<IExperienceBonusDomainService, ExperienceBonusDomainService>();
        services.TryAddSingleton<ICharacterExperienceDomainService, CharacterExperienceDomainService>();
        // One store, three services. The store keeps the collections whose invariant spans them
        // behind a single lock; the services are the three responsibilities read off it.
        services.TryAddSingleton<OnlineStateRegistry>();
        services.TryAddSingleton(
            provider => new OnlineUserDomainService(
                provider.GetRequiredService<OnlineStateRegistry>()));
        services.TryAddSingleton<IOnlineUserDomainService>(
            provider => provider.GetRequiredService<OnlineUserDomainService>());
        services.TryAddSingleton<IOnlineUserQueryDomainService>(
            provider => provider.GetRequiredService<OnlineUserDomainService>());
        services.TryAddSingleton<IGameSessionEndpointDomainService>(provider =>
            new GameSessionEndpointDomainService(
                provider.GetRequiredService<OnlineStateRegistry>()));
        services.TryAddSingleton<IActiveCharacterDomainService>(
            provider => new ActiveCharacterDomainService(
                provider.GetRequiredService<OnlineStateRegistry>()));
        // The gate is keyed on the character rather than the account because the operations
        // that need it span accounts: a party pays an entry cost together.
        services.TryAddSingleton<UserExecutionGate<CharacterId>>();
        // What the archive declares about quests, and the shared parameters a reward is worked
        // out from. One reader for both, because no quest outcome is decided without both.
        services.TryAddSingleton<IQuestContentRuleService, QuestContentRuleService>();
        services.TryAddSingleton<ISeekingQuestProgressDomainService, SeekingQuestProgressDomainService>();
        services.TryAddSingleton<IQuestMaterialRequirementDomainService, QuestMaterialRequirementDomainService>();
        services.TryAddSingleton<IQuestHelperGroundBonusDomainService, QuestHelperGroundBonusDomainService>();
        services.TryAddSingleton<IQuestRewardRuleService, QuestRewardRuleService>();
        services.TryAddSingleton<IQuestCompletionEffectRuleService, QuestCompletionEffectRuleService>();
        services.TryAddSingleton<IQuestCompletionDomainService, QuestCompletionDomainService>();
        services.TryAddSingleton<IDungeonLayoutRuleService, DungeonLayoutRuleService>();
        services.TryAddSingleton<IDungeonEntryIntentDomainService, DungeonEntryIntentDomainService>();
        services.TryAddSingleton<IDungeonSettlementRuleService, DungeonSettlementRuleService>();
        services.TryAddSingleton<IDungeonClearDomainService, DungeonClearDomainService>();
        // What the server declares about its channels, and what a tutorial step hands over.
        services.TryAddSingleton<IChannelContentRuleService, ChannelContentRuleService>();
        services.TryAddSingleton<ITutorialRewardRuleService, TutorialRewardRuleService>();
        // What a room declares, and what a run standing in it makes of that: the actor numbering,
        // the monster-or-ally boundary, and which of the declared pieces are still on the floor.
        services.TryAddSingleton<IDungeonRoomRuleService, DungeonRoomRuleService>();
        services.TryAddSingleton<IIllusionTowerRuleService, IllusionTowerRuleService>();
        services.TryAddSingleton<IIllusionTowerRewardRuleService, IllusionTowerRewardRuleService>();
        services.TryAddSingleton<IIllusionTowerRewardDomainService, IllusionTowerRewardDomainService>();
        services.TryAddSingleton<IIllusionTowerDomainService, IllusionTowerDomainService>();
        services.TryAddSingleton<IObjectDropRuleService, ObjectDropRuleService>();
        services.TryAddSingleton<IInventorySlotMappingRuleService, InventorySlotMappingRuleService>();
        services.TryAddSingleton<IBusinessInventorySlotDomainService, BusinessInventorySlotDomainService>();
        services.TryAddSingleton<ICharacterBusinessInventoryDomainService, CharacterBusinessInventoryDomainService>();
        services.TryAddSingleton<IEquipmentDomainService, EquipmentDomainService>();
        services.TryAddSingleton<ICreatureRuleService, CreatureRuleService>();
        services.TryAddSingleton<ICreatureDomainService, CreatureDomainService>();
        services.TryAddSingleton<ICreatureNamingDomainService, CreatureNamingDomainService>();
        services.TryAddSingleton<CreatureRuntimeDomainService>();
        services.TryAddSingleton<ICreatureRuntimeDomainService>(provider => provider.GetRequiredService<CreatureRuntimeDomainService>());
        services.TryAddSingleton<ICreatureInstanceDomainService>(provider => provider.GetRequiredService<CreatureRuntimeDomainService>());
        services.TryAddSingleton<LegacyWornItemNormalizer>();
        services.TryAddSingleton<IInventorySlotProjectionDomainService, InventorySlotProjectionDomainService>();
        services.TryAddSingleton<IItemSlotRuleService, ItemSlotRuleService>();
        services.TryAddSingleton<IInventoryExpansionRuleService, InventoryExpansionRuleService>();
        services.TryAddSingleton<IInventoryExpansionDomainService, InventoryExpansionDomainService>();
        services.TryAddSingleton<IInventoryCapacityRuleService, InventoryCapacityRuleService>();
        services.TryAddSingleton<IInventoryCapacityDomainService, InventoryCapacityDomainService>();
        services.TryAddSingleton<ICargoItemRuleService, CargoItemRuleService>();
        services.TryAddSingleton<ICargoTransferDomainService, CargoTransferDomainService>();
        // Where an item belongs, which slot it is worn in, and whether it is equipment at all --
        // the one place the archive's item definitions are read for those three answers.
        services.TryAddSingleton<IItemPlacementRuleService, ItemPlacementRuleService>();
        // Where an arriving item goes, and which day a refill belongs to.
        services.TryAddSingleton<IItemArrivalRuleService, ItemArrivalRuleService>();
        services.TryAddSingleton<IItemLifecycleDomainService, ItemLifecycleDomainService>();
        services.TryAddSingleton<IDailyRefillRuleService, DailyRefillRuleService>();

        // The rules a character's entry turns on, kept out of whatever reads or writes the rows
        // they apply to, so each can be checked without a database in reach.
        services.TryAddSingleton<AmbientStoreAccess>();
        services.TryAddSingleton<IAccountMaterialRuleService, AccountMaterialRuleService>();
        services.TryAddSingleton<IItemGrantDomainService, ItemGrantDomainService>();
        // The store is looked up rather than demanded, because a host may be assembled without
        // persistence at all and this seam is built eagerly by the cleanup service. Asking for it
        // outright would make such a host fail to start; asking for it optionally lets it start
        // and fail loudly only if it ever actually tries to save a position.
        services.TryAddSingleton<ISelectedSessionPositionPersistence>(
            provider => new SelectedSessionPositionPersistence(
                provider.GetService<ICharacterDomainStore>(),
                provider.GetRequiredService<IDomainTransactionAccessor>()));
        // What a job starts with and what a creation request may ask for; and what entry
        // reconciles a character against on the way in.
        services.TryAddSingleton<ICharacterCreationRuleService, CharacterCreationRuleService>();
        services.TryAddSingleton<
            ICharacterEntryPreparationRuleService, CharacterEntryPreparationRuleService>();
        services.TryAddSingleton<IGoldLimitRuleService, GoldLimitRuleService>();
        services.TryAddSingleton<ICarriedGoldRuleService, CarriedGoldRuleService>();
        services.TryAddSingleton<IPremiumServiceDomainService, PremiumServiceDomainService>();
        services.TryAddSingleton<IPremiumServiceUsageDomainService, PremiumServiceUsageDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Configuration.IDailyResetRuleService, DailyResetRuleService>();
        services.TryAddSingleton<IPremiumServiceRuleService, PremiumServiceRuleService>();
        services.TryAddSingleton<IQuestHelperPuppetRuleService, QuestHelperPuppetRuleService>();
        services.TryAddSingleton<IQuestHelperPuppetDomainService, QuestHelperPuppetDomainService>();
        services.TryAddSingleton<IExpertSkillContractRuleService, ExpertSkillContractRuleService>();
        services.TryAddSingleton<IDungeonEffectRuleService, DungeonEffectRuleService>();
        services.TryAddSingleton<IWeaknessRuleService>(_ => new WeaknessRuleService(configuration));
        services.TryAddSingleton<IWeaknessDomainService>(provider => new WeaknessDomainService(
            () => provider.GetRequiredService<ICharacterDomainService>(),
            provider.GetRequiredService<IWeaknessRuleService>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<WeaknessDomainService>>(),
            provider.GetRequiredService<Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>()));
        services.TryAddSingleton<IDungeonRunDomainService, DungeonRunDomainService>();
        services.TryAddSingleton<IDungeonRevivalDomainService, DungeonRevivalDomainService>();

        // The compensation seam and its first-phase implementation, which never compensates. It is
        // registered rather than left out so that everything upstream is written against the real
        // shape from the first day.
        // What an occasion drops: the one reader of the three drop tables and the only caller of
        // DropRules. The compensation seam below stays outside it, because what a character is
        // owed is state that crosses runs.
        services.TryAddSingleton<IDropRuleService, DropRuleService>();
        services.TryAddSingleton<IIndependentDropRuleService>(provider => new IndependentDropRuleService(
            provider.GetRequiredService<IIndependentDropTableSource>(),
            () => provider.GetRequiredService<ICharacterDomainService>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<IndependentDropRuleService>>(),
            provider.GetRequiredService<IndependentDropOptions>()));
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
        services.TryAddSingleton<DungeonVisitDomainService>();
        services.TryAddSingleton<IDungeonVisitDomainService, TransactionalDungeonVisitDomainService>();
        services.TryAddSingleton<IDungeonDifficultyRuleService, DungeonDifficultyRuleService>();
        services.TryAddSingleton<ICharacterDungeonDifficultyDomainService, CharacterDungeonDifficultyDomainService>();
        // Runtime kill interpretation reads only the published Cell snapshot.
        services.TryAddSingleton<IMonsterKillDomainService, MonsterKillDomainService>();
        // Recomputing a session's offer list after a level or a quest moved what it is made of.
        services.TryAddSingleton<
            IQuestOfferRefreshDomainService, QuestOfferRefreshDomainService>();
        // Whether a character may stand where they say, and what a tutorial step declares.
        services.TryAddSingleton<ITownMovementDomainService, TownMovementDomainService>();
        services.TryAddSingleton<ITutorialRewardDomainService, TutorialRewardDomainService>();
        services.TryAddSingleton<ITutorialCompletionDomainService, TutorialCompletionDomainService>();
        // The seams the command handlers reach the rules through, so that none of them holds a
        // rule service of its own.
        services.TryAddSingleton<IGroundPickupDomainService, GroundPickupDomainService>();
        services.TryAddSingleton<IEquipmentWearRuleService, EquipmentWearRuleService>();
        services.TryAddSingleton<IEquipmentWearDomainService, EquipmentWearDomainService>();
        services.TryAddSingleton<IItemMovementDomainService, ItemMovementDomainService>();
        services.TryAddSingleton<IInventorySortDomainService, InventorySortDomainService>();
        services.TryAddSingleton<INpcShopRuleService, NpcShopRuleService>();
        services.TryAddSingleton<INpcShopDomainService, NpcShopDomainService>();
        services.TryAddSingleton<ICeraShopRuleService, CeraShopRuleService>();
        services.TryAddSingleton<ICeraShopPurchaseRestrictionDomainService, CeraShopPurchaseRestrictionDomainService>();
        services.TryAddSingleton<ICeraShopDomainService, CeraShopDomainService>();
        services.TryAddSingleton<IRecipeCraftRuleService, RecipeCraftRuleService>();
        services.TryAddSingleton<IRecipeCraftDomainService, RecipeCraftDomainService>();
        services.TryAddSingleton<IPackageOpeningRuleService, PackageOpeningRuleService>();
        services.TryAddSingleton<IOpeningInventoryDomainService, OpeningInventoryDomainService>();
        services.TryAddSingleton<IOpeningEffectPlanRuleService, OpeningEffectPlanRuleService>();
        services.TryAddSingleton<ISpecialConsumableEffectPlanRuleService, SpecialConsumableEffectPlanRuleService>();
        services.TryAddSingleton<IConsumableStateDomainService, ConsumableStateDomainService>();
        services.TryAddSingleton<IMagicBoxOpeningDomainService, MagicBoxOpeningDomainService>();
        services.TryAddSingleton<ILotteryRandomSource, LotteryRandomSource>();
        services.TryAddSingleton<ILotteryResultSelector, WeightedLotteryResultSelector>();
        services.TryAddSingleton<IVendingMachineDomainService, VendingMachineDomainService>();
        services.TryAddSingleton<ILotteryOpeningDomainService, LotteryOpeningDomainService>();
        services.TryAddSingleton<IPackageOpeningDomainService, PackageOpeningDomainService>();
        services.TryAddSingleton<IAvatarPackageOpeningDomainService, AvatarPackageOpeningDomainService>();
        services.TryAddSingleton<ITeleportConsumableDomainService, TeleportConsumableDomainService>();
        services.TryAddSingleton<ITeleportConsumableRuleService, TeleportConsumableRuleService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, TownPositionEffectHandler>());
        services.TryAddSingleton<IItemSaleDomainService, ItemSaleDomainService>();
        services.TryAddSingleton<IItemSaleRuleService, ItemSaleRuleService>();
        services.TryAddSingleton<IEquipmentRepairRuleService, EquipmentRepairRuleService>();
        services.TryAddSingleton<IEquipmentEnchantRuleService, EquipmentEnchantRuleService>();
        services.TryAddSingleton<IEquipmentUpgradeRuleService, EquipmentUpgradeRuleService>();
        services.TryAddSingleton<IEquipmentReinforcementFailureRuleService, EquipmentReinforcementFailureRuleService>();
        services.TryAddSingleton<IEquipmentRepairDomainService, EquipmentRepairDomainService>();
        services.TryAddSingleton<IEquipmentReinforcementRollSource, EquipmentReinforcementRollSource>();
        services.TryAddSingleton<IEquipmentReinforcementDomainService, EquipmentReinforcementDomainService>();
        services.TryAddSingleton<IEquipmentEnchantDomainService, EquipmentEnchantDomainService>();
        services.TryAddSingleton<IEquipmentMagicSealSeedSource, EquipmentMagicSealSeedSource>();
        services.TryAddSingleton<IEquipmentMagicSealRuleService, EquipmentMagicSealRuleService>();
        services.TryAddSingleton<IEquipmentMagicSealDomainService, EquipmentMagicSealDomainService>();
        services.TryAddSingleton<IEquipmentOutworldSeedSource, EquipmentOutworldSeedSource>();
        services.TryAddSingleton<IEquipmentOutworldDomainService, EquipmentOutworldDomainService>();
        services.TryAddSingleton<IEquipmentForgingRollSource, EquipmentForgingRollSource>();
        services.TryAddSingleton<IEquipmentForgingDomainService, EquipmentForgingDomainService>();
        services.TryAddSingleton<MailItemTransfers>();
        services.TryAddSingleton<IMailItemRuleService, MailItemRuleService>();
        services.TryAddSingleton<IMailboxQueryDomainService, MailboxQueryDomainService>();
        services.TryAddSingleton<IMailboxDomainService, MailboxDomainService>();
        services.TryAddSingleton<IMailboxAssetDomainService, MailboxAssetDomainService>();
        services.TryAddSingleton<IMailboxSendDomainService, MailboxSendDomainService>();
        services.TryAddSingleton<IEquipmentDisjointRuleService, EquipmentDisjointRuleService>();
        services.TryAddSingleton<IEquipmentDisjointDomainService, EquipmentDisjointDomainService>();
        services.TryAddSingleton<IEquipmentDurabilityDomainService, EquipmentDurabilityDomainService>();
        services.TryAddSingleton<IItemDiscardDomainService, ItemDiscardDomainService>();
        services.TryAddSingleton<ICrystalContractDomainService, Dfo.Server.Application.Accounts.CrystalContractDomainService>();
        services.TryAddSingleton<IMaterialConsumptionDomainService, MaterialConsumptionDomainService>();
        services.TryAddSingleton<IMaterialConsumptionRuleService, MaterialConsumptionRuleService>();
        services.TryAddSingleton<IItemDiscardRuleService, ItemDiscardRuleService>();
        services.TryAddSingleton<IQuestDefinitionDomainService, QuestDefinitionDomainService>();
        services.TryAddSingleton<IQuestClearCouponRuleService, QuestClearCouponRuleService>();
        services.TryAddSingleton<IQuestClearCouponDomainService, QuestClearCouponDomainService>();
        services.TryAddSingleton<IFatiguePotionDomainService, FatiguePotionDomainService>();
        services.TryAddSingleton<IItemUsageDomainService, ItemUsageDomainService>();
        services.TryAddSingleton<
            IExperienceValuationDomainService, ExperienceValuationDomainService>();
        services.TryAddSingleton<IExperienceCalculationDomainService, ExperienceCalculationDomainService>();
        services.TryAddSingleton<
            IChannelDirectoryDomainService, ChannelDirectoryDomainService>();
        services.TryAddSingleton<MonsterDropDomainService>();
        services.TryAddSingleton<IMonsterDropDomainService>(services =>
            services.GetRequiredService<MonsterDropDomainService>());
        services.TryAddSingleton<IDungeonCellFactory, DungeonCellFactory>();
        services.TryAddSingleton<IQuestEnemyDropDomainService, QuestEnemyDropDomainService>();
        services.TryAddSingleton<IQuestEnemyDropRuleService, QuestEnemyDropRuleService>();
        // What a character has left to spend, asked by identifier and worked out on the spot. The
        // arithmetic belongs to the shared skill rules; the state service supplies current facts.
        services.TryAddSingleton<ICharacterSkillStateDomainService, CharacterSkillStateDomainService>();
        services.TryAddSingleton<ICharacterSkillResetRuleService, CharacterSkillResetRuleService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Notices.INoticeStateDomainService, Dfo.Server.Application.Notices.NoticeStateDomainService>();
        services.TryAddSingleton<ICharacterOptionsDomainService, CharacterOptionsDomainService>();
        services.TryAddSingleton<ICharacterSkillLearningDomainService, CharacterSkillLearningDomainService>();
        services.TryAddSingleton<ICharacterStateProjectionDomainService, CharacterStateProjectionDomainService>();
        services.TryAddSingleton<
            ICharacterSkillPointDomainService, CharacterSkillPointDomainService>();
        // Where a character being played is standing, asked by identifier rather than by session.
        services.TryAddSingleton<
            ICharacterPositionDomainService, CharacterPositionDomainService>();
        // A character's keys and options, asked by identifier. The entry load is the only other
        // reader of those two tables and it is left alone: it builds its frames from rows it has
        // already read, and this reads the same rows for anyone who is not entry.
        services.TryAddSingleton<
            ICharacterSettingsDomainService, CharacterSettingsDomainService>();
        // Configured allowance plus persistent, business-day usage accounting.
        services.TryAddSingleton<
            ICharacterFatigueDomainService, CharacterFatigueDomainService>();
        services.TryAddSingleton(
            provider => new CharacterDomainService(
                () => provider.GetRequiredService<ICharacterRepository>(),
                provider.GetRequiredService<ICharacterCreationRuleService>(),
                characterId => new PlayingCharacter(
                    characterId,
                    provider.GetRequiredService<IItemUseJudgementService>(),
                    provider.GetRequiredService<ICharacterItemCooldownDomainService>(),
                    provider.GetRequiredService<ITownAreaDomainService>(),
                    provider.GetRequiredService<ICharacterCarriedGoldTable>(),
                    provider.GetRequiredService<ICarriedGoldRuleService>(),
                    provider.GetRequiredService<ICharacterTable>(),
                    provider.GetRequiredService<ICharacterSkillPointDomainService>(),
                    provider.GetRequiredService<ICharacterStatRuleService>(),
                    provider.GetRequiredService<ICharacterSkillStateDomainService>(),
                    provider.GetRequiredService<IDungeonRunDomainService>(),
                    () => provider.GetRequiredService<IIllusionTowerDomainService>())));
        services.TryAddSingleton<ICharacterDomainService>(
            provider => provider.GetRequiredService<CharacterDomainService>());
        services.TryAddSingleton<ISelectCharacterPreparationDomainService, SelectCharacterPreparationService>();
        services.TryAddSingleton<ICharacterInventoryEntryDomainService, CharacterInventoryEntryDomainService>();
        services.TryAddSingleton<IAccountInventoryEntryDomainService, AccountInventoryEntryDomainService>();
        services.TryAddSingleton<IEquippedCreatureQuery, EquippedCreatureQuery>();
        // Fourteen inventory tables, and the sequencing that makes them one inventory.
        services.TryAddSingleton<InventoryEntryAssembly>();
        services.TryAddSingleton<ICharacterInventoryEntryRepository>(
            provider => provider.GetRequiredService<InventoryEntryAssembly>());
        services.TryAddSingleton<IAccountInventoryEntryRepository>(
            provider => provider.GetRequiredService<InventoryEntryAssembly>());
        services.TryAddSingleton<IEntryInventoryMutationRepository, DailyRefillService>();
        // Thirty-three tables: the order they are read in, and everything that makes them one
        // character.
        services.TryAddSingleton<ICharacterEntryStateRepository, CharacterEntryAssembly>();
        services.TryAddSingleton<CharacterContainerAddition>();
        services.TryAddSingleton<ICharacterContainerDomainService, CharacterContainerDomainService>();
        services.TryAddSingleton<Dfo.Server.Application.World.IllusionTower.TowerInventoryDomainService>();
        services.TryAddSingleton<ICharacterQuestLogDomainService, CharacterQuestLogDomainService>();
        services.TryAddSingleton<IQuestTriggerDomainService, QuestTriggerDomainService>();
        services.TryAddSingleton<IQuestProgressDomainService, QuestProgressDomainService>();
        services.TryAddSingleton<
            ICharacterDarkKnightComboDomainService, CharacterDarkKnightComboDomainService>();
        services.TryAddSingleton<ICharacterItemCooldownDomainService, CharacterItemCooldownDomainService>();
        services.TryAddSingleton<IItemUseJudgementService, ItemUseJudgementService>();
        // What entry has to grant before a character is published. One reader for the daily
        // refill declarations.
        services.TryAddSingleton<IInventoryLifecycleRuleService, InventoryLifecycleRuleService>();
        services.TryAddSingleton<IItemUseEffectDomainService, ItemUseEffectDomainService>();
        services.TryAddSingleton<IItemUseEffectRuleService, ItemUseEffectRuleService>();
        services.TryAddSingleton<IApplyEffectsDomainService, ApplyEffectsDomainService>();
        services.TryAddSingleton<IInventoryEffectPreflightDomainService, InventoryEffectPreflightDomainService>();
        services.TryAddSingleton<IEquipmentEffectPlanRuleService, EquipmentEffectPlanRuleService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, GrantPreparedItemEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInventoryEffectPreviewHandler, GrantPreparedItemEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, GrantOpeningVirtualEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInventoryEffectPreviewHandler, GrantOpeningVirtualEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetOpeningPremiumEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInventoryEffectPreviewHandler, SetOpeningPremiumEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetSeriaLuckEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInventoryEffectPreviewHandler, SetSeriaLuckEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetDoublePotUsageEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInventoryEffectPreviewHandler, SetDoublePotUsageEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SpendGoldEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, GrantStackableEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, GrantBasicCharacterItemEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetQuestRewardGoldEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, CharacterExperienceEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetCharacterGrowTypeEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, MaterializeSkillBuildsEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetEquipmentUpgradeEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetEquipmentEnchantmentEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetEquipmentDimensionalEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetEquipmentMagicSealEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetEquipmentForgingEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, DestroyEquipmentEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, ConsumeFromContainerEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, StartItemCooldownEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, StartSpiritPotionEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, AddPetStomachEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetFatigueStateEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetPurchaseRestrictionEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetItemUsageStateEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetCarriedQuestEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, RemoveTrackedQuestEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetClearedQuestEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetLearnedSkillEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, RenamePetEffectHandler>());
        services.TryAddSingleton<ISelectCharacterEntryInventoryDomainService, SelectCharacterEntryInventoryDomainService>();
        services.TryAddSingleton<IExpiredCharacterItemCleanupDomainService, ExpiredCharacterItemCleanupDomainService>();
        services.TryAddSingleton<ICharacterProfileEntryProjection, CharacterProfileEntryProjection>();
        services.TryAddSingleton<ICharacterSkillQuestEntryProjection, CharacterSkillQuestEntryProjection>();
        services.TryAddSingleton<ICharacterProgressionEntryProjection, CharacterProgressionEntryProjection>();
        services.TryAddSingleton<ICharacterFeatureEntryProjection, CharacterFeatureEntryProjection>();
        services.TryAddSingleton<ICharacterEntryStateDomainService, CharacterEntryStateDomainService>();
        services.TryAddSingleton<ISelectCharacterQuestEntryDomainService, SelectCharacterQuestEntryService>();
        services.TryAddSingleton<ISelectCharacterPetEntryDomainService, SelectCharacterPetEntryService>();
        services.TryAddSingleton<IAccountSettingsEntryProjection, AccountSettingsEntryProjection>();
        services.TryAddSingleton<IAccountWalletEntitlementEntryProjection, AccountWalletEntitlementEntryProjection>();
        services.TryAddSingleton<IAccountProgressionEntryProjection, AccountProgressionEntryProjection>();
        services.TryAddSingleton<IAccountRelationCompatibilityEntryProjection, AccountRelationCompatibilityEntryProjection>();
        // Creating an account if its login name is new, and putting its eight tables together.
        // Both are decisions, so both are here.
        // The character side's decisions. Reading and writing stayed in persistence.
        services.TryAddSingleton<ICharacterRepository, CharacterRosterService>();
        services.TryAddSingleton<ICharacterPvpSkillProfileRepository, PvpSkillProfileService>();
        services.TryAddSingleton<IExpertSkillContractReconciliationRepository,
            ExpertSkillContractReconciliationService>();
        services.TryAddSingleton<IDungeonPersistentEffectRecoveryRepository,
            DungeonEffectRecoveryService>();
        services.TryAddSingleton<IAccountEntryStateRepository, AccountEntryAssembly>();
        services.TryAddSingleton<IAccountEntryStateDomainService, AccountEntryStateDomainService>();
        services.TryAddSingleton<IWorldContentEntryRuleService, WorldContentEntryRuleService>();
        services.TryAddSingleton<IChannelEntryPolicy, ChannelEntryPolicy>();
        services.TryAddSingleton<IEntryPlacementRuleService, EntryPlacementRuleService>();
        services.TryAddSingleton<SelectedSessionDomainService>();
        services.TryAddSingleton<ISelectedSessionDomainService>(sp => sp.GetRequiredService<SelectedSessionDomainService>());
        services.TryAddSingleton<ISelectedSessionSnapshotQuery>(sp => sp.GetRequiredService<SelectedSessionDomainService>());
        services.TryAddSingleton<ISelectedSessionRuntimeDirectory, SelectedSessionRuntimeDirectory>();
        services.TryAddSingleton<ICharacterVitalRuntimeDomainService, CharacterVitalRuntimeDomainService>();
        services.TryAddSingleton<ISelectedCharacterCurrentViewDomainService, SelectedCharacterCurrentViewDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Effects.IReplayStateDomainService, Dfo.Server.Application.Effects.ReplayStateDomainService>();
        services.AddCommandHandler<Dfo.Server.Application.Effects.ReadReplayStateCommand,
            Dfo.Server.Abstractions.Effects.ReplayStateSnapshot, Dfo.Server.Application.Effects.ReadReplayStateCommandHandler>(CommandExecutionKind.Transactional);
        services.TryAddSingleton<
            IOtherCharacterDetailQueryDomainService,
            OtherCharacterDetailQueryDomainService>();
        // Network-only test hosts deliberately assemble Application without persistence. Party
        // handlers are discovered eagerly at listener startup, while inventory is only needed if
        // a real invite is requested, so defer that dependency to the command invocation.
        services.TryAddSingleton<IPartyMemberStatusDomainService>(provider =>
            new PartyMemberStatusDomainService(
                () => provider.GetRequiredService<ICharacterInventoryEntryDomainService>(),
                provider.GetRequiredService<ICharacterFatigueDomainService>(),
                provider.GetRequiredService<ICharacterVitalRuntimeDomainService>()));
        services.TryAddSingleton<IPartyModeRuleService, PartyModeRuleService>();
        services.TryAddSingleton<IPartyMemberSessionEndpointSource,
            PartyMemberSessionEndpointSource>();
        services.TryAddSingleton<PartyService>();
        services.TryAddSingleton<IPartyService>(provider =>
            provider.GetRequiredService<PartyService>());
        services.TryAddSingleton<IPartyDirectoryPresenceSink>(provider =>
            provider.GetRequiredService<PartyService>());
        services.TryAddSingleton<ITownAreaDomainService, TownAreaDomainService>();
        services.TryAddSingleton<IExpertStoreDomainService, ExpertStoreDomainService>();
        services.TryAddSingleton<IPvpLobbyDomainService, PvpLobbyDomainService>();
        services.TryAddSingleton<ISessionAdjacentEntryDomainService, SessionAdjacentEntryDomainService>();
        services.TryAddSingleton<ISelectedSessionCleanupDomainService, SelectedSessionCleanupService>();
        services.TryAddSingleton<ISelectCharacterSessionEntryDomainService, SelectCharacterSessionEntryService>();
        services.AddCommandDispatching();
        services.TryAddSingleton<IAuthenticatedGamePresenceQuery>(provider => provider.GetRequiredService<OnlineUserDomainService>());
        services.TryAddSingleton<Dfo.Server.Application.Administration.GmPlayerRuntimeQuery>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmPlayerDirectoryDomainService, Dfo.Server.Application.Administration.GmPlayerDirectoryDomainService>();
        services.AddCommandHandler<Dfo.Server.Application.Administration.QueryGmAccountsCommand, Dfo.Server.Data.Administration.GmPage<Dfo.Server.Data.Administration.GmAccountView>, Dfo.Server.Application.Administration.QueryGmAccountsCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Application.Administration.QueryGmCharactersCommand, Dfo.Server.Data.Administration.GmPage<Dfo.Server.Data.Administration.GmCharacterView>, Dfo.Server.Application.Administration.QueryGmCharactersCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Application.Administration.FindGmAccountCommand, Dfo.Server.Data.Administration.GmAccountLookup, Dfo.Server.Application.Administration.FindGmAccountCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Application.Administration.FindGmCharacterCommand, Dfo.Server.Data.Administration.GmCharacterLookup, Dfo.Server.Application.Administration.FindGmCharacterCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Application.Administration.QueryGmOverviewCommand, Dfo.Server.Data.Administration.GmOverview, Dfo.Server.Application.Administration.QueryGmOverviewCommandHandler>(CommandExecutionKind.Transactional);
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmAccessDomainService, Dfo.Server.Application.Administration.GmAccessDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmAuditDomainService, Dfo.Server.Application.Administration.GmAuditDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmCeraRuleService, Dfo.Server.Application.Administration.GmCeraRuleService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IAccountWalletDomainService, Dfo.Server.Application.Administration.AccountWalletDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmCeraReminderDomainService, Dfo.Server.Application.Administration.GmCeraReminderDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmCeraDomainService, Dfo.Server.Application.Administration.GmCeraDomainService>();
        services.AddCommandHandler<Dfo.Server.Application.Administration.QueryGmCeraCommand, Dfo.Server.Data.Administration.GmCeraQueryResult, Dfo.Server.Application.Administration.QueryGmCeraCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Application.Administration.GrantGmCeraCommand, Dfo.Server.Data.Administration.GmCeraGrantResult, Dfo.Server.Application.Administration.GrantGmCeraCommandHandler>(CommandExecutionKind.Transactional, accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<Dfo.Server.Application.Administration.RecordGmCeraFailureCommand, bool, Dfo.Server.Application.Administration.RecordGmCeraFailureCommandHandler>(CommandExecutionKind.Transactional);
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmMailRuleService, Dfo.Server.Application.Administration.GmMailRuleService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmMailEquipmentRuleService, Dfo.Server.Application.Administration.GmMailEquipmentRuleService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmOfficialMailDomainService, Dfo.Server.Application.Administration.GmOfficialMailDomainService>();
        services.AddCommandHandler<Dfo.Server.Application.Administration.QueryGmMailCommand, Dfo.Server.Data.Administration.GmMailQueryResult, Dfo.Server.Application.Administration.QueryGmMailCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Application.Administration.EditGmMailCommand, Dfo.Server.Data.Administration.GmMailDocument, Dfo.Server.Application.Administration.EditGmMailCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Application.Administration.SendGmMailCommand, Dfo.Server.Data.Administration.GmMailSendResult, Dfo.Server.Application.Administration.SendGmMailCommandHandler>(CommandExecutionKind.Transactional, accountAccessMode: UserAccessMode.Exclusive);
        services.AddCommandHandler<Dfo.Server.Application.Administration.RecordGmMailFailureCommand, bool, Dfo.Server.Application.Administration.RecordGmMailFailureCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Application.Administration.BootstrapGmAdministratorCommand, bool, Dfo.Server.Application.Administration.BootstrapGmAdministratorCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Application.Administration.LoginGmAdministratorCommand, Dfo.Server.Data.Administration.GmLoginResult, Dfo.Server.Application.Administration.LoginGmAdministratorCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Application.Administration.LogoutGmAdministratorCommand, bool, Dfo.Server.Application.Administration.LogoutGmAdministratorCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<Dfo.Server.Application.Administration.QueryGmAuditCommand, Dfo.Server.Data.Administration.GmAuditPage, Dfo.Server.Application.Administration.QueryGmAuditCommandHandler>(CommandExecutionKind.Transactional);
        services.TryAddSingleton<IPasswordCredentialRuleService, PasswordCredentialService>();
        services.TryAddSingleton(AuthenticationRuleService.From(configuration));
        services.TryAddSingleton(LoginSessionOptions.From(configuration));
        services.TryAddSingleton<LoginSessionRegistry>();
        services.TryAddSingleton<ILoginSessionDomainService>(provider => provider.GetRequiredService<LoginSessionRegistry>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INetworkConnectionObserver, LoginSessionConnectionObserver>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, LoginSessionMaintenanceService>());
        services.TryAddSingleton<IAuthenticationDomainService, AuthenticationDomainService>();
        services.TryAddSingleton<IGameLoginAuthenticationDomainService, GameLoginAuthenticationDomainService>();
        services.AddCommandHandler<RegisterAccountCommand, RegistrationResult, RegisterAccountCommandHandler>(CommandExecutionKind.Transactional);
        services.AddCommandHandler<AuthenticateAndIssueTicketCommand, AuthenticationResult, AuthenticateAndIssueTicketCommandHandler>(CommandExecutionKind.Transactional);
        services.AddNetworkMessageDispatching();
        services.AddNotificationDispatching();
    }
}
