using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Rental;

/// <summary>What the Rental domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class RentalRegistration
{
    internal static IServiceCollection AddRental(this IServiceCollection services)
    {
        services.TryAddSingleton<Dfo.Server.Abstractions.Rental.ILuckyStarRuleService, Dfo.Server.Application.Rental.LuckyStarRuleService>();
        services.AddDomainEvent<Dfo.Server.Abstractions.Rental.LuckyStarChanged>("lucky-star.changed", 1);
        services.TryAddSingleton<Dfo.Server.Abstractions.Rental.IEquipmentRentalRuleService, Dfo.Server.Application.Rental.EquipmentRentalRuleService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Rental.IAccountLuckyStarDomainService, Dfo.Server.Application.Rental.AccountLuckyStarDomainService>();
        services.TryAddSingleton<Dfo.Server.Abstractions.Rental.IEquipmentRentalDomainService, Dfo.Server.Application.Rental.EquipmentRentalDomainService>();
        return services;
    }
}
