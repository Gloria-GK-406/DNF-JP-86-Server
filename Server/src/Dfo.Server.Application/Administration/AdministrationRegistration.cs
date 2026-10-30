using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Administration;

/// <summary>What the Administration domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class AdministrationRegistration
{
    internal static IServiceCollection AddAdministration(this IServiceCollection services)
    {
        services.AddSingleton<IItemCatalogSelectionRuleService>(p => p.GetRequiredService<Dfo.Server.Abstractions.Administration.IGmMailRuleService>());
        services.TryAddSingleton<Dfo.Server.Application.Administration.GmPlayerRuntimeQuery>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmPlayerDirectoryDomainService, Dfo.Server.Application.Administration.GmPlayerDirectoryDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmAccessDomainService, Dfo.Server.Application.Administration.GmAccessDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmAuditDomainService, Dfo.Server.Application.Administration.GmAuditDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmCeraRuleService, Dfo.Server.Application.Administration.GmCeraRuleService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmCeraReminderDomainService, Dfo.Server.Application.Administration.GmCeraReminderDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmCeraDomainService, Dfo.Server.Application.Administration.GmCeraDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmMailRuleService, Dfo.Server.Application.Administration.GmMailRuleService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmOfficialMailDomainService, Dfo.Server.Application.Administration.GmOfficialMailDomainService>();
        services.AddInternalCommandHandler<Dfo.Server.Application.Administration.BootstrapGmAdministratorCommand, bool, Dfo.Server.Application.Administration.BootstrapGmAdministratorCommandHandler>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmDailyRefillDomainService, Dfo.Server.Application.Administration.GmDailyRefillDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmInitializationDomainService, Dfo.Server.Application.Administration.GmInitializationDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Administration.IGmCredentialReader, Dfo.Server.Application.Administration.GmCredentialReader>();
        return services;
    }
}
