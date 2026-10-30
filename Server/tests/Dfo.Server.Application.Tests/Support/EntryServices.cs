using Dfo.Server.Testing;
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
        ICharacterRosterDomainService characters,
        ITestInventoryEntryServices inventory,
        ICharacterEntryStateDomainService characterEntries,
        IAccountEntryStateDomainService accountEntries,
        IWorldContentEntryRuleService? worldContent = null,
        ISkillPointDefinitionSource? skillPoints = null,
        ICharacterPvpSkillProfileRepository? pvpSkills = null,
        IDungeonPersistentEffectRecoveryRepository? dungeonEffects = null,
        IExpertSkillContractDefinitionSource? expertContracts = null,
        IExpertSkillContractReconciliationRepository? expertSkillReconciliation = null,
        ICreatureEquipmentLifetimeSource? creatureLifetimes = null,
        IChannelEntryDomainService? channels = null,
        IEntryPlacementRuleService? placement = null,
        ISessionAdjacentEntryDomainService? sessionAdjacent = null,
        ISelectedSessionPositionPersistence? positions = null,
        TimeProvider? timeProvider = null) =>
        new(characters,
            inventory,
            inventory,
            characterEntries,
            accountEntries,
            new Dfo.Server.Application.Characters.CharacterAppearanceRuleService(),
            new EquippedCreatureQuery(),
            worldContent ?? new StubWorldContentEntryRuleService(),
            new CharacterEntryPreparationRuleService(
                skillPoints ?? new StubSkillPointDefinitionSource(),
                expertContracts ?? new StubExpertContractDefinitionSource(),
                creatureLifetimes ?? new StubCreatureEquipmentLifetimeSource(), logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<Dfo.Server.Application.Characters.CharacterEntryPreparationRuleService>.Instance),
            pvpSkills ?? new StubPvpSkillProfileRepository(),
            dungeonEffects ?? new StubDungeonEffectRecoveryRepository(),
            expertSkillReconciliation ?? new StubExpertSkillReconciliationRepository(),
            channels ?? new StubChannelEntryPolicy(),
            placement ?? new StubEntryPlacementRuleService(),
            sessionAdjacent ?? new StubSessionAdjacentEntryDomainService(),
            positions ?? new NoPositionPersistence(),
            timeProvider ?? TimeProvider.System, events: TestRequired.Absent<Dfo.Server.Abstractions.Events.IEventManagementDomainService>(), attendance: TestRequired.Absent<Dfo.Server.Abstractions.Events.IWeeklyAttendanceDomainService>(), joust: TestRequired.Absent<Dfo.Server.Abstractions.Events.IJoustProjectionDomainService>(), epicBook: TestRequired.Absent<Dfo.Server.Abstractions.Inventory.IEpicBookDomainService>(), achievements: TestRequired.Absent<Dfo.Server.Abstractions.Achievements.IAchievementDomainService>(), titles: TestRequired.Absent<Dfo.Server.Abstractions.Achievements.ITitleBookDomainService>(), marriage: TestRequired.Absent<Dfo.Server.Abstractions.Marriage.IMarriageQueryDomainService>(), weddingQuests: TestRequired.Absent<Dfo.Server.Abstractions.Marriage.IMarriageQuestDomainService>(), housing: TestRequired.Absent<Dfo.Server.Abstractions.Marriage.IMarriageHousingQueryDomainService>(), capsules: TestRequired.Absent<Dfo.Server.Abstractions.Marriage.IMarriageCapsuleQueryDomainService>(), collections: TestRequired.Absent<Dfo.Server.Abstractions.Characters.ICollectionBoxDomainService>(), southern: TestRequired.Absent<Dfo.Server.Abstractions.World.ISouthernDaleQuestDomainService>(), crystalExchange: TestRequired.Absent<Dfo.Server.Abstractions.Quests.IGiantDragonCrystalExchangeDomainService>());

    /// <summary>Entry fixtures do not exercise position storage; the channel decides whether it runs.</summary>
    private sealed class NoPositionPersistence : ISelectedSessionPositionPersistence
    {
        public ValueTask PersistAsync(
            Dfo.Server.Data.Accounts.AccountId accountId,
            Dfo.Server.Data.Characters.CharacterId characterId,
            Dfo.Server.Data.Characters.CharacterPositionState position,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

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
    public ValueTask<Dfo.Server.Data.Characters.CharacterSkillsEntryState> ReadAsync(
        AccountId accountId, Dfo.Server.Data.Characters.CharacterId characterId,
        CancellationToken cancellationToken) => throw new NotSupportedException();
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
