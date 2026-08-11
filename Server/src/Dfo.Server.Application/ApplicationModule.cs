using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Channel;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Commands.Channel;
using Dfo.Server.Application.Commands.Game;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Game;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Networking;
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
        services.TryAddSingleton<IChannelEncryptionKeyProvider, ChannelEncryptionKeyProvider>();
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
        services.TryAddSingleton<IInventoryEntryService, InventoryEntryService>();
        services.TryAddSingleton<ICharacterProfileEntryService, CharacterProfileEntryService>();
        services.TryAddSingleton<ICharacterSkillQuestEntryService, CharacterSkillQuestEntryService>();
        services.TryAddSingleton<ICharacterProgressionEntryService, CharacterProgressionEntryService>();
        services.TryAddSingleton<ICharacterFeatureEntryService, CharacterFeatureEntryService>();
        services.TryAddSingleton<ICharacterEntryStateService, CharacterEntryStateService>();
        services.TryAddSingleton<IAccountSettingsEntryService, AccountSettingsEntryService>();
        services.TryAddSingleton<IAccountWalletEntitlementEntryService, AccountWalletEntitlementEntryService>();
        services.TryAddSingleton<IAccountProgressionEntryService, AccountProgressionEntryService>();
        services.TryAddSingleton<IAccountRelationCompatibilityEntryService, AccountRelationCompatibilityEntryService>();
        services.TryAddSingleton<IAccountEntryStateService, AccountEntryStateService>();
        services.TryAddSingleton<ICharacterEntryService, CharacterEntryService>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<INetworkConnectionObserver, OnlineUserConnectionObserver>());
        services.AddCommandDispatching();
        services.RegisterCommandHandler<EstablishChannelConnectionCommandHandler>(
            ApplicationCommandType.EstablishChannelConnection,
            CommandExecutionKind.Direct);
        services.RegisterCommandHandler<GetChannelScriptVersionCommandHandler>(
            ApplicationCommandType.GetChannelScriptVersion,
            CommandExecutionKind.Direct);
        services.RegisterCommandHandler<GetChannelScriptCommandHandler>(
            ApplicationCommandType.GetChannelScript,
            CommandExecutionKind.Direct);
        services.RegisterCommandHandler<GetChannelInfoCommandHandler>(
            ApplicationCommandType.GetChannelInfo,
            CommandExecutionKind.Direct);
        services.RegisterCommandHandler<AcknowledgeGameExitCommandHandler>(
            ApplicationCommandType.AcknowledgeGameExit,
            CommandExecutionKind.Direct);
        services.RegisterCommandHandler<LoginGameUserCommandHandler>(
            ApplicationCommandType.LoginGameUser,
            CommandExecutionKind.Direct,
            UserAccessMode.Exclusive);
        services.RegisterCommandHandler<GetAccountCharacterRosterCommandHandler>(
            ApplicationCommandType.GetAccountCharacterRoster,
            CommandExecutionKind.Direct,
            accountAccessMode: UserAccessMode.Shared);
        services.RegisterCommandHandler<SelectCharacterCommandHandler>(
            ApplicationCommandType.SelectCharacter,
            CommandExecutionKind.Workflow,
            accountAccessMode: UserAccessMode.Exclusive);
        services.AddNetworkMessageDispatching();
        services.RegisterMessageHandler<ChannelConnectHandler>(ClientMessageType.ChannelConnect);
        services.RegisterMessageHandler<ChannelScriptVersionHandler>(ClientMessageType.ChannelScriptVersion);
        services.RegisterMessageHandler<ChannelScriptHandler>(ClientMessageType.ChannelScript);
        services.RegisterMessageHandler<ChannelInfoHandler>(ClientMessageType.ChannelInfo);
        services.RegisterMessageHandler<GameExitHandler>(ClientMessageType.GameExit);
        services.RegisterMessageHandler<GameLoginHandler>(ClientMessageType.GameLogin);
        services.RegisterMessageHandler<GetAccountCharacterRosterHandler>(
            ClientMessageType.GameGetAccountCharacterRoster);
        services.RegisterMessageHandler<SelectCharacterHandler>(ClientMessageType.GameSelectCharacter);
    }
}
