using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class CharacterEntryInventoryTests
{
    [Fact]
    public async Task InventoryEntry_LoadsExplicitOrderedCharacterContainersAndSeparateAccountCargo()
    {
        var accountId = new AccountId(41);
        var characterId = new CharacterId(51);
        var equipment = StorageItem(
            1,
            InventoryOwnerType.Character,
            characterId.Value,
            characterId,
            InventoryContainerType.Equipment,
            8,
            itemId: 108);
        var mainLater = StorageItem(
            2,
            InventoryOwnerType.Character,
            characterId.Value,
            characterId,
            InventoryContainerType.Main,
            9,
            itemId: 109);
        var mainEarlier = StorageItem(
            3,
            InventoryOwnerType.Character,
            characterId.Value,
            characterId,
            InventoryContainerType.Main,
            4,
            itemId: 104);
        var runtime = StorageItem(
            4,
            InventoryOwnerType.Character,
            characterId.Value,
            characterId,
            InventoryContainerType.Main,
            1,
            itemId: 1,
            value: 77);
        var cargo = StorageItem(
            5,
            InventoryOwnerType.Account,
            accountId.Value,
            characterId,
            InventoryContainerType.AccountCargo,
            3,
            itemId: 203);
        var repository = new StubRepository(new InventoryEntryStorageRecord(
            accountId,
            characterId,
            [equipment, mainLater, runtime, mainEarlier],
            [cargo],
            [new InventoryContainerParameter(characterId, InventoryContainerType.PersonalCargo, 48)],
            new AccountCargoStorageState(accountId, 16, 1234, 1),
            [new InventoryRuntimeValueState(1, 1, 77)],
            [new InventoryRuntimeValueState(356, 3035, 99)],
            [
                new InventoryItemValueStorageRecord(
                    characterId, InventoryItemValueListType.Effect, 2, 9002, 92),
                new InventoryItemValueStorageRecord(
                    characterId, InventoryItemValueListType.Cooldown, 3, 8003, 83),
                new InventoryItemValueStorageRecord(
                    characterId, InventoryItemValueListType.Cooldown, 1, 8001, 81),
            ],
            [
                new InventoryEquipmentLockStorageRecord(
                    characterId, 10, InventoryContainerType.AccountCargo, 3, 2, 600),
                new InventoryEquipmentLockStorageRecord(
                    characterId, 9, InventoryContainerType.Equipment, 8, 1, 0),
            ],
            [new CharacterRentalStorageRecord(characterId, 9001, 1001, 1900000000)],
            new InventoryGoldLimitState(400_000_000, 500_000_000)));

        var result = await new InventoryEntryService(repository).LoadAsync(
            accountId,
            characterId,
            CancellationToken.None);

        Assert.Equal(
            [
                InventoryContainerType.Main,
                InventoryContainerType.Avatar,
                InventoryContainerType.PersonalCargo,
                InventoryContainerType.Equipment,
                InventoryContainerType.Pet,
            ],
            result.CharacterInventory.Containers.Select(container => container.ContainerType));
        Assert.Equal([1, 4, 9], result.CharacterInventory.Containers[0].Items.Select(item => item.SlotIndex));
        Assert.Empty(result.CharacterInventory.Containers[1].Items);
        Assert.Equal((ushort)48, result.CharacterInventory.Containers[2].Parameter);
        Assert.Equal([8], result.CharacterInventory.Containers[3].Items.Select(item => item.SlotIndex));
        Assert.Empty(result.CharacterInventory.Containers[4].Items);
        var runtimeValue = Assert.Single(result.CharacterInventory.VirtualResources);
        Assert.Equal(new InventoryRuntimeValueState(1, 1, 77), runtimeValue);
        Assert.Equal([1, 3], result.CharacterInventory.ItemValues.Cooldowns.Select(item => item.SortOrder));
        Assert.Equal([2], result.CharacterInventory.ItemValues.Effects.Select(item => item.SortOrder));
        Assert.Equal(
            [new InventoryEquipmentLockState(9, InventoryContainerType.Equipment, 8, 1, 0)],
            result.CharacterInventory.EquipmentLocks);
        Assert.Equal(
            new CharacterRentalItemState(9001, 1001, 1900000000),
            Assert.Single(result.CharacterInventory.Rentals.Items));
        Assert.Equal(new InventoryGoldLimitState(400_000_000, 500_000_000), result.CharacterInventory.GoldLimits);

        Assert.Equal(accountId, result.AccountInventory.AccountId);
        Assert.Equal(InventoryContainerType.AccountCargo, result.AccountInventory.Cargo.Container.ContainerType);
        Assert.Equal([3], result.AccountInventory.Cargo.Container.Items.Select(item => item.SlotIndex));
        Assert.Equal(
            [new InventoryEquipmentLockState(10, InventoryContainerType.AccountCargo, 3, 2, 600)],
            result.AccountInventory.Cargo.EquipmentLocks);
        Assert.Equal((ushort)16, result.AccountInventory.Cargo.SelectionKey);
        Assert.Equal(1234, result.AccountInventory.Cargo.Value32);
        Assert.Equal((ushort)1, result.AccountInventory.Cargo.StoredItemCount);
        Assert.Equal(
            new InventoryRuntimeValueState(356, 3035, 99),
            Assert.Single(result.AccountInventory.VirtualResources));
    }

    [Fact]
    public async Task CharacterEntry_InventoryFailurePreservesPreviouslyPublishedCharacter()
    {
        var accountId = new AccountId(42);
        var previous = Character(501, 1);
        var selected = Character(502, 2);
        var active = new StatefulActiveCharacterService(accountId, previous);
        var entry = new CharacterEntryService(
            new StubCharacterService(selected),
            new FailingInventoryEntryService(),
            active,
            TimeProvider.System);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await entry.EnterAsync(accountId, 2, CancellationToken.None));

        Assert.Same(previous, active.GetRequired(accountId));
    }

    [Fact]
    public async Task CharacterEntry_ComposesInventoryOutsideActiveCharacterState()
    {
        var accountId = new AccountId(43);
        var selected = Character(503, 3);
        var inventory = EmptyInventory(accountId, selected.Identity.CharacterId);
        var active = new StatefulActiveCharacterService();
        var entry = new CharacterEntryService(
            new StubCharacterService(selected),
            new StubInventoryEntryService(inventory),
            active,
            TimeProvider.System);

        var result = await entry.EnterAsync(accountId, 3, CancellationToken.None);

        Assert.Same(selected, result.Character);
        Assert.Same(inventory.CharacterInventory, result.CharacterInventory);
        Assert.Same(inventory.AccountInventory, result.AccountInventory);
        Assert.Same(selected, active.GetRequired(accountId));
    }

    private static InventoryStorageItem StorageItem(
        long storageEntryId,
        InventoryOwnerType ownerType,
        int ownerId,
        CharacterId? characterId,
        InventoryContainerType containerType,
        int slot,
        int itemId,
        int value = 1) =>
        new(
            storageEntryId,
            ownerType,
            ownerId,
            characterId,
            containerType,
            slot,
            Core(itemId, value));

    private static InventoryItemCoreState Core(int itemId, int value) => new(
        1,
        itemId,
        value,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        -1,
        new ChronicleOptionState(0, 0, 0, 0, 0),
        new ChronicleOptionState(0, 0, 0, 0, 0),
        0,
        0,
        0,
        0,
        0,
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        0,
        0xFF,
        0,
        new RandomOptionState(0, 0, 0),
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);

    private static ActiveCharacterState Character(int id, ushort slot) => new(
        new CharacterIdentityState(new CharacterId(id), slot, [0x41], 1, 0),
        new CharacterProgressionState(10, 100),
        new CharacterFatigueState(156, 0),
        new CharacterPositionState(1, 1, 10, 20, 0),
        DateTimeOffset.UnixEpoch,
        null);

    private static InventoryEntryState EmptyInventory(AccountId accountId, CharacterId characterId) => new(
        new CharacterInventoryEntryState(
            characterId,
            [],
            [],
            new CharacterItemValueEntryState([], []),
            [],
            new CharacterRentalEntryState([]),
            new InventoryGoldLimitState(0, 0)),
        new AccountInventoryEntryState(
            accountId,
            new AccountCargoEntryState(
                accountId,
                new InventoryContainerState(InventoryContainerType.AccountCargo, 0, []),
                [],
                0,
                0,
                0),
            []));

    private sealed class StubRepository(InventoryEntryStorageRecord record) : IInventoryEntryRepository
    {
        public ValueTask<InventoryEntryStorageRecord> LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) => ValueTask.FromResult(record);
    }

    private sealed class StubCharacterService(ActiveCharacterState state) : ICharacterService
    {
        public ValueTask<AccountCharacterRoster> GetAccountRosterAsync(
            AccountId accountId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<ActiveCharacterState> LoadForSelectionAsync(
            AccountId accountId,
            ushort slotIndex,
            CancellationToken cancellationToken) => ValueTask.FromResult(state);
    }

    private sealed class StubInventoryEntryService(InventoryEntryState state) : IInventoryEntryService
    {
        public ValueTask<InventoryEntryState> LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) => ValueTask.FromResult(state);
    }

    private sealed class FailingInventoryEntryService : IInventoryEntryService
    {
        public ValueTask<InventoryEntryState> LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<InventoryEntryState>(new InvalidDataException("malformed inventory"));
    }

    private sealed class StatefulActiveCharacterService : IActiveCharacterService
    {
        private readonly Dictionary<AccountId, ActiveCharacterState> states = [];

        public StatefulActiveCharacterService()
        {
        }

        public StatefulActiveCharacterService(AccountId accountId, ActiveCharacterState state) =>
            states.Add(accountId, state);

        public void Publish(AccountId accountId, ActiveCharacterState character) =>
            states[accountId] = character;

        public ActiveCharacterState GetRequired(AccountId accountId) => states[accountId];
    }
}
