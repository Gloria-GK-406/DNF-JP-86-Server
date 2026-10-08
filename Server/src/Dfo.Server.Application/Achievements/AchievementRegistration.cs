using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Achievements;

internal static class AchievementRegistration
{
    internal static IServiceCollection AddAchievements(this IServiceCollection services)
    {
        services.TryAddSingleton<IAchievementRuleService, AchievementRuleService>();
        services.TryAddSingleton<ITitleBookRuleService, TitleBookRuleService>();
        services.TryAddSingleton<IAchievementDomainService, AchievementDomainService>();
        services.TryAddSingleton<IAchievementCharacterFactsDomainService, AchievementCharacterFactsDomainService>();
        services.TryAddSingleton<ITitleBookDomainService, TitleBookDomainService>();
        services.TryAddSingleton<ICharacterGameplayFactDomainService, CharacterGameplayFactDomainService>();
        services.AddDomainEvent<MonsterDefeated>("character.monster-defeated", 1);
        services.AddDomainEvent<DungeonCompleted>("character.dungeon-completed", 1);
        services.AddDomainEvent<QuestCompleted>("character.quest-completed", 1);
        services.AddDomainEvent<ItemUsed>("character.item-used", 1);
        services.AddDomainEvent<MaterialConsumed>("character.material-consumed", 1);
        services.AddDomainEvent<CharacterLevelChanged>("character.level-changed", 1);
        services.AddDomainEvent<AchievementProgressChanged>("achievement.progress-changed", 1);
        services.AddDomainEvent<AchievementCompleted>("achievement.completed", 1);
        services.AddDomainEvent<TitleUnlocked>("title.unlocked", 1);
        services.AddDomainEvent<TitleStored>("title.stored", 1);
        services.AddDomainEvent<TitleWithdrawn>("title.withdrawn", 1);
        services.AddDomainSubscriber<MonsterDefeated, AchievementDomainService>("achievement.monster", DomainEventDeliveryPolicy.Reliable);
        services.AddDomainSubscriber<DungeonCompleted, AchievementDomainService>("achievement.dungeon", DomainEventDeliveryPolicy.Reliable);
        services.AddDomainSubscriber<QuestCompleted, AchievementDomainService>("achievement.quest", DomainEventDeliveryPolicy.Reliable);
        services.AddDomainSubscriber<ItemUsed, AchievementDomainService>("achievement.item", DomainEventDeliveryPolicy.Reliable);
        services.AddDomainSubscriber<MaterialConsumed, AchievementDomainService>("achievement.material", DomainEventDeliveryPolicy.Reliable);
        services.AddDomainSubscriber<CharacterLevelChanged, AchievementDomainService>("achievement.level", DomainEventDeliveryPolicy.Reliable);
        services.AddDomainSubscriber<AchievementCompleted, TitleBookDomainService>("title.unlock", DomainEventDeliveryPolicy.Reliable);
        return services;
    }
}
