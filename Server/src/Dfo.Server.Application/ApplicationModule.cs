using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Channel;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
                provider.GetRequiredService<IInitialSkillDefinitionSource>()));
        services.TryAddSingleton<ICharacterDomainService>(
            provider => provider.GetRequiredService<CharacterDomainService>());
        services.TryAddSingleton<ISelectCharacterPreparationService, SelectCharacterPreparationService>();
        services.TryAddSingleton<IEquipmentEligibilityCharacterReader>(
            provider => provider.GetRequiredService<CharacterDomainService>());
        services.TryAddSingleton<ICharacterInventoryEntryDomainService, CharacterInventoryEntryDomainService>();
        services.TryAddSingleton<IAccountInventoryEntryDomainService, AccountInventoryEntryDomainService>();
        services.TryAddSingleton<IEquippedCreatureQuery, EquippedCreatureQuery>();
        services.TryAddSingleton<IEntryInventoryLeaseRegistry, EntryInventoryLeaseRegistry>();
        services.TryAddSingleton<ICharacterContainerDomainService, CharacterContainerDomainService>();
        services.TryAddSingleton<IItemContainerFactory, ItemContainerFactory>();
        services.TryAddSingleton<ICharacterItemCooldownDomainService, CharacterItemCooldownDomainService>();
        services.TryAddSingleton<IItemUsabilityDomainService, ItemUsabilityDomainService>();
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
