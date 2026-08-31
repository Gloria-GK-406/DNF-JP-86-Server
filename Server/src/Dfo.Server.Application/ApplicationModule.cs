using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Channel;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
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

        // What a premium term adds to an award. Empty unless an operator configures it: the
        // archive declares no rate for any of its fifty-two premium types, so any number here
        // would be invented, and an invented number in this subject is indistinguishable from a
        // measured one.
        // What a kill is worth before anything a character carries: the curve, the divisor, the
        // rank rates and the level-gap bands. Only what the archive does not declare -- and unlike
        // the two policies below, a malformed value here stops the server rather than defaulting,
        // because these have a right answer and a wrong one is invisible in play.
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
        services.TryAddSingleton<IExperienceBonusService, ExperienceBonusService>();
        services.TryAddSingleton<ICharacterExperienceGrant, CharacterExperienceGrant>();
        // One store, three services. The store keeps the collections whose invariant spans them
        // behind a single lock; the services are the three responsibilities read off it.
        services.TryAddSingleton<OnlineStateStore>();
        services.TryAddSingleton<IAccountDomainService>(
            provider => new AccountDomainService(
                () => provider.GetRequiredService<IAccountRepository>(),
                provider.GetRequiredService<OnlineStateStore>()));
        services.TryAddSingleton(
            provider => new OnlineUserDomainService(
                provider.GetRequiredService<OnlineStateStore>()));
        services.TryAddSingleton<IOnlineUserDomainService>(
            provider => provider.GetRequiredService<OnlineUserDomainService>());
        services.TryAddSingleton<IOnlineUserQueryDomainService>(
            provider => provider.GetRequiredService<OnlineUserDomainService>());
        services.TryAddSingleton<IActiveCharacterDomainService>(
            provider => new ActiveCharacterDomainService(
                provider.GetRequiredService<OnlineStateStore>()));
        // The gate is keyed on the character rather than the account because the operations
        // that need it span accounts: a party pays an entry cost together.
        services.TryAddSingleton<UserExecutionGate<CharacterId>>();
        // What the archive declares about quests, and the shared parameters a reward is worked
        // out from. One reader for both, because no quest outcome is decided without both.
        services.TryAddSingleton<IQuestContentRuleService, QuestContentRuleService>();
        services.TryAddSingleton<IDungeonLayoutRuleService, DungeonLayoutRuleService>();
        // What the server declares about its channels, and what a tutorial step hands over.
        services.TryAddSingleton<IChannelContentRuleService, ChannelContentRuleService>();
        services.TryAddSingleton<ITutorialRewardRuleService, TutorialRewardRuleService>();
        // What a room declares, and what a run standing in it makes of that: the actor numbering,
        // the monster-or-ally boundary, and which of the declared pieces are still on the floor.
        services.TryAddSingleton<IDungeonRoomRuleService, DungeonRoomRuleService>();
        services.TryAddSingleton<IItemSlotRuleService, ItemSlotRuleService>();
        // Where an item belongs, which slot it is worn in, and whether it is equipment at all --
        // the one place the archive's item definitions are read for those three answers.
        services.TryAddSingleton<IItemPlacementRuleService, ItemPlacementRuleService>();
        // Where an arriving item goes, and which day a refill belongs to. Both were decisions the
        // database used to make about itself.
        services.TryAddSingleton<IItemArrivalRuleService, ItemArrivalRuleService>();
        services.TryAddSingleton<IDailyRefillRuleService, DailyRefillRuleService>();

        // The rules a character's entry turns on, kept out of whatever reads or writes the rows
        // they apply to, so each can be checked without a database in reach.
        services.TryAddSingleton<AmbientStoreAccess>();
        // The store is looked up rather than demanded, because a host may be assembled without
        // persistence at all and this seam is built eagerly by the cleanup service. Asking for it
        // outright would make such a host fail to start; asking for it optionally lets it start
        // and fail loudly only if it ever actually tries to save a position.
        services.TryAddSingleton<ISelectedSessionPositionPersistence>(
            provider => new SelectedSessionPositionPersistence(
                provider.GetService<ICharacterStore>(),
                provider.GetRequiredService<IDomainTransactionAccessor>()));
        // What a job starts with and what a creation request may ask for; and what entry
        // reconciles a character against on the way in.
        services.TryAddSingleton<ICharacterCreationRuleService, CharacterCreationRuleService>();
        services.TryAddSingleton<
            ICharacterEntryPreparationRuleService, CharacterEntryPreparationRuleService>();
        services.TryAddSingleton<IGoldLimitRuleService, GoldLimitRuleService>();
        services.TryAddSingleton<IExpertSkillContractRuleService, ExpertSkillContractRuleService>();
        services.TryAddSingleton<IDungeonEffectRuleService, DungeonEffectRuleService>();
        services.TryAddSingleton<IDungeonRunDomainService, DungeonRunDomainService>();

        // The compensation seam and its first-phase implementation, which never compensates. It is
        // registered rather than left out so that everything upstream is written against the real
        // shape from the first day.
        services.TryAddSingleton<IAccountMoneyService, AccountMoneyService>();
        // What an occasion drops: the one reader of the three drop tables and the only caller of
        // DropRules. The compensation seam below stays outside it, because what a character is
        // owed is state that crosses runs.
        services.TryAddSingleton<IDropRuleService, DropRuleService>();
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
        // Entering a dungeon and walking between its rooms: the use case two protocol handlers
        // used to assemble inline.
        services.TryAddSingleton<IDungeonSelectionRuleService, DungeonSelectionRuleService>();
        services.TryAddSingleton<IDungeonVisitDomainService, DungeonVisitDomainService>();
        // What a kill reported against a run means: which declared actor it names, and whether
        // the room is now empty.
        services.TryAddSingleton<IMonsterKillDomainService, MonsterKillDomainService>();
        // Recomputing a session's offer list after a level or a quest moved what it is made of.
        // It used to sit in the Protocol layer, where five handlers reached it directly.
        services.TryAddSingleton<
            IQuestOfferRefreshDomainService, QuestOfferRefreshDomainService>();
        // Whether a character may stand where they say, and what a tutorial step declares.
        services.TryAddSingleton<ITownMovementDomainService, TownMovementDomainService>();
        services.TryAddSingleton<ITutorialRewardDomainService, TutorialRewardDomainService>();
        // The seams the command handlers reach the rules through, so that none of them holds a
        // rule service of its own.
        services.TryAddSingleton<IGroundPickupDomainService, GroundPickupDomainService>();
        services.TryAddSingleton<IItemMovementDomainService, ItemMovementDomainService>();
        services.TryAddSingleton<IQuestDefinitionDomainService, QuestDefinitionDomainService>();
        services.TryAddSingleton<
            IExperienceValuationDomainService, ExperienceValuationDomainService>();
        services.TryAddSingleton<
            IChannelDirectoryDomainService, ChannelDirectoryDomainService>();
        services.TryAddSingleton<IMonsterDropService, MonsterDropService>();
        services.TryAddSingleton<IActiveCharacterAccessDomainService>(
            provider => new ActiveCharacterAccessDomainService(
                provider.GetRequiredService<OnlineStateStore>(),
                provider.GetRequiredService<UserExecutionGate<CharacterId>>(),
                TimeSpan.FromSeconds(10)));
        services.TryAddSingleton(
            provider => new CharacterDomainService(
                () => provider.GetRequiredService<ICharacterRepository>(),
                provider.GetRequiredService<ICharacterCreationRuleService>(),
                characterId => new PlayingCharacter(
                    characterId,
                    provider.GetRequiredService<IItemUseJudgementService>(),
                    provider.GetRequiredService<ICharacterItemCooldownDomainService>(),
                    provider.GetRequiredService<ISelectedSessionDirectory>(),
                    provider.GetRequiredService<ISelectedSessionRuntimeDirectory>(),
                    provider.GetRequiredService<ISessionPresenceRegistry>())));
        services.TryAddSingleton<ICharacterDomainService>(
            provider => provider.GetRequiredService<CharacterDomainService>());
        services.TryAddSingleton<ISelectCharacterPreparationService, SelectCharacterPreparationService>();
        services.TryAddSingleton<IEquipmentEligibilityCharacterReader>(
            provider => provider.GetRequiredService<CharacterDomainService>());
        services.TryAddSingleton<ICharacterInventoryEntryDomainService, CharacterInventoryEntryDomainService>();
        services.TryAddSingleton<IAccountInventoryEntryDomainService, AccountInventoryEntryDomainService>();
        services.TryAddSingleton<IEquippedCreatureQuery, EquippedCreatureQuery>();
        services.TryAddSingleton<IEntryInventoryLeaseRegistry, EntryInventoryLeaseRegistry>();
        // What used to be two SQL classes on the inventory side: fourteen tables between them,
        // and the sequencing that made those tables one inventory.
        services.TryAddSingleton<InventoryEntryAssembly>();
        services.TryAddSingleton<ICharacterInventoryEntryRepository>(
            provider => provider.GetRequiredService<InventoryEntryAssembly>());
        services.TryAddSingleton<IAccountInventoryEntryRepository>(
            provider => provider.GetRequiredService<InventoryEntryAssembly>());
        services.TryAddSingleton<IEntryInventoryLeasePersistence>(
            provider => provider.GetRequiredService<InventoryEntryAssembly>());
        services.TryAddSingleton<IEntryInventoryMutationRepository, DailyRefillService>();
        // What used to be one SQL class over thirty-three tables. The tables stayed; the order
        // they are read in and everything that made them one character came here.
        services.TryAddSingleton<ICharacterEntryStateRepository, CharacterEntryAssembly>();
        services.TryAddSingleton<ICharacterContainerDomainService, CharacterContainerDomainService>();
        services.TryAddSingleton<ICharacterQuestLogDomainService, CharacterQuestLogDomainService>();
        services.TryAddSingleton<ICharacterItemCooldownDomainService, CharacterItemCooldownDomainService>();
        services.TryAddSingleton<IItemUseJudgementService, ItemUseJudgementService>();
        // What entry has to grant before a character is published. One reader for the daily
        // refill declarations.
        services.TryAddSingleton<IInventoryLifecycleRuleService, InventoryLifecycleRuleService>();
        services.TryAddSingleton<IItemUseEffectDomainService, ItemUseEffectDomainService>();
        services.TryAddSingleton<ISelectCharacterEntryInventoryDomainService, SelectCharacterEntryInventoryDomainService>();
        services.TryAddSingleton<ICharacterProfileEntryProjection, CharacterProfileEntryProjection>();
        services.TryAddSingleton<ICharacterSkillQuestEntryProjection, CharacterSkillQuestEntryProjection>();
        services.TryAddSingleton<ICharacterProgressionEntryProjection, CharacterProgressionEntryProjection>();
        services.TryAddSingleton<ICharacterFeatureEntryProjection, CharacterFeatureEntryProjection>();
        services.TryAddSingleton<ICharacterEntryStateDomainService, CharacterEntryStateDomainService>();
        services.TryAddSingleton<ISelectCharacterQuestEntryService, SelectCharacterQuestEntryService>();
        services.TryAddSingleton<ISelectCharacterPetEntryService, SelectCharacterPetEntryService>();
        services.TryAddSingleton<IAccountSettingsEntryProjection, AccountSettingsEntryProjection>();
        services.TryAddSingleton<IAccountWalletEntitlementEntryProjection, AccountWalletEntitlementEntryProjection>();
        services.TryAddSingleton<IAccountProgressionEntryProjection, AccountProgressionEntryProjection>();
        services.TryAddSingleton<IAccountRelationCompatibilityEntryProjection, AccountRelationCompatibilityEntryProjection>();
        // What used to be two SQL classes: creating an account if its login name is new, and
        // putting an account's eight tables together. Both are decisions, so both are here.
        services.TryAddSingleton<IAccountRepository, AccountProvisioning>();
        // What used to be four SQL classes on the character side. Each of them read, decided, and
        // wrote; only the reading and writing stayed behind.
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
        services.TryAddSingleton<ISessionPresenceRegistry, SessionPresenceRegistry>();
        services.TryAddSingleton<ISelectedSessionDirectory, SelectedSessionDirectory>();
        services.TryAddSingleton<ISelectedSessionRuntimeDirectory, SelectedSessionRuntimeDirectory>();
        services.TryAddSingleton<IExpertStoreRegistry, ExpertStoreRegistry>();
        services.TryAddSingleton<IPvpLobbyRegistry, PvpLobbyRegistry>();
        services.TryAddSingleton<ISessionAdjacentEntryDomainService, SessionAdjacentEntryDomainService>();
        services.TryAddSingleton<ISelectedSessionCleanupService, SelectedSessionCleanupService>();
        services.TryAddSingleton<ISelectCharacterSessionEntryService, SelectCharacterSessionEntryService>();
        services.AddCommandDispatching();
        services.AddNetworkMessageDispatching();
    }
}
