using Dfo.Server.Abstractions.Relations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Relations;

/// <summary>What the Relations domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class RelationsRegistration
{
    internal static IServiceCollection AddRelations(this IServiceCollection services)
    {
        // One owner behind two faces: readers take the query face only, and both must be the same
        // instance so there is never a second relation owner to fall out of step with.
        services.TryAddSingleton<CharacterRelationDomainService>();
        services.TryAddSingleton<ICharacterRelationDomainService>(sp => sp.GetRequiredService<CharacterRelationDomainService>());
        services.TryAddSingleton<ICharacterRelationQueryDomainService>(sp => sp.GetRequiredService<CharacterRelationDomainService>());
        return services;
    }
}
