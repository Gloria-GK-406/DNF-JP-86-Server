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

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);
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
        services.TryAddSingleton<IDungeonLayoutRuleService, DungeonLayoutRuleService>();
        services.TryAddSingleton<IItemSlotRuleService, ItemSlotRuleService>();
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
        services.TryAddSingleton<IGoldLimitRuleService, GoldLimitRuleService>();
        services.TryAddSingleton<IExpertSkillContractRuleService, ExpertSkillContractRuleService>();
        services.TryAddSingleton<IDungeonEffectRuleService, DungeonEffectRuleService>();
        services.TryAddSingleton<IDungeonRunDomainService, DungeonRunDomainService>();
        services.TryAddSingleton<IActiveCharacterAccessDomainService>(
            provider => new ActiveCharacterAccessDomainService(
                provider.GetRequiredService<OnlineStateStore>(),
                provider.GetRequiredService<UserExecutionGate<CharacterId>>(),
                TimeSpan.FromSeconds(10)));
        services.TryAddSingleton(
            provider => new CharacterDomainService(
                () => provider.GetRequiredService<ICharacterRepository>(),
                provider.GetRequiredService<ICharacterStatDefinitionSource>(),
                provider.GetRequiredService<IInitialSkillDefinitionSource>(),
                provider.GetRequiredService<IInitialEquipmentDefinitionSource>(),
                characterId => new PlayingCharacter(
                    characterId,
                    provider.GetRequiredService<IItemUseRuleDefinitionSource>(),
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
