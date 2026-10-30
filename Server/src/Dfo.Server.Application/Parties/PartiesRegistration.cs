using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Parties;

/// <summary>What the Parties domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class PartiesRegistration
{
    internal static IServiceCollection AddParties(this IServiceCollection services)
    {
        services.TryAddSingleton<IPartyMemberStatusDomainService, PartyMemberStatusDomainService>();
        services.TryAddSingleton<IPartyModeRuleService, PartyModeRuleService>();
        services.TryAddSingleton<IPartyMemberSessionEndpointSource,
            PartyMemberSessionEndpointSource>();
        services.TryAddSingleton<PartyService>();
        services.TryAddSingleton<IPartyService>(provider =>
            provider.GetRequiredService<PartyService>());
        return services;
    }
}
