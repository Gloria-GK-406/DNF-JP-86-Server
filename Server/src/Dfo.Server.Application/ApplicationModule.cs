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
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World.IllusionTower;
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
        services.AddAltarDungeons(configuration);
        services.AddMarriage(configuration);
        services.AddInventory();
        services.AddMirrorAradQuests();
        services.AddDungeonContents(configuration);
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
        services.AddExperience(configuration);
        services.AddUsers();
        services.AddQuests();
        services.TryAddSingleton<IQuestHelperGroundBonusDomainService, QuestHelperGroundBonusDomainService>();
        services.TryAddSingleton<IQuestCompletionSettlementDomainService, QuestCompletionSettlementService>();
        services.AddDungeonFlow();
        services.AddDungeonClearRewards(configuration);
        services.AddChannel();
        services.AddTowns();
        services.AddIllusionTower();
        services.AddDungeonDrops(configuration);
        services.AddCreatures();
        services.AddAccounts();
        services.TryAddSingleton<Dfo.Server.Abstractions.Configuration.IDailyResetRuleService, DailyResetRuleService>();
        services.AddRental();
        services.AddDungeonRuns();
        services.AddDomainEvent<ItemMirrorRemoved>("inventory.mirror-removed", 1);
        services.AddEffects();
        services.TryAddSingleton<IQuestClearCouponSettlementDomainService, QuestClearCouponSettlementService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Notices.INoticeStateDomainService, Dfo.Server.Application.Notices.NoticeStateDomainService>();
        services.AddDomainEvent<ClientAppearanceSettingsChanged>("settings.appearance-changed", 1);
        services.AddAdministration();
        services.AddInitialization();
        services.AddSingleton<IItemCatalogSelectionRuleService>(p => p.GetRequiredService<IDailyRefillConfigurationRuleService>());
        services.AddSessions();
        services.AddAuthentication(configuration);
        services.AddParties();
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
