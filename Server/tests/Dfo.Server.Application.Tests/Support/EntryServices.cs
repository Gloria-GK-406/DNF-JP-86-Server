using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Game;
using Dfo.Server.Protocol.Game.SelectCharacter;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>
/// Builds a <see cref="SelectCharacterPreparationService"/> for tests, defaulting every
/// collaborator a test is not interested in.
/// </summary>
/// <remarks>
/// The handler intentionally exposes every participating domain. Funnelling test-only
/// defaults through here keeps unrelated behavior tests focused without hiding the
/// production dependency boundary.
/// </remarks>
internal static class EntryServices
{
    public static SelectCharacterPreparationService Create(
        ICharacterDomainService characters,
        ITestInventoryEntryServices inventory,
        ICharacterEntryStateDomainService characterEntries,
        IAccountEntryStateDomainService accountEntries,
        IActiveCharacterDomainService activeCharacters,
        IWorldContentEntryRuleService? worldContent = null,
        ISkillPointDefinitionSource? skillPoints = null,
        ICharacterPvpSkillProfileRepository? pvpSkills = null,
        IDungeonPersistentEffectRecoveryRepository? dungeonEffects = null,
        IExpertSkillContractDefinitionSource? expertContracts = null,
        IExpertSkillContractReconciliationRepository? expertSkillReconciliation = null,
        ICreatureEquipmentLifetimeSource? creatureLifetimes = null,
        IChannelEntryPolicy? channels = null,
        IEntryPlacementRuleService? placement = null,
        ISessionAdjacentEntryDomainService? sessionAdjacent = null,
        TimeProvider? timeProvider = null) =>
        new(characters,
            inventory,
            inventory,
            characterEntries,
            accountEntries,
            new EquippedCreatureQuery(),
            worldContent ?? new StubWorldContentEntryRuleService(),
            new CharacterEntryPreparationRuleService(
                skillPoints ?? new StubSkillPointDefinitionSource(),
                expertContracts ?? new StubExpertContractDefinitionSource(),
                creatureLifetimes ?? new StubCreatureEquipmentLifetimeSource()),
            pvpSkills ?? new StubPvpSkillProfileRepository(),
            dungeonEffects ?? new StubDungeonEffectRecoveryRepository(),
            expertSkillReconciliation ?? new StubExpertSkillReconciliationRepository(),
            channels ?? new StubChannelEntryPolicy(),
            placement ?? new StubEntryPlacementRuleService(),
            sessionAdjacent ?? new StubSessionAdjacentEntryDomainService(),
            timeProvider ?? TimeProvider.System);

    public static async ValueTask<SelectCharacterResult> EnterAsync(
        this SelectCharacterPreparationService handler,
        AccountId accountId,
        ushort slotIndex,
        ChannelEntryIdentity channel,
        CancellationToken cancellationToken) =>
        await handler.PrepareAsync(
            await handler.PreflightAsync(accountId, slotIndex, channel, cancellationToken),
            accountId, channel, cancellationToken);
}

internal sealed class StubPvpSkillProfileRepository : ICharacterPvpSkillProfileRepository
{
    public ValueTask<Dfo.Server.Data.Characters.CharacterSkillsEntryState> LoadOrInitializeAsync(
        AccountId accountId, Dfo.Server.Data.Characters.CharacterId characterId,
        Dfo.Server.Data.Characters.CharacterSkillsEntryState initialProfile,
        CancellationToken cancellationToken) => ValueTask.FromResult(initialProfile);
}

internal sealed class StubDungeonEffectRecoveryRepository : IDungeonPersistentEffectRecoveryRepository
{
    public ValueTask<DungeonPersistentEffectRecoveryOutcome> RecoverAsync(
        AccountId accountId, Dfo.Server.Data.Characters.CharacterId characterId,
        CancellationToken cancellationToken) => ValueTask.FromResult(new DungeonPersistentEffectRecoveryOutcome(0,0,0,0));
}
internal sealed class StubExpertContractDefinitionSource : IExpertSkillContractDefinitionSource
{ public IReadOnlySet<int> GetOverSkillPremiumTypes() => new HashSet<int>(); }
internal sealed class StubExpertSkillReconciliationRepository : IExpertSkillContractReconciliationRepository
{
    public ValueTask<bool> ReconcileAsync(AccountId accountId, Dfo.Server.Data.Characters.CharacterId characterId,
        IReadOnlySet<int> premiumTypes, IReadOnlyDictionary<int, SkillPointCostDefinition> definitions,
        IReadOnlyDictionary<int, byte> freeBaseline, CancellationToken cancellationToken) => ValueTask.FromResult(false);
}

internal sealed class StubCreatureEquipmentLifetimeSource : ICreatureEquipmentLifetimeSource
{
    public ValueTask<CreatureEquipmentLifetime> GetAsync(
        int itemTemplateId, DateTimeOffset preparedAt, CancellationToken cancellationToken) =>
        ValueTask.FromResult(default(CreatureEquipmentLifetime));
}

internal sealed class StubSkillPointDefinitionSource : ISkillPointDefinitionSource
{
    public ValueTask<SkillPointDefinitionSet> GetAsync(
        byte job, byte packedGrowType, byte characterLevel,
        IReadOnlyCollection<int> learnedSkillIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new SkillPointDefinitionSet(0, 0, [],
            new Dictionary<int, SkillPointCostDefinition>()));
}

internal interface ITestInventoryEntryServices
    : ICharacterInventoryEntryDomainService, IAccountInventoryEntryDomainService;
