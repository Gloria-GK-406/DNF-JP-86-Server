using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
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
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.World;
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
        services.TryAddSingleton(
            provider => new UserService(
                () => provider.GetRequiredService<IAccountRepository>()));
        services.TryAddSingleton<IAccountService>(
            provider => provider.GetRequiredService<UserService>());
        services.TryAddSingleton<IOnlineUserService>(
            provider => provider.GetRequiredService<UserService>());
        services.TryAddSingleton<IOnlineUserQueryService>(
            provider => provider.GetRequiredService<UserService>());
        services.TryAddSingleton<IActiveCharacterService>(
            provider => provider.GetRequiredService<UserService>());
        services.TryAddSingleton(
            provider => new CharacterService(
                () => provider.GetRequiredService<ICharacterRepository>()));
        services.TryAddSingleton<ICharacterService>(
            provider => provider.GetRequiredService<CharacterService>());
        services.TryAddSingleton<ISelectCharacterPreparationService, SelectCharacterPreparationService>();
        services.TryAddSingleton<IEquipmentEligibilityCharacterReader>(
            provider => provider.GetRequiredService<CharacterService>());
        services.TryAddSingleton<ICharacterInventoryEntryService, CharacterInventoryEntryService>();
        services.TryAddSingleton<IAccountInventoryEntryService, AccountInventoryEntryService>();
        services.TryAddSingleton<IEquippedCreatureQuery, EquippedCreatureQuery>();
        services.TryAddSingleton<IEntryInventoryLeaseRegistry, EntryInventoryLeaseRegistry>();
        services.TryAddSingleton<ISelectCharacterEntryInventoryService, SelectCharacterEntryInventoryService>();
        services.TryAddSingleton<ICharacterProfileEntryService, CharacterProfileEntryService>();
        services.TryAddSingleton<ICharacterSkillQuestEntryService, CharacterSkillQuestEntryService>();
        services.TryAddSingleton<ICharacterProgressionEntryService, CharacterProgressionEntryService>();
        services.TryAddSingleton<ICharacterFeatureEntryService, CharacterFeatureEntryService>();
        services.TryAddSingleton<ICharacterEntryStateService, CharacterEntryStateService>();
        services.TryAddSingleton<ISelectCharacterQuestEntryService, SelectCharacterQuestEntryService>();
        services.TryAddSingleton<ISelectCharacterPetEntryService, SelectCharacterPetEntryService>();
        services.TryAddSingleton<IAccountSettingsEntryService, AccountSettingsEntryService>();
        services.TryAddSingleton<IAccountWalletEntitlementEntryService, AccountWalletEntitlementEntryService>();
        services.TryAddSingleton<IAccountProgressionEntryService, AccountProgressionEntryService>();
        services.TryAddSingleton<IAccountRelationCompatibilityEntryService, AccountRelationCompatibilityEntryService>();
        services.TryAddSingleton<IAccountEntryStateService, AccountEntryStateService>();
        services.TryAddSingleton<IWorldContentEntryService, WorldContentEntryService>();
        services.TryAddSingleton<IChannelEntryPolicy, ChannelEntryPolicy>();
        services.TryAddSingleton<IEntryPlacementService, EntryPlacementService>();
        services.TryAddSingleton<ISessionPresenceRegistry, SessionPresenceRegistry>();
        services.TryAddSingleton<ISelectedSessionDirectory, SelectedSessionDirectory>();
        services.TryAddSingleton<ISelectedSessionRuntimeDirectory, SelectedSessionRuntimeDirectory>();
        services.TryAddSingleton<IExpertStoreRegistry, ExpertStoreRegistry>();
        services.TryAddSingleton<IPvpLobbyRegistry, PvpLobbyRegistry>();
        services.TryAddSingleton<IDungeonRejoinRegistry, DungeonRejoinRegistry>();
        services.TryAddSingleton<ISessionAdjacentEntryService, SessionAdjacentEntryService>();
        services.TryAddSingleton<ISelectedSessionCleanupService, SelectedSessionCleanupService>();
        services.TryAddSingleton<ISelectCharacterSessionEntryService, SelectCharacterSessionEntryService>();
        services.AddCommandDispatching();
        services.AddNetworkMessageDispatching();
    }
}
