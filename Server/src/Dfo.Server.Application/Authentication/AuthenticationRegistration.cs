using Dfo.Server.Application.Execution;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Authentication;

/// <summary>What the Authentication domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class AuthenticationRegistration
{
    internal static IServiceCollection AddAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<ILoginSessionMaintenanceDomainService, LoginSessionMaintenanceDomainService>();
        services.AddInternalCommandHandler<SweepLoginSessionsCommand, bool, SweepLoginSessionsCommandHandler>();
        services.TryAddSingleton<IPasswordCredentialRuleService, PasswordCredentialService>();
        services.TryAddSingleton<PasswordComputationLimiter>();
        services.TryAddSingleton<ILauncherCredentialReader, LauncherCredentialReader>();
        services.TryAddSingleton(AuthenticationRuleService.From(configuration));
        services.TryAddSingleton(LoginSessionOptions.From(configuration));
        services.TryAddSingleton<LoginSessionRegistry>();
        services.AddTypedGameplayTimer<LoginSessionMaintenanceService>();
        services.TryAddSingleton<IAuthenticationDomainService, AuthenticationDomainService>();
        services.TryAddSingleton<IGameLoginAuthenticationDomainService, GameLoginAuthenticationDomainService>();
        return services;
    }
}
