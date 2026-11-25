using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.DomainEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Pvp;

/// <summary>What the Pvp domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class PvpRegistration
{
    internal static IServiceCollection AddPvp(this IServiceCollection services)
    {
        // One owner behind two faces: readers take the query face only.
        services.TryAddSingleton<PvpRoomDomainService>();
        services.TryAddSingleton<PvpMatchmakingRuleService>();
        services.TryAddSingleton<PvpDuelDomainService>();
        services.TryAddSingleton<PvpTraditionalDuelDomainService>();
        services.TryAddSingleton<PvpDailyMissionRuleService>();
        services.TryAddSingleton<PvpColosseumRuleService>();
        services.TryAddSingleton<PvpTotalMatchTeamRuleService>();
        services.TryAddSingleton<PvpDailyMissions>();
        services.TryAddSingleton<PvpVictoryEmblems>();
        services.TryAddSingleton<PvpColosseumDomainService>();
        services.TryAddSingleton<IPvpColosseumDomainService>(sp => sp.GetRequiredService<PvpColosseumDomainService>());
        services.TryAddSingleton<IPvpDailyMissionQueryDomainService>(sp => sp.GetRequiredService<PvpDailyMissions>());
        services.TryAddSingleton<IPvpRoomDomainService>(sp => sp.GetRequiredService<PvpRoomDomainService>());
        services.TryAddSingleton<IPvpRoomQueryDomainService>(sp => sp.GetRequiredService<PvpRoomDomainService>());
        services.TryAddSingleton<IPvpMatchmakingDomainService>(sp => sp.GetRequiredService<PvpRoomDomainService>());
        services.TryAddSingleton<IPvpSettlementRuleService, PvpSettlementRuleService>();
        services.TryAddSingleton<IPvpRoomRuleService, PvpRoomRuleService>();
        services.TryAddSingleton(provider =>
        {
            var value = provider.GetService<IConfiguration>()?["Pvp:RatingSeasonId"];
            if (value is null) return PvpRatingSeason.First;
            if (!int.TryParse(value, out var season) || season <= 0)
                throw new InvalidOperationException("Pvp:RatingSeasonId must be a positive integer.");
            return new PvpRatingSeason(season);
        });
        services.TryAddSingleton<IPvpStartupVerification, PvpStartupVerification>();
        services.TryAddSingleton<IPvpSettlementDomainService, PvpSettlementDomainService>();
        services.TryAddSingleton<IPvpRecordDomainService, PvpRecordDomainService>();
        services.TryAddSingleton<IPvpTotalMatchRepresentativeSwitchDomainService, PvpTotalMatchRepresentativeSwitchDomainService>();
        services.TryAddSingleton<IPvpTotalMatchTeamDomainService, PvpTotalMatchTeamDomainService>();
        services.TryAddSingleton<IPvpTotalMatchCandidateQueryDomainService, PvpTotalMatchCandidateQueryDomainService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICharacterSkillGroupInitializer, PvpSkillInitializer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICharacterSkillGroupEditPolicy, PvpSkillEditPolicy>());
        // Published with no subscriber in the first phase; the fact is kept for whoever subscribes later.
        services.AddDomainEvent<PvpMatchSettled>("pvp.match-settled", 1);
        services.AddPvpDeadlineScheduler();
        return services;
    }
}
