using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Tests.Support;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class SelectCharacterEntryLifecycleServiceTests
{
    [Fact]
    public async Task FreshEntryReloadsInventoryWithoutCarryingThePreviousSessionCopy()
    {
        var result = WithMainContainer(
            await CharacterEntryInventoryTests.CreateLifecycleResultAsync(new AccountId(600), 600, 1));
        var previous = Session(result);
        var next = previous with { ConnectionGeneration = previous.ConnectionGeneration + 1 };
        var loader = new InventoryLoader(result.CharacterInventory);
        var containers = new CharacterContainerDomainService();
        var service = new SelectCharacterEntryInventoryDomainService(
            new RefillConfiguration(new RefillContent()), new RefillMutation(), loader,
            containers, new VolatileItemDomainStore(), new DomainTransactionScope(),
            null!, null!, null!, transientStores: new Dfo.Server.Persistence.Inventory.TransientItemDomainStoreFactory());

        var updated = await service.PrepareLeaseAsync(next, result, CancellationToken.None);

        Assert.Equal(result.CharacterInventory, updated.CharacterInventory);
        Assert.Equal(1, loader.LoadCount);
        Assert.Equal(ContainerId.ForAccount(next.AccountId, InventoryContainerType.SharedMaterial),
            containers.GetContainer(next.CharacterId, InventoryContainerType.SharedMaterial).Id);
        Assert.DoesNotContain(updated.CharacterInventory.Containers,
            container => container.ContainerType == InventoryContainerType.SharedMaterial);
    }

    [Fact]
    public async Task InventoryPreparation_ReloadsCommittedGroups()
    {
        var result = await CharacterEntryInventoryTests.CreateLifecycleResultAsync(new AccountId(601), 601, 1);
        var session = Session(result);
        var reloaded = result.CharacterInventory with
        {
            Containers = [
                new InventoryContainerState(InventoryContainerType.Main, 0,
                    [new InventoryItemState(new ItemIdentity(1), 65, Core(itemKind: 4, itemId: 9001, value: 2))]),
                new InventoryContainerState(InventoryContainerType.Equipment, 0, []),
                new InventoryContainerState(InventoryContainerType.Avatar, 0, [])]
        };
        var service = new SelectCharacterEntryInventoryDomainService(
            new RefillConfiguration(new RefillContent()), new RefillMutation(), new InventoryLoader(reloaded),
            new CharacterContainerDomainService(),
            new VolatileItemDomainStore(), new DomainTransactionScope(), null!, null!, null!, transientStores: new Dfo.Server.Persistence.Inventory.TransientItemDomainStoreFactory());
        _ = await service.PrepareLeaseAsync(session, result, CancellationToken.None);

        var prepared = await service.ApplyDailyRefillAsync(session, result, CancellationToken.None);

        Assert.Equal(reloaded, prepared.Result.CharacterInventory);
        var update = Assert.Single(prepared.CommittedUpdates);
        Assert.Equal(InventoryContainerType.Main, update.ContainerType);
        Assert.Equal(65, Assert.Single(update.Items).SlotIndex);
    }

    [Fact]
    public async Task InventoryPreparation_MutationFailureDoesNotRefreshAnUnusedLease()
    {
        var result = WithMainContainer(
            await CharacterEntryInventoryTests.CreateLifecycleResultAsync(new AccountId(605), 605, 1));
        var session = Session(result);
        var loader = new InventoryLoader(result.CharacterInventory);
        var service = new SelectCharacterEntryInventoryDomainService(
            new RefillConfiguration(new RefillContent()), new FailingRefillMutation(), loader,
            new CharacterContainerDomainService(), new VolatileItemDomainStore(), new DomainTransactionScope(),
            null!, null!, null!, transientStores: new Dfo.Server.Persistence.Inventory.TransientItemDomainStoreFactory());
        _ = await service.PrepareLeaseAsync(session, result, CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(async () =>
            await service.ApplyDailyRefillAsync(session, result, CancellationToken.None));

        Assert.Equal(1, loader.LoadCount);
    }

    [Fact]
    public async Task QuestSynchronization_UsesVirtualGoldAndCountsCreatureAsOneInstance()
    {
        var result = await CharacterEntryInventoryTests.CreateLifecycleResultAsync(new AccountId(602), 602, 1);
        result = result with
        {
            CharacterEntry = result.CharacterEntry with
            {
                Quests = result.CharacterEntry.Quests with
                {
                    ActiveQuests = [new CharacterQuestProgressState(0, 100, 0),
                        new CharacterQuestProgressState(1, 101, 0)]
                }
            },
            CharacterInventory = result.CharacterInventory with
            {
                VirtualResources = [new InventoryRuntimeValueState(0, 0, 7)],
                Containers = [new InventoryContainerState(InventoryContainerType.Main, 0,
                    [new InventoryItemState(new ItemIdentity(1), 65, Core(itemKind: 5, itemId: 99, value: 999_999))]),
                    new InventoryContainerState(InventoryContainerType.Equipment, 0, []),
                    new InventoryContainerState(InventoryContainerType.Avatar, 0, [])]
            }
        };
        var logs = new RecordingQuestLogs
        {
            Carried =
            {
                [602] = [new CarriedQuest(0, 100, 0), new CarriedQuest(1, 101, 0)],
            },
        };
        var service = new SelectCharacterQuestEntryService(
            new QuestContentRuleService(
                new QuestSource(new Dictionary<int, IReadOnlyList<SeekingItemRequirement>>
                {
                    [100] = [new SeekingItemRequirement(0, 10)],
                    [101] = [new SeekingItemRequirement(99, 2)],
                }),
                new NoQuestParameters()),
            logs);

        var projection = await service.SynchronizeAsync(Session(result), result, CancellationToken.None);

        // The whole of each write, not just the value: the slot says which row was corrected, and
        // the quest as last seen is the guard that stands between this snapshot and a row that
        // moved since. A test that watched only the new value would pass with either thrown away.
        Assert.Equal(
            [(new CarriedQuest(0, 100, 0), 3), (new CarriedQuest(1, 101, 0), 1)],
            logs.ProgressWrites);
        Assert.Equal([3u, 1u], projection!.State.Quests.Select(value => value.TriggerValue));
        Assert.Equal([3, 1], projection.Result.CharacterEntry.Quests.ActiveQuests
            .Select(value => value.TriggerValue));
    }

    [Fact]
    public async Task PetRevival_EmitsOnlyTheCommittedDeadToAliveTransition()
    {
        var result = await CharacterEntryInventoryTests.CreateLifecycleResultAsync(new AccountId(603), 603, 1);
        result = result with
        {
            CharacterEntry = result.CharacterEntry with
            {
                Features = result.CharacterEntry.Features with
                {
                    Pets = [new CharacterPetRuntimeState(77, 0, 0, 0, 0, 0, 0, [], 0)]
                }
            }
        };
        var characters = new RecordingCharacterDomainStore();
        characters.Creatures[(603, 77)] = new StoredCreature(77, 0);
        var accessor = new OpenTransactionAccessor();
        var items = new VolatileItemDomainStore();
        // The creature is named by its identity and by nothing else. Its core carries a different
        // number on purpose: that value is what the wire shows for a creature, it is not the key
        // its condition is kept under, and a revival that went looking by it would find the wrong
        // creature or none.
        items.Seed(ContainerId.ForCharacter(new CharacterId(603), InventoryContainerType.Equipment), 25,
            ItemSnapshot.FromCore(Core(itemKind: 5, itemId: 1, value: 999), false, new(77),
                creature: new(0, 0, 0, 0, 0, 1, [], 0)));
        var service = new SelectCharacterPetEntryService(
            characters, items, new AmbientStoreAccess(accessor));

        var changed = await service.ReviveAsync(Session(result), result, CancellationToken.None);
        var unchanged = await service.ReviveAsync(Session(result), result, CancellationToken.None);

        Assert.Equal(new PetRevivalState(603, 77, 1), changed!.State);
        Assert.Equal(1, Assert.Single(changed.Result.CharacterEntry.Features.Pets).Field04);
        Assert.Null(unchanged);
    }

    /// <summary>
    /// Entry is where a character acquires the container its items come from.
    /// </summary>
    /// <remarks>
    /// The shape comes from the inventory that was just loaded rather than from the container type,
    /// because capacity is a fact about this character: a player who paid to expand their bag would
    /// otherwise be told it is the size everyone else's is. Nothing else in the run re-derives it,
    /// so a wrong value here reaches the client and nothing fails.
    /// </remarks>
    [Fact]
    public async Task Entry_binds_the_character_to_its_own_main_equipment_and_avatar_containers()
    {
        var result = await CharacterEntryInventoryTests.CreateLifecycleResultAsync(
            new AccountId(606), 606, 1);
        var session = Session(result);
        // An expanded bag: the parameter is this character's, not the container type's.
        var expanded = result.CharacterInventory with
        {
            Containers = [
                new InventoryContainerState(InventoryContainerType.Main, 156, []),
                new InventoryContainerState(InventoryContainerType.Equipment, 0, []),
                new InventoryContainerState(InventoryContainerType.Avatar, 0, [])],
        };
        var containers = new CharacterContainerDomainService();
        var service = new SelectCharacterEntryInventoryDomainService(
            new RefillConfiguration(new RefillContent()), new RefillMutation(),
            new InventoryLoader(expanded), containers,
            new VolatileItemDomainStore(), new DomainTransactionScope(), null!, null!, null!, transientStores: new Dfo.Server.Persistence.Inventory.TransientItemDomainStoreFactory());

        var prepared = await service.PrepareLeaseAsync(session, result, CancellationToken.None);

        var loaded = prepared.CharacterInventory.Containers.Single(
            value => value.ContainerType == InventoryContainerType.Main);
        var bound = containers.GetContainer(session.CharacterId, InventoryContainerType.Main);
        Assert.Equal(InventoryContainerType.Main, bound.Type);
        Assert.Equal(loaded.Parameter, bound.Shape.Parameter);
        Assert.Equal(156, bound.Shape.Parameter);
        Assert.False(bound.IsRemovable);
        Assert.Equal([bound], containers.GetContainers(
            session.CharacterId, InventoryContainerType.Main));

        // Wearing a piece is a move between the two lists, so both ends have to be bound. Before
        // 2026-08-26 only the bag was, and a player's drag onto an equipment slot came back refused
        // for a list this server did not serve.
        var equipment = containers.GetContainer(
            session.CharacterId, InventoryContainerType.Equipment);
        Assert.Equal(InventoryContainerType.Equipment, equipment.Type);
        Assert.False(equipment.IsRemovable);

        // The avatar list is the third, bound on 2026-09-07 after the identical symptom: a drag
        // inside it came back `ListNotServed`, before the item was read, so the refusal could not
        // even say which costume it was about. A loaded inventory always carries this container --
        // the entry projection emits every container type -- so an absent one is a fixture that
        // does not match a loaded inventory rather than a state a character can be in.
        var avatar = containers.GetContainer(session.CharacterId, InventoryContainerType.Avatar);
        Assert.Equal(InventoryContainerType.Avatar, avatar.Type);
        Assert.False(avatar.IsRemovable);
    }

    /// <summary>
    /// Gives the fixture's result the main container a loaded inventory always has.
    /// </summary>
    /// <remarks>
    /// The entry projection emits every container type, empty ones included, so entry binds the
    /// character to the main one and to the equipment it wears. The shared fixture builds its state
    /// by hand and leaves the list empty, which is a shape no load produces.
    /// </remarks>
    private static SelectCharacterResult WithMainContainer(SelectCharacterResult result) =>
        result with
        {
            CharacterInventory = result.CharacterInventory with
            {
                Containers = [
                    new InventoryContainerState(InventoryContainerType.Main, 0, []),
                    new InventoryContainerState(InventoryContainerType.Equipment, 0, []),
                    new InventoryContainerState(InventoryContainerType.Avatar, 0, [])],
            },
        };

    private static SelectedSessionIdentity Session(SelectCharacterResult result) =>
        SelectedSessionIdentity.Create(result.Account.AccountId, result.Character.Identity.CharacterId,
            new NetworkConnectionId("Game:entry-service"), 1, 10011);

    private static InventoryItemCoreState Core(byte itemKind, int itemId, int value) => new(
        itemKind, itemId, value, 0, 0, 0, 0, 0, 0, 0, -1,
        new ChronicleOptionState(0, 0, 0, 0, 0), new ChronicleOptionState(0, 0, 0, 0, 0),
        0, 0, 0, 0, 0,
        new RandomOptionState(0, 0, 0), new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0), 0, 0xFF, 0,
        new RandomOptionState(0, 0, 0), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private sealed class RefillContent : IEntryLifecycleContentSource
    {
        public ValueTask<IReadOnlyList<DailyRefillRule>> GetDailyRefillRulesAsync(
            DateTimeOffset preparedAt, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<DailyRefillRule>>(
                [new(9001, 2, DateTimeOffset.MaxValue, DailyRefillMode.RefillToTarget, 10, 4,
                    new(BusinessInventorySpace.Quest, 0, 55))]);
    }

    private sealed class RefillMutation : IDailyRefillDomainService
    {

        public ValueTask<DailyRefillMutationResult> ApplyDailyRefillAsync(
            AccountId accountId, CharacterId characterId, IReadOnlyList<DailyRefillRule> rules,
            DateTimeOffset preparedAt, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new DailyRefillMutationResult(
                [new DailyRefillCommittedGrant(new(BusinessInventorySpace.Consumable, 0), 9001, 2, 2)]));
    }

    private sealed class FailingRefillMutation : IDailyRefillDomainService
    {

        public ValueTask<DailyRefillMutationResult> ApplyDailyRefillAsync(
            AccountId accountId, CharacterId characterId, IReadOnlyList<DailyRefillRule> rules,
            DateTimeOffset preparedAt, CancellationToken cancellationToken) =>
            ValueTask.FromException<DailyRefillMutationResult>(new IOException("transaction failed"));
    }

    private sealed class InventoryLoader(CharacterInventoryEntryState inventory)
        : ICharacterInventoryEntryDomainService
    {
        public int LoadCount { get; private set; }

        public ValueTask<CharacterInventoryEntryState> ReadCurrentAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) => LoadAsync(accountId, characterId, cancellationToken);

        public ValueTask<CharacterInventoryEntryState> LoadAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
        {
            LoadCount++;
            return ValueTask.FromResult(inventory);
        }
    }

    /// <summary>Nothing reached through here works out a quest reward.</summary>
    private sealed class NoQuestParameters : IQuestParameterSource
    {
        public QuestParameters Load() =>
            throw new NotSupportedException(
                "No test on this path asks what a quest pays; a value here would be invented.");
    }

    private sealed class QuestSource(
        IReadOnlyDictionary<int, IReadOnlyList<SeekingItemRequirement>> requirements)
        : IQuestDefinitionSource
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([]);

        public ValueTask<QuestDefinition?> GetAsync(int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(requirements.TryGetValue(questId, out var values)
                ? new QuestDefinition(questId, "", null, null, null, null, null, null, [], "", "",
                    null, null, null, null, null, null, null, [], [], [], [], SeekingItems: values)
                : null);

        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> GetTrainingAvailabilityAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
    }

    private sealed class RefillConfiguration(IEntryLifecycleContentSource source) : IDailyRefillConfigurationDomainService
    {
        public ValueTask<IReadOnlyList<DailyRefillRule>> EffectiveAsync(DateTimeOffset at, CancellationToken ct) => source.GetDailyRefillRulesAsync(at, ct);
        public ValueTask<DailyRefillList> ListAsync(DateTimeOffset at, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<DailyRefillList> ChangeAsync(DailyRefillChange change, string actor, DateTimeOffset at, CancellationToken ct) => throw new NotSupportedException();
    }

}
