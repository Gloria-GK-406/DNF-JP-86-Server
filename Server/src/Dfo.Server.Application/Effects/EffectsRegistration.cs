using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Application.Effects;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Effects;

/// <summary>What the Effects domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class EffectsRegistration
{
    internal static IServiceCollection AddEffects(this IServiceCollection services)
    {
        services.TryAddSingleton<IConsumableStateDomainService, ConsumableStateDomainService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, TownPositionEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetCharacterLevelEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, DungeonEntryAllowanceEffectHandler>());
        services.TryAddSingleton<IApplyEffectsDomainService, ApplyEffectsDomainService>();
        services.TryAddSingleton<IInventoryEffectPreflightDomainService, InventoryEffectPreflightDomainService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, GrantPreparedItemEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInventoryEffectPreviewHandler, GrantPreparedItemEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, GrantOpeningGoldEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInventoryEffectPreviewHandler, GrantOpeningGoldEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, GrantOpeningVirtualEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInventoryEffectPreviewHandler, GrantOpeningVirtualEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetOpeningPremiumEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInventoryEffectPreviewHandler, SetOpeningPremiumEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetSeriaLuckEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInventoryEffectPreviewHandler, SetSeriaLuckEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetDoublePotUsageEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInventoryEffectPreviewHandler, SetDoublePotUsageEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SpendGoldEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, GrantStackableEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, GrantBasicCharacterItemEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetQuestRewardGoldEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, CharacterExperienceEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetCharacterGrowTypeEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetCharacterAwakeningEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, MaterializeSkillBuildsEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetEquipmentUpgradeEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetEquipmentEnchantmentEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetEquipmentDimensionalEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetEquipmentMagicSealEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetEquipmentForgingEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, DestroyEquipmentEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, ConsumeFromContainerEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, StartItemCooldownEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, StartSpiritPotionEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, AddPetStomachEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetFatigueStateEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetPurchaseRestrictionEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetItemUsageStateEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetCarriedQuestEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, RemoveTrackedQuestEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetClearedQuestEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, SetLearnedSkillEffectHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEffectHandler, RenamePetEffectHandler>());
        services.TryAddSingleton<Dfo.Server.Abstractions.Effects.IReplayStateDomainService, Dfo.Server.Application.Effects.ReplayStateDomainService>();
        return services;
    }
}
