using Dfo.Server.Application.Execution;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Protocol.Game.Weakness;

/// <summary>What the Weakness slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class WeaknessRegistration
{
    internal static IServiceCollection AddWeaknessSlice(this IServiceCollection services)
    {
        services.TryAddSingleton<WeaknessRecoveryService>(provider => new(provider.GetRequiredService<ICommandBus>(),
            provider.GetRequiredService<Dfo.Server.Abstractions.Characters.IWeaknessDomainService>().RecoveryInterval,
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<WeaknessRecoveryService>>()));
        services.AddTypedGameplayTimer<WeaknessRecoveryService>();
        services.AddInternalCommandHandler<RecoverWeaknessTickCommand, bool, RecoverWeaknessTickCommandHandler>();
        services.AddGameNotiMessageEncoder<WeaknessProjectionNotification, WeaknessProjectionEncoder>();
        return services;
    }
}
