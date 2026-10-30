using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Accounts;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Accounts;

/// <summary>What the Accounts domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class AccountsRegistration
{
    internal static IServiceCollection AddAccounts(this IServiceCollection services)
    {
        services.TryAddSingleton<IPremiumServiceDomainService, PremiumServiceDomainService>();
        services.TryAddSingleton<IPremiumServiceUsageDomainService, PremiumServiceUsageDomainService>();
        services.TryAddSingleton<IPremiumServiceRuleService, PremiumServiceRuleService>();
        services.TryAddSingleton<IQuestHelperPuppetRuleService, QuestHelperPuppetRuleService>();
        services.TryAddSingleton<IQuestHelperPuppetDomainService, QuestHelperPuppetDomainService>();
        services.TryAddSingleton<ICrystalContractDomainService, Dfo.Server.Application.Accounts.CrystalContractDomainService>();
        services.TryAddSingleton<IClientSettingsDomainService, ClientSettingsDomainService>();
        services.TryAddSingleton<IAccountSettingsEntryProjection, AccountSettingsEntryProjection>();
        services.TryAddSingleton<IAccountWalletEntitlementEntryProjection, AccountWalletEntitlementEntryProjection>();
        services.TryAddSingleton<IAccountProgressionEntryProjection, AccountProgressionEntryProjection>();
        services.TryAddSingleton<IAccountRelationCompatibilityEntryProjection, AccountRelationCompatibilityEntryProjection>();
        services.TryAddSingleton<IAccountEntryStateRepository, AccountEntryAssembly>();
        services.TryAddSingleton<IAccountEntryStateDomainService, AccountEntryStateDomainService>();
        services.TryAddSingleton<IAccountWalletRuleService, AccountWalletRuleService>();
        services.TryAddSingleton<IAccountWalletDomainService, AccountWalletDomainService>();
        return services;
    }
}
