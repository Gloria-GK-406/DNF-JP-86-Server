using Dfo.Server.Application.Administration;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.World;

/// <summary>What the World domain registers for AltarDungeons; called once from <see cref="ApplicationModule"/>.</summary>
internal static class AltarDungeonsRegistration
{
    internal static IServiceCollection AddAltarDungeons(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(AltarPolicyFrom(configuration));
        services.TryAddSingleton<IAltarDungeonRuleService, AltarDungeonRuleService>();
        services.TryAddSingleton<IGuardianAltarRuleService, GuardianAltarRuleService>();
        services.TryAddSingleton<GuardianAltarRuntimeStore>();
        services.TryAddSingleton<GuardianAltarDomainService>();
        services.TryAddSingleton<IGuardianAltarDomainService>(sp => sp.GetRequiredService<GuardianAltarDomainService>());
        services.AddDomainSubscriber<DungeonParticipantLeft, GuardianAltarDomainService>("guardian-altar.departure", Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.Reliable);
        services.TryAddSingleton<AltarDungeonEngine>();
        services.TryAddSingleton<EndlessAltarDomainService>();
        services.TryAddSingleton<UltimateAltarDomainService>();
        services.TryAddSingleton<AltarDungeonDomainService>();
        services.TryAddSingleton<IAltarDungeonDomainService>(sp => sp.GetRequiredService<AltarDungeonDomainService>());
        services.AddDomainEvent<AltarChallengeCompleted>("altar.challenge-completed", 1);
        services.AddDomainSubscriber<AltarChallengeCompleted, AltarDungeonQuestSubscriber>("quest.altar-complete", Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.Reliable);
        services.AddDomainSubscriber<DungeonParticipantLeft, AltarDungeonDomainService>("altar.departure", Dfo.Server.Abstractions.DomainEvents.DomainEventDeliveryPolicy.Reliable);
        return services;
    }

    private static AltarDungeonPolicy AltarPolicyFrom(IConfiguration configuration)
    {
        var windows = configuration.GetSection("AltarDungeons:OpeningWindows").GetChildren().Select(row =>
        {
            if (!int.TryParse(row["DungeonId"], out var id) || id is not (11006 or 11007)
                || !TimeOnly.TryParse(row["Opens"], System.Globalization.CultureInfo.InvariantCulture, out var opens)
                || !TimeOnly.TryParse(row["Closes"], System.Globalization.CultureInfo.InvariantCulture, out var closes))
                throw new InvalidDataException("Invalid altar opening window configuration.");
            return new AltarOpeningWindow(id, opens, closes);
        }).ToArray();
        return new() { OpeningWindows = windows };
    }
}
