using Dfo.Server.Application.Execution;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Events;

/// <summary>What the Events domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class EventsRegistration
{
    internal static IServiceCollection AddEvents(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDomainEvent<Dfo.Server.Abstractions.Events.ManagedEventConfigurationChanged>("activity.configuration-changed", 1);
        services.AddDomainSubscriber<Dfo.Server.Abstractions.Events.ManagedEventConfigurationChanged,
            Dfo.Server.Application.Events.EventsConfigurationNotificationSubscriber>("activity.refresh",
            Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.LiveOnly,
            fact => fact.EventId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        services.AddRuntimeEventSubscriber<Dfo.Server.Abstractions.Events.JoustLifecycleChanged,
            Dfo.Server.Application.Events.JoustLifecycleNotificationSubscriber>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Events.IWeeklyAttendanceRuleService, Dfo.Server.Application.Events.WeeklyAttendanceRuleService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Events.IJoustRuleService, Dfo.Server.Application.Events.JoustRuleService>();
        services.TryAddSingleton<Dfo.Server.Application.Events.JoustDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Events.IJoustDomainService>(sp => sp.GetRequiredService<Dfo.Server.Application.Events.JoustDomainService>());
        services.TryAddSingleton<Dfo.Server.Abstractions.Events.IJoustProjectionDomainService>(sp => sp.GetRequiredService<Dfo.Server.Application.Events.JoustDomainService>());
        services.AddSingleton<Dfo.Server.Abstractions.Events.IManagedEventDomainService>(sp => sp.GetRequiredService<Dfo.Server.Application.Events.JoustDomainService>());
        services.TryAddSingleton<Dfo.Server.Abstractions.Events.IGmJoustDomainService,Dfo.Server.Application.Events.GmJoustDomainService>();
        services.AddTypedGameplayTimer<Dfo.Server.Application.Events.JoustMaintenanceService>();
        services.AddSingleton<Dfo.Server.Abstractions.Events.IManagedEventDomainService, Dfo.Server.Application.Events.SaoTowerEntranceEventDomainService>();
        services.AddSingleton<Dfo.Server.Abstractions.Events.IManagedEventDomainService, Dfo.Server.Application.Events.SouthernDaleEntranceEventDomainService>();
        services.AddSingleton<Dfo.Server.Abstractions.Events.IManagedEventDomainService, Dfo.Server.Application.Events.SealedTreasureEntranceEventDomainService>();
        services.AddSingleton<Dfo.Server.Abstractions.Events.IManagedEventDomainService, Dfo.Server.Application.Events.GoldSilverHornEntranceEventDomainService>();
        services.AddSingleton<Dfo.Server.Abstractions.Events.IManagedEventDomainService, Dfo.Server.Application.Events.BossTowerEntranceEventDomainService>();
        services.AddSingleton<Dfo.Server.Abstractions.Events.IManagedEventDomainService, Dfo.Server.Application.Events.GuardianAltarEntranceEventDomainService>();
        services.AddSingleton<Dfo.Server.Abstractions.Events.IManagedEventDomainService, Dfo.Server.Application.Events.TauKingdomEntranceEventDomainService>();
        services.AddSingleton<Dfo.Server.Abstractions.Events.IManagedEventDomainService, Dfo.Server.Application.Events.HerasEntranceEventDomainService>();
        services.TryAddSingleton<Dfo.Server.Application.Events.WeeklyAttendanceDomainService>();
        services.AddSingleton<Dfo.Server.Abstractions.Events.IManagedEventDomainService>(sp => sp.GetRequiredService<Dfo.Server.Application.Events.WeeklyAttendanceDomainService>());
        services.TryAddSingleton<Dfo.Server.Abstractions.Events.IWeeklyAttendanceDomainService>(sp => sp.GetRequiredService<Dfo.Server.Application.Events.WeeklyAttendanceDomainService>());
        services.TryAddSingleton<Dfo.Server.Abstractions.Events.IEventManagementDomainService, Dfo.Server.Application.Events.EventManagementDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Events.IEventPlayerProjectionDomainService,Dfo.Server.Application.Events.EventPlayerProjectionDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Events.IGmEventsDomainService, Dfo.Server.Application.Events.GmEventsDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Events.IJoustMaintenanceDomainService, Dfo.Server.Application.Events.JoustMaintenanceDomainService>();
        services.AddInternalCommandHandler<Dfo.Server.Application.Events.MaintainJoustCommand, bool, Dfo.Server.Application.Events.MaintainJoustCommandHandler>();
        return services;
    }
}
