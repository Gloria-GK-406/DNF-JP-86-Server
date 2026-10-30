using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.World;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Quests;

/// <summary>What the Quests domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class QuestsRegistration
{
    internal static IServiceCollection AddQuests(this IServiceCollection services)
    {
        // What the archive declares about quests, and the shared parameters a reward is worked
        // out from. One reader for both, because no quest outcome is decided without both.
        services.TryAddSingleton<IQuestContentRuleService, QuestContentRuleService>();
        services.TryAddSingleton<IQuestStateRuleService, QuestStateRuleService>();
        services.TryAddSingleton<IGiantDragonCrystalExchangeDomainService, GiantDragonCrystalExchangeDomainService>();
        services.TryAddSingleton<IQuestAvailabilityRuleService, QuestAvailabilityRuleService>();
        services.TryAddSingleton<IQuestFactsDomainService, QuestFactsDomainService>();
        services.TryAddSingleton<IQuestQueryDomainService, QuestQueryDomainService>();
        services.TryAddSingleton<ISeekingQuestProgressDomainService, SeekingQuestProgressDomainService>();
        services.TryAddSingleton<IQuestMaterialRequirementDomainService, QuestMaterialRequirementDomainService>();
        services.TryAddSingleton<IQuestRewardRuleService, QuestRewardRuleService>();
        services.TryAddSingleton<IQuestCompletionEffectRuleService, QuestCompletionEffectRuleService>();
        services.TryAddSingleton<IQuestCompletionDomainService, QuestCompletionDomainService>();
        // Recomputing a session's offer list after a level or a quest moved what it is made of.
        services.TryAddSingleton<
            IQuestOfferRefreshDomainService, QuestOfferRefreshDomainService>();
        services.TryAddSingleton<IQuestAcceptanceDomainService, QuestAcceptanceDomainService>();
        services.TryAddSingleton<IQuestDefinitionDomainService, QuestDefinitionDomainService>();
        services.TryAddSingleton<IQuestClearCouponRuleService, QuestClearCouponRuleService>();
        services.TryAddSingleton<IQuestClearancePlanRuleService, QuestClearancePlanRuleService>();
        services.TryAddSingleton<ICharacterQuestLogDomainService, CharacterQuestLogDomainService>();
        services.TryAddSingleton<IQuestTriggerDomainService, QuestTriggerDomainService>();
        services.TryAddSingleton<IQuestProgressDomainService, QuestProgressDomainService>();
        return services;
    }
}
