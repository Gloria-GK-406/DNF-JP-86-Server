using Dfo.Server.Abstractions.ExpertJobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.ExpertJobs;

internal static class ExpertJobsRegistration
{
    internal static void AddExpertJobs(this IServiceCollection services)
    {
        services.TryAddSingleton<IExpertJobRuleService, ExpertJobRuleService>();
        services.TryAddSingleton<DisjointMachineRuleService>();
        services.TryAddSingleton<DisjointerOperations>();
        services.TryAddSingleton<ExpertJobProductionOperations>();
        services.TryAddSingleton<EnchanterStoreOperations>();
        services.TryAddSingleton<IExpertJobQueryDomainService, ExpertJobQueryDomainService>();
        services.TryAddSingleton<IExpertJobDomainService, ExpertJobDomainService>();
    }
}
