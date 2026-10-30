using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Initialization;

/// <summary>What the Initialization domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class InitializationRegistration
{
    internal static IServiceCollection AddInitialization(this IServiceCollection services)
    {
        services.AddSingleton<IItemCatalogSelectionRuleService>(p => p.GetRequiredService<Dfo.Server.Abstractions.Initialization.IInitializationRuleService>());
        services.TryAddSingleton<Dfo.Server.Abstractions.Initialization.IInitializationRuleService, Dfo.Server.Application.Initialization.InitializationRuleService>();
        services.TryAddSingleton<Dfo.Server.Application.Initialization.InitializationAssetGrants>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Initialization.IInitializationDomainService, Dfo.Server.Application.Initialization.InitializationDomainService>();
        return services;
    }
}
