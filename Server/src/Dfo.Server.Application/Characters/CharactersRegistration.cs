using Dfo.Server.Application.Mail;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Parties;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Application.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Characters;

/// <summary>What the Characters domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class CharactersRegistration
{
    internal static IServiceCollection AddCharacters(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<ICharacterVisibleSessionDomainService, CharacterVisibleSessionDomainService>();
        services.TryAddSingleton<ICharacterStatRuleService, CharacterStatRuleService>();
        services.TryAddSingleton<CharacterSkillRuleService>();
        services.TryAddSingleton<ICharacterSkillRuleService>(sp => sp.GetRequiredService<CharacterSkillRuleService>());
        services.TryAddSingleton<ISkillPointDefinitionSource>(sp => sp.GetRequiredService<CharacterSkillRuleService>());
        services.TryAddSingleton<ICollectionBoxRuleService, CollectionBoxRuleService>();
        services.TryAddSingleton<ICollectionBoxDomainService, CollectionBoxDomainService>();
        services.TryAddSingleton<IExperienceCapsuleDomainService, ExperienceCapsuleDomainService>();

        // The rules a character's entry turns on, kept out of whatever reads or writes the rows
        // they apply to, so each can be checked without a database in reach.
        services.TryAddSingleton<AmbientStoreAccess>();
        services.TryAddSingleton<SelectedSessionPositionPersistence>();
        services.TryAddSingleton<ISelectedSessionPositionPersistence>(p => p.GetRequiredService<SelectedSessionPositionPersistence>());
        services.TryAddSingleton<ICharacterLocationDomainService>(p => p.GetRequiredService<SelectedSessionPositionPersistence>());
        // What a job starts with and what a creation request may ask for; and what entry
        // reconciles a character against on the way in.
        services.TryAddSingleton<ICharacterCreationRuleService, CharacterCreationRuleService>();
        services.TryAddSingleton<
            ICharacterEntryPreparationRuleService, CharacterEntryPreparationRuleService>();
        services.TryAddSingleton<IGoldLimitRuleService, GoldLimitRuleService>();
        services.TryAddSingleton<ICarriedGoldRuleService, CarriedGoldRuleService>();
        services.TryAddSingleton<IExpertSkillContractRuleService, ExpertSkillContractRuleService>();
        services.TryAddSingleton<IDungeonEffectRuleService, DungeonEffectRuleService>();
        services.TryAddSingleton<IWeaknessRuleService>(_ => new WeaknessRuleService(configuration));
        services.TryAddSingleton<IWeaknessDomainService, WeaknessDomainService>();
        services.TryAddSingleton<ICharacterDungeonDifficultyDomainService, CharacterDungeonDifficultyDomainService>();
        services.TryAddSingleton<ITutorialCompletionDomainService, TutorialCompletionDomainService>();
        // What a character has left to spend, asked by identifier and worked out on the spot. The
        // arithmetic belongs to the shared skill rules; the state service supplies current facts.
        services.TryAddSingleton<ICharacterSkillStateDomainService, CharacterSkillStateDomainService>();
        services.TryAddSingleton<ICharacterSkillResetRuleService, CharacterSkillResetRuleService>();
        services.TryAddSingleton<ICharacterAppearanceRuleService, CharacterAppearanceRuleService>();
        services.TryAddSingleton<ICharacterAppearanceDomainService, CharacterAppearanceDomainService>();
        services.TryAddSingleton<ICharacterOptionsDomainService, CharacterOptionsDomainService>();
        services.TryAddSingleton<ICharacterSkillLearningDomainService, CharacterSkillLearningDomainService>();
        services.TryAddSingleton<ICharacterStateProjectionDomainService, CharacterStateProjectionDomainService>();
        services.TryAddSingleton<
            ICharacterSkillPointDomainService, CharacterSkillPointDomainService>();
        // What a character being played is like right now: position, vitals, cooldowns and the
        // spirit potion, owned by the session generation that began it. The position is kept by the
        // location service, which only this runtime writes.
        services.TryAddSingleton<
            IOnlineCharacterRuntimeDomainService, OnlineCharacterRuntimeDomainService>();
        // A character's keys and options, asked by identifier. The entry load is the only other
        // reader of those two tables and it is left alone: it builds its frames from rows it has
        // already read, and this reads the same rows for anyone who is not entry.
        services.TryAddSingleton<
            ICharacterSettingsDomainService, CharacterSettingsDomainService>();
        // Configured allowance plus persistent, business-day usage accounting.
        services.TryAddSingleton<
            ICharacterFatigueDomainService, CharacterFatigueDomainService>();
        // The character entity sits below the world and the effects; the roster, which creates and draws
        // characters, sits above initialization and appearance, which both reach the entity.
        services.TryAddSingleton<ICharacterDomainService, CharacterDomainService>();
        services.TryAddSingleton<ICharacterRosterDomainService, CharacterRosterDomainService>();
        services.TryAddSingleton<ICharacterItemUseDomainService, CharacterItemUseDomainService>();
        services.TryAddSingleton<ICharacterExperienceAwardDomainService, CharacterExperienceAwardDomainService>();
        services.TryAddSingleton<ICharacterExperienceInitializationDomainService, CharacterExperienceInitializationDomainService>();
        services.AddInternalCommandHandler<InitializeCharacterExperienceStorageCommand, CharacterExperienceScale, InitializeCharacterExperienceStorageCommandHandler>();
        services.TryAddSingleton<ISelectCharacterPreparationDomainService, SelectCharacterPreparationService>();
        services.TryAddSingleton<ICharacterCurrentStateQueryDomainService, CharacterCurrentStateQueryDomainService>();
        // Thirty-three tables: the order they are read in, and everything that makes them one
        // character.
        services.TryAddSingleton<ICharacterEntryStateRepository, CharacterEntryAssembly>();
        services.TryAddSingleton<
            ICharacterDarkKnightComboDomainService, CharacterDarkKnightComboDomainService>();
        services.TryAddSingleton<IItemUseJudgementService, ItemUseJudgementService>();
        services.TryAddSingleton<ICharacterProfileEntryProjection, CharacterProfileEntryProjection>();
        services.TryAddSingleton<ICharacterSkillQuestEntryProjection, CharacterSkillQuestEntryProjection>();
        services.TryAddSingleton<ICharacterProgressionEntryProjection, CharacterProgressionEntryProjection>();
        services.TryAddSingleton<ICharacterFeatureEntryProjection, CharacterFeatureEntryProjection>();
        services.TryAddSingleton<ICharacterEntryStateDomainService, CharacterEntryStateDomainService>();
        services.TryAddSingleton<ISelectCharacterQuestEntryDomainService, SelectCharacterQuestEntryService>();
        services.TryAddSingleton<ISelectCharacterPetEntryDomainService, SelectCharacterPetEntryService>();
        // Creating an account if its login name is new, and putting its eight tables together.
        // Both are decisions, so both are here.
        // The character side's decisions. Reading and writing stayed in persistence.
        services.TryAddSingleton<ICharacterRepository, CharacterRosterService>();
        services.TryAddSingleton<ICharacterPvpSkillProfileRepository, PvpSkillProfileService>();
        services.TryAddSingleton<IExpertSkillContractReconciliationRepository,
            ExpertSkillContractReconciliationService>();
        services.TryAddSingleton<IDungeonPersistentEffectRecoveryRepository,
            DungeonEffectRecoveryService>();

        services.TryAddSingleton<
            IOtherCharacterDetailQueryDomainService,
            OtherCharacterDetailQueryDomainService>();
        return services;
    }
}
