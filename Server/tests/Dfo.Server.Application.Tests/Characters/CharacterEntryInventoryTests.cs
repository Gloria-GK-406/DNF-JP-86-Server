using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class CharacterEntryInventoryTests
{
    internal static async ValueTask<SelectCharacterResult> CreateLifecycleResultAsync(
        AccountId accountId, int characterId, ushort slotIndex)
    {
        var selected = Character(characterId, slotIndex);
        return await EntryServices.Create(
                new StubCharacterService(selected),
                new StubInventoryEntryService(EmptyInventory(accountId, selected.Identity.CharacterId)),
                new StubCharacterEntryStateService(EmptyCharacterEntry(selected.Identity.CharacterId)),
                new StubAccountEntryStateService(EmptyAccountEntry(accountId)),
                new StatefulActiveCharacterService())
            .EnterAsync(accountId, slotIndex, StubChannelEntryPolicy.GeneralChannel, CancellationToken.None);
    }

    [Fact]
    public void EquippedCreatureQuery_UsesTheInventoryOwnedCreatureSlotAndItemKind()
    {
        var characterId = new CharacterId(50);
        var inventory = new CharacterInventoryEntryState(
            characterId,
            [
                new InventoryContainerState(
                    InventoryContainerType.Equipment,
                    0,
                    [new InventoryItemState(1, 24, Core(9001, 1, itemKind: 5))]),
            ],
            [],
            new CharacterItemValueEntryState([], []),
            [],
            new CharacterRentalEntryState([]),
            new InventoryGoldLimitState(0, 0));

        var itemId = new EquippedCreatureQuery().GetItemId(inventory);

        Assert.Equal(9001, itemId);
    }

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
        var repository = new StubRepository(
            new CharacterInventoryEntryStorageRecord(
                accountId,
                characterId,
                [equipment, mainLater, runtime, mainEarlier],
                [new InventoryContainerParameter(characterId, InventoryContainerType.PersonalCargo, 48)],
                [new InventoryRuntimeValueState(1, 1, 77)],
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
                        characterId, 9, InventoryContainerType.Equipment, 8, 1, 0),
                ],
                [new CharacterRentalStorageRecord(characterId, 9001, 1001, 1900000000)],
                new InventoryGoldLimitState(400_000_000, 500_000_000)),
            new AccountInventoryEntryStorageRecord(
                accountId,
                characterId,
                [cargo],
                new AccountCargoStorageState(accountId, 16, 1234, 1),
                [new InventoryRuntimeValueState(356, 3035, 99)],
                [
                    new InventoryEquipmentLockStorageRecord(
                        characterId, 10, InventoryContainerType.AccountCargo, 3, 2, 600),
                ]));

        var characterResult = await new CharacterInventoryEntryService(repository).LoadAsync(
            accountId,
            characterId,
            CancellationToken.None);
        var accountResult = await new AccountInventoryEntryService(repository).LoadAsync(
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
            characterResult.Containers.Select(container => container.ContainerType));
        Assert.Equal([1, 4, 9], characterResult.Containers[0].Items.Select(item => item.SlotIndex));
        Assert.Empty(characterResult.Containers[1].Items);
        Assert.Equal((ushort)48, characterResult.Containers[2].Parameter);
        Assert.Equal([8], characterResult.Containers[3].Items.Select(item => item.SlotIndex));
        Assert.Empty(characterResult.Containers[4].Items);
        var runtimeValue = Assert.Single(characterResult.VirtualResources);
        Assert.Equal(new InventoryRuntimeValueState(1, 1, 77), runtimeValue);
        Assert.Equal([1, 3], characterResult.ItemValues.Cooldowns.Select(item => item.SortOrder));
        Assert.Equal([2], characterResult.ItemValues.Effects.Select(item => item.SortOrder));
        Assert.Equal(
            [new InventoryEquipmentLockState(9, InventoryContainerType.Equipment, 8, 1, 0)],
            characterResult.EquipmentLocks);
        Assert.Equal(
            new CharacterRentalItemState(9001, 1001, 1900000000),
            Assert.Single(characterResult.Rentals.Items));
        Assert.Equal(new InventoryGoldLimitState(400_000_000, 500_000_000), characterResult.GoldLimits);

        Assert.Equal(accountId, accountResult.AccountId);
        Assert.Equal(InventoryContainerType.AccountCargo, accountResult.Cargo.Container.ContainerType);
        Assert.Equal([3], accountResult.Cargo.Container.Items.Select(item => item.SlotIndex));
        Assert.Equal(
            [new InventoryEquipmentLockState(10, InventoryContainerType.AccountCargo, 3, 2, 600)],
            accountResult.Cargo.EquipmentLocks);
        Assert.Equal((ushort)16, accountResult.Cargo.SelectionKey);
        Assert.Equal(1234, accountResult.Cargo.Value32);
        Assert.Equal((ushort)1, accountResult.Cargo.StoredItemCount);
        Assert.Equal(
            new InventoryRuntimeValueState(356, 3035, 99),
            Assert.Single(accountResult.VirtualResources));
    }

    [Fact]
    public async Task CharacterEntry_InventoryFailurePreservesPreviouslyPublishedCharacter()
    {
        var accountId = new AccountId(42);
        var previous = Character(501, 1);
        var selected = Character(502, 2);
        var active = new StatefulActiveCharacterService(accountId, previous);
        var entry = EntryServices.Create(
            new StubCharacterService(selected),
            new FailingInventoryEntryService(),
            new StubCharacterEntryStateService(EmptyCharacterEntry(selected.Identity.CharacterId)),
            new StubAccountEntryStateService(EmptyAccountEntry(accountId)),
            active);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await entry.EnterAsync(accountId, 2, StubChannelEntryPolicy.GeneralChannel, CancellationToken.None));

        Assert.Same(previous, active.GetRequired(accountId));
    }

    /// <remarks>
    /// A refusal must leave no trace. Publication is the observable that matters, because a
    /// published character is what every later stage reads.
    /// </remarks>
    [Fact]
    public async Task CharacterEntry_RefusedByTheChannelPublishesNothing()
    {
        var accountId = new AccountId(44);
        var previous = Character(601, 1);
        var selected = Character(602, 2);
        var active = new StatefulActiveCharacterService(accountId, previous);
        var refusal = new ChannelAdmissionRefusal(0xFD, "level too low");
        var entry = EntryServices.Create(
            new StubCharacterService(selected),
            new NotReachedInventoryEntryService(),
            new StubCharacterEntryStateService(EmptyCharacterEntry(selected.Identity.CharacterId)),
            new StubAccountEntryStateService(EmptyAccountEntry(accountId)),
            active,
            channels: new StubChannelEntryPolicy(refusal));

        var error = await Assert.ThrowsAsync<ChannelAdmissionRefusedException>(async () =>
            await entry.EnterAsync(
                accountId,
                2,
                StubChannelEntryPolicy.RestrictedChannel,
                CancellationToken.None));

        Assert.Equal(refusal, error.Refusal);
        Assert.Equal(StubChannelEntryPolicy.RestrictedChannel, error.Channel);
        Assert.Same(previous, active.GetRequired(accountId));
        Assert.Equal(0, active.PublishCount);
    }

    [Fact]
    public async Task CharacterEntry_FreeDuelUsesIndependentSkillsWhileOrdinaryKeepsNormalProfile()
    {
        var accountId = new AccountId(440);
        var selected = Character(6020, 2);
        var normal = EmptyCharacterEntry(selected.Identity.CharacterId) with
        {
            Skills = new CharacterSkillsEntryState(
                [new CharacterSkillPageState(0, [new CharacterSkillState(3, 777, 2, [])])],
                [new CharacterSkillPointSlotState(4, 55)],
                new CharacterSkillPointBasisState(6, 7),
                [new DarkKnightComboEntryState(0, [1, 2, 3])], [11, 12]),
        };
        var pvp = normal.Skills with
        {
            Pages = [new CharacterSkillPageState(0, [new CharacterSkillState(9, 888, 4, [])]),
                new CharacterSkillPageState(1, [])],
            ProtocolMirror = new CharacterSkillProtocolMirrorState(
                ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue),
        };
        var entry = EntryServices.Create(
            new StubCharacterService(selected),
            new StubInventoryEntryService(EmptyInventory(accountId, selected.Identity.CharacterId)),
            new StubCharacterEntryStateService(normal), new StubAccountEntryStateService(EmptyAccountEntry(accountId)),
            new StatefulActiveCharacterService(), skillPoints: new NormalSkillSource(),
            pvpSkills: new FixedPvpSkills(pvp), placement: new DuelAwarePlacement());

        var ordinary = await entry.EnterAsync(
            accountId, 2, StubChannelEntryPolicy.GeneralChannel, CancellationToken.None);
        var duel = await entry.EnterAsync(
            accountId, 2, StubChannelEntryPolicy.FreeDuelChannel, CancellationToken.None);

        Assert.Equal((ushort)777, Assert.Single(ordinary.CharacterEntry.Skills.Pages[0].Skills).SkillId);
        Assert.NotNull(ordinary.CharacterEntry.Skills.ProtocolMirror);
        Assert.Equal((ushort)888, Assert.Single(duel.CharacterEntry.Skills.Pages[0].Skills).SkillId);
        Assert.Equal(ushort.MaxValue, duel.CharacterEntry.Skills.ProtocolMirror!.Page0Sp);
        Assert.Equal(normal.Skills.SkillPointSlots, duel.CharacterEntry.Skills.SkillPointSlots);
        Assert.Equal(normal.Skills.SkillPointBasis, duel.CharacterEntry.Skills.SkillPointBasis);
        Assert.Equal(normal.Skills.DarkKnightCombos, duel.CharacterEntry.Skills.DarkKnightCombos);
        Assert.Equal(normal.Skills.KnightShieldDeck, duel.CharacterEntry.Skills.KnightShieldDeck);
    }

    [Fact]
    public async Task CharacterEntry_OrdinarySelectionReconcilesExpiredExpertSkillsAndReloadsTypedTruth()
    {
        var accountId = new AccountId(441);
        var selected = Character(6022, 2);
        var before = EmptyCharacterEntry(selected.Identity.CharacterId) with
        {
            Skills = new CharacterSkillsEntryState(
                [new CharacterSkillPageState(0, [new CharacterSkillState(3, 777, 4, [])])], [],
                new CharacterSkillPointBasisState(0, 0), null, null),
        };
        var after = before with
        {
            Skills = before.Skills with
            {
                Pages = [new CharacterSkillPageState(0, [new CharacterSkillState(3, 777, 2, [])])]
            }
        };
        var reconciliation = new ChangedExpertReconciliation();
        var entry = EntryServices.Create(
            new StubCharacterService(selected),
            new StubInventoryEntryService(EmptyInventory(accountId, selected.Identity.CharacterId)),
            new ReloadingCharacterEntryStateService(before, after),
            new StubAccountEntryStateService(EmptyAccountEntry(accountId)),
            new StatefulActiveCharacterService(), skillPoints: new NormalSkillSource(),
            expertContracts: new FixedExpertContracts(), expertSkillReconciliation: reconciliation);

        var result = await entry.EnterAsync(
            accountId, 2, StubChannelEntryPolicy.GeneralChannel, CancellationToken.None);

        Assert.Equal(1, reconciliation.Calls);
        Assert.Equal((byte)2, Assert.Single(result.CharacterEntry.Skills.Pages[0].Skills).Level);
    }

    [Fact]
    public async Task PvpInitializer_PreservesInitialOrderAndUsesDarkKnightGroupAllocation()
    {
        var darkKnight = Character(6021, 3) with
        {
            Identity = Character(6021, 3).Identity with { Job = 9 },
        };
        var result = await Dfo.Server.Application.Commands.Game.SelectCharacterPreparationService
            .BuildInitialPvpSkillProfileAsync(
                darkKnight, EmptyCharacterEntry(darkKnight.Identity.CharacterId).Skills,
                new AllocationSkillSource(), CancellationToken.None);

        Assert.Equal([(short)0, (short)102, (short)150],
            result.Pages[0].Skills.Select(value => value.Slot));
        Assert.Equal([(ushort)100, (ushort)101, (ushort)102],
            result.Pages[0].Skills.Select(value => value.SkillId));
        Assert.Equal(result.Pages[0].Skills, result.Pages[1].Skills);
    }

    [Fact]
    public async Task CharacterEntry_AdmittedByTheChannelPublishesTheSelectedCharacter()
    {
        var accountId = new AccountId(45);
        var selected = Character(603, 3);
        var active = new StatefulActiveCharacterService(accountId, Character(604, 1));
        var entry = EntryServices.Create(
            new StubCharacterService(selected),
            new StubInventoryEntryService(EmptyInventory(accountId, selected.Identity.CharacterId)),
            new StubCharacterEntryStateService(EmptyCharacterEntry(selected.Identity.CharacterId)),
            new StubAccountEntryStateService(EmptyAccountEntry(accountId)),
            active);

        var result = await entry.EnterAsync(
            accountId,
            3,
            StubChannelEntryPolicy.RestrictedChannel,
            CancellationToken.None);

        Assert.Same(selected, result.Character);
        Assert.Contains(result.Roster!.Characters, value => value.CharacterId == selected.Identity.CharacterId);
        Assert.Equal(new CharacterId(604), active.GetRequired(accountId).Identity.CharacterId);
        Assert.Equal(0, active.PublishCount);
    }

    [Fact]
    public async Task CharacterEntry_FinalResultFailurePreservesPreviouslyPublishedCharacter()
    {
        var accountId = new AccountId(47);
        var previous = Character(605, 1);
        var selected = Character(606, 3);
        var active = new StatefulActiveCharacterService(accountId, previous);
        var entry = EntryServices.Create(
            new StubCharacterService(selected),
            new StubInventoryEntryService(EmptyInventory(accountId, selected.Identity.CharacterId)),
            new StubCharacterEntryStateService(EmptyCharacterEntry(selected.Identity.CharacterId)),
            new StubAccountEntryStateService(EmptyAccountEntry(accountId)),
            active,
            timeProvider: new FailingTimeProvider());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await entry.EnterAsync(
                accountId,
                3,
                StubChannelEntryPolicy.GeneralChannel,
                CancellationToken.None));

        Assert.Same(previous, active.GetRequired(accountId));
        Assert.Equal(0, active.PublishCount);
    }

    /// <remarks>
    /// One per required source class added by this work. A source that failed after
    /// publication would leave a character published with an incomplete entry, and every
    /// later stage would read that entry as complete.
    /// </remarks>
    public static TheoryData<string> FailingSources => ["content", "placement", "session"];

    [Theory]
    [MemberData(nameof(FailingSources))]
    public async Task CharacterEntry_FailsBeforePublicationWhenARequiredSourceIsUnavailable(
        string failingSource)
    {
        var accountId = new AccountId(46);
        var previous = Character(701, 1);
        var selected = Character(702, 2);
        var active = new StatefulActiveCharacterService(accountId, previous);
        var entry = EntryServices.Create(
            new StubCharacterService(selected),
            new StubInventoryEntryService(EmptyInventory(accountId, selected.Identity.CharacterId)),
            new StubCharacterEntryStateService(EmptyCharacterEntry(selected.Identity.CharacterId)),
            new StubAccountEntryStateService(EmptyAccountEntry(accountId)),
            active,
            worldContent: failingSource is "content" ? new FailingWorldContentEntryService() : null,
            placement: failingSource is "placement" ? new FailingEntryPlacementService() : null,
            sessionAdjacent: failingSource is "session" ? new FailingSessionAdjacentEntryService() : null);

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await entry.EnterAsync(
                accountId,
                2,
                StubChannelEntryPolicy.GeneralChannel,
                CancellationToken.None));

        Assert.Same(previous, active.GetRequired(accountId));
        Assert.Equal(0, active.PublishCount);
    }

    [Fact]
    public async Task CharacterEntry_ComposesInventoryOutsideActiveCharacterState()
    {
        var accountId = new AccountId(43);
        var selected = Character(503, 3);
        var inventory = EmptyInventory(accountId, selected.Identity.CharacterId);
        var active = new StatefulActiveCharacterService();
        var entry = EntryServices.Create(
            new StubCharacterService(selected),
            new StubInventoryEntryService(inventory),
            new StubCharacterEntryStateService(EmptyCharacterEntry(selected.Identity.CharacterId)),
            new StubAccountEntryStateService(EmptyAccountEntry(accountId)),
            active);

        var result = await entry.EnterAsync(accountId, 3, StubChannelEntryPolicy.GeneralChannel, CancellationToken.None);

        Assert.Same(selected, result.Character);
        Assert.Equal(selected.Identity.CharacterId, result.CharacterEntry.CharacterId);
        Assert.Same(inventory.CharacterInventory, result.CharacterInventory);
        Assert.Same(inventory.AccountInventory, result.AccountInventory);
        Assert.Equal(accountId, result.Account.AccountId);
        Assert.Equal(0, active.PublishCount);
    }

    [Fact]
    public async Task CharacterEntry_CompletesSkillMirrorFromTypedLedgerDefinitions()
    {
        var accountId = new AccountId(48);
        var selected = Character(508, 3) with
        {
            Identity = Character(508, 3).Identity with { GrowType = 0x11 },
        };
        var characterEntry = EmptyCharacterEntry(selected.Identity.CharacterId) with
        {
            Skills = new CharacterSkillsEntryState(
                [new CharacterSkillPageState(0, [new CharacterSkillState(1, 100, 3, [])])],
                [], new CharacterSkillPointBasisState(5, 0), null, null),
        };
        var entry = EntryServices.Create(
            new StubCharacterService(selected),
            new StubInventoryEntryService(EmptyInventory(accountId, selected.Identity.CharacterId)),
            new StubCharacterEntryStateService(characterEntry),
            new StubAccountEntryStateService(EmptyAccountEntry(accountId)),
            new StatefulActiveCharacterService(),
            skillPoints: new PopulatedSkillPointDefinitionSource());

        var result = await entry.EnterAsync(
            accountId, 3, StubChannelEntryPolicy.GeneralChannel, CancellationToken.None);

        Assert.Equal(new CharacterSkillProtocolMirrorState(85, 105, 0, 0),
            result.CharacterEntry.Skills.ProtocolMirror);
    }

    [Fact]
    public async Task CharacterEntry_CharacterStateFailurePreservesPreviouslyPublishedCharacter()
    {
        var accountId = new AccountId(44);
        var previous = Character(504, 4);
        var selected = Character(505, 5);
        var active = new StatefulActiveCharacterService(accountId, previous);
        var entry = EntryServices.Create(
            new StubCharacterService(selected),
            new StubInventoryEntryService(EmptyInventory(accountId, selected.Identity.CharacterId)),
            new FailingCharacterEntryStateService(),
            new StubAccountEntryStateService(EmptyAccountEntry(accountId)),
            active);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await entry.EnterAsync(accountId, 5, StubChannelEntryPolicy.GeneralChannel, CancellationToken.None));

        Assert.Same(previous, active.GetRequired(accountId));
    }

    [Fact]
    public async Task CharacterEntry_AccountStateFailurePreservesPreviouslyPublishedCharacter()
    {
        var accountId = new AccountId(45);
        var previous = Character(506, 5);
        var selected = Character(507, 6);
        var active = new StatefulActiveCharacterService(accountId, previous);
        var entry = EntryServices.Create(
            new StubCharacterService(selected),
            new StubInventoryEntryService(EmptyInventory(accountId, selected.Identity.CharacterId)),
            new StubCharacterEntryStateService(EmptyCharacterEntry(selected.Identity.CharacterId)),
            new FailingAccountEntryStateService(),
            active);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await entry.EnterAsync(accountId, 6, StubChannelEntryPolicy.GeneralChannel, CancellationToken.None));

        Assert.Same(previous, active.GetRequired(accountId));
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

    private static InventoryItemCoreState Core(int itemId, int value, byte itemKind = 1) => new(
        itemKind,
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

    private static TestInventoryEntryState EmptyInventory(AccountId accountId, CharacterId characterId) => new(
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

    private static CharacterEntryState EmptyCharacterEntry(CharacterId characterId) => new(
        characterId,
        new CharacterPublicEntryState(
            new CharacterSubtype0EntryState(
                0, 0, false, false, 0, 0, false, false, 0, 0, 0, 0, 0,
                CreatureBuffer: []),
            new CharacterSubtype1EntryState(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, -1, 0, 0,
                Dimensions: [], DimensionFlags: new CharacterDimensionFlagsState(0, 0, 0, 0)),
            new CharacterAppearanceCompatibilityState(1, []), 0, 0),
        new CharacterSettingsEntryState([], [], 0),
        new CharacterSkillsEntryState(
            [], [], new CharacterSkillPointBasisState(0, 0), null, null),
        new CharacterQuestEntryState([], [], [], new byte[30_000]),
        new CharacterProgressionEntryState(
            [], 0, 255, 0, 0, 0, 0, 0, 0, false, 1, [], new byte[6], [], [], [], []),
        new CharacterFeaturesEntryState(
            [],
            new CharacterCollectionsEntryState([], [], []),
            new CharacterExpertJobEntryState(0, 0, 0, 0, 0, 0, []),
            new CharacterMailboxEntryState(0, []),
            []));

    private static AccountEntryState EmptyAccountEntry(AccountId accountId) => new(
        accountId,
        new AccountSettingsEntryState([], [], [], 0, null),
        new AccountWalletEntryState(0, 0, 0, 0),
        new AccountEntitlementEntryState([]),
        new AccountProgressionEntryState(0, 0, new AccountAdventureEntryState([]), []),
        new AccountRelationCompatibilityEntryState([], [], [], 0));

    private sealed class StubRepository(
        CharacterInventoryEntryStorageRecord characterRecord,
        AccountInventoryEntryStorageRecord accountRecord)
        : ICharacterInventoryEntryRepository, IAccountInventoryEntryRepository
    {
        public ValueTask<CharacterInventoryEntryStorageRecord> LoadCharacterAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) => ValueTask.FromResult(characterRecord);

        public ValueTask<AccountInventoryEntryStorageRecord> LoadAccountAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) => ValueTask.FromResult(accountRecord);
    }

    private sealed class StubCharacterService(ActiveCharacterState state) : ICharacterService
    {
        public ValueTask<AccountCharacterRoster> GetAccountRosterAsync(
            AccountId accountId,
            CancellationToken cancellationToken) => ValueTask.FromResult(new AccountCharacterRoster(
                accountId,
                24,
                [new CharacterSummary(
                    state.Identity.CharacterId,
                    state.Identity.SlotIndex,
                    state.Identity.Name,
                    state.Identity.Job,
                    state.Identity.GrowType,
                    state.Progression.Level,
                    0)]));

        public ValueTask<ActiveCharacterState> LoadForSelectionAsync(
            AccountId accountId,
            ushort slotIndex,
            CancellationToken cancellationToken) => ValueTask.FromResult(state);
    }

    private sealed class StubInventoryEntryService(TestInventoryEntryState state)
        : ITestInventoryEntryServices
    {
        ValueTask<CharacterInventoryEntryState> ICharacterInventoryEntryService.LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) => ValueTask.FromResult(state.CharacterInventory);

        ValueTask<AccountInventoryEntryState> IAccountInventoryEntryService.LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) => ValueTask.FromResult(state.AccountInventory);
    }

    private sealed class FailingInventoryEntryService : ITestInventoryEntryServices
    {
        ValueTask<CharacterInventoryEntryState> ICharacterInventoryEntryService.LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<CharacterInventoryEntryState>(
                new InvalidDataException("malformed inventory"));

        ValueTask<AccountInventoryEntryState> IAccountInventoryEntryService.LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Account inventory must not load after character inventory failure.");
    }

    private sealed class NotReachedInventoryEntryService : ITestInventoryEntryServices
    {
        ValueTask<CharacterInventoryEntryState> ICharacterInventoryEntryService.LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inventory must not load after channel refusal.");

        ValueTask<AccountInventoryEntryState> IAccountInventoryEntryService.LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Inventory must not load after channel refusal.");
    }

    private sealed record TestInventoryEntryState(
        CharacterInventoryEntryState CharacterInventory,
        AccountInventoryEntryState AccountInventory);

    private sealed class FailingTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            throw new InvalidOperationException("Clock is unavailable.");
    }

    private sealed class NormalSkillSource : ISkillPointDefinitionSource
    {
        public ValueTask<SkillPointDefinitionSet> GetAsync(byte job, byte packedGrowType,
            byte characterLevel, IReadOnlyCollection<int> learnedSkillIds,
            CancellationToken cancellationToken) => ValueTask.FromResult(new SkillPointDefinitionSet(
                100, 10, [], learnedSkillIds.ToDictionary(id => id,
                    id => new SkillPointCostDefinition(id, false, [10], false, 1, 100, 0, 1, 1))));
    }

    private sealed class AllocationSkillSource : ISkillPointDefinitionSource
    {
        public ValueTask<SkillPointDefinitionSet> GetAsync(byte job, byte packedGrowType,
            byte characterLevel, IReadOnlyCollection<int> learnedSkillIds,
            CancellationToken cancellationToken) => ValueTask.FromResult(new SkillPointDefinitionSet(
                0, 0,
                [new SkillPointGrantDefinition(100, 1, true),
                    new SkillPointGrantDefinition(101, 1, true),
                    new SkillPointGrantDefinition(102, 1, false)],
                new Dictionary<int, SkillPointCostDefinition>
                {
                    [100] = new(100, false, [], false, 0, 1, 0, 1, 1, IsActive: true),
                    [101] = new(101, false, [], false, 0, 1, 0, 1, 1, IsActive: false),
                    [102] = new(102, false, [], false, 0, 1, 0, 1, 1,
                        IsActive: true, RawGroup: 3, NumberOfGrowTypes: 4),
                }));
    }

    private sealed class FixedPvpSkills(CharacterSkillsEntryState state)
        : ICharacterPvpSkillProfileRepository
    {
        public ValueTask<CharacterSkillsEntryState> LoadOrInitializeAsync(
            AccountId accountId, CharacterId characterId, CharacterSkillsEntryState initialProfile,
            CancellationToken cancellationToken) => ValueTask.FromResult(state);
    }

    private sealed class FixedExpertContracts : IExpertSkillContractDefinitionSource
    {
        public IReadOnlySet<int> GetOverSkillPremiumTypes() => new HashSet<int> { 580 };
    }

    private sealed class ChangedExpertReconciliation : IExpertSkillContractReconciliationRepository
    {
        public int Calls { get; private set; }
        public ValueTask<bool> ReconcileAsync(
            AccountId accountId, CharacterId characterId, IReadOnlySet<int> premiumTypes,
            IReadOnlyDictionary<int, SkillPointCostDefinition> definitions,
            IReadOnlyDictionary<int, byte> freeBaseline, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Contains(580, premiumTypes);
            Assert.Contains(777, definitions.Keys);
            return ValueTask.FromResult(true);
        }
    }

    private sealed class DuelAwarePlacement : Dfo.Server.Abstractions.World.IEntryPlacementService
    {
        public ValueTask<ChannelEntryPolicyState> ResolveAsync(ChannelEntryIdentity channel,
            int persistedTownId, CancellationToken cancellationToken) => ValueTask.FromResult(
                new ChannelEntryPolicyState(channel.ChannelId,
                    new EntrySpawnState(1, 1, 10, 20, 5, 3, channel.IsTransient),
                    !channel.IsTransient, channel.IsFreeDuel));
        public ValueTask<bool> CanEnterTownAsync(ChannelEntryIdentity channel, int townId,
            CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }

    private sealed class StubCharacterEntryStateService(CharacterEntryState state)
        : ICharacterEntryStateService
    {
        public ValueTask<CharacterEntryState> LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) => ValueTask.FromResult(state);
    }

    private sealed class ReloadingCharacterEntryStateService(
        CharacterEntryState before, CharacterEntryState after) : ICharacterEntryStateService
    {
        private int calls;
        public ValueTask<CharacterEntryState> LoadAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Interlocked.Increment(ref calls) == 1 ? before : after);
    }

    private sealed class PopulatedSkillPointDefinitionSource : ISkillPointDefinitionSource
    {
        public ValueTask<SkillPointDefinitionSet> GetAsync(
            byte job, byte packedGrowType, byte characterLevel,
            IReadOnlyCollection<int> learnedSkillIds, CancellationToken cancellationToken)
        {
            Assert.Equal((byte)0x11, packedGrowType);
            Assert.Equal([100], learnedSkillIds);
            return ValueTask.FromResult(new SkillPointDefinitionSet(
                100, 0,
                [new SkillPointGrantDefinition(100, 1)],
                new Dictionary<int, SkillPointCostDefinition>
                {
                    [100] = new(100, false, [10], false, 1, 10, 0, 1, 1),
                }));
        }
    }

    private sealed class FailingCharacterEntryStateService : ICharacterEntryStateService
    {
        public ValueTask<CharacterEntryState> LoadAsync(
            AccountId accountId,
            CharacterId characterId,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<CharacterEntryState>(
                new InvalidDataException("character entry state is unavailable"));
    }

    private sealed class StubAccountEntryStateService(AccountEntryState state)
        : IAccountEntryStateService
    {
        public ValueTask<AccountEntryState> LoadAsync(
            AccountId accountId,
            CancellationToken cancellationToken) => ValueTask.FromResult(state);
    }

    private sealed class FailingAccountEntryStateService : IAccountEntryStateService
    {
        public ValueTask<AccountEntryState> LoadAsync(
            AccountId accountId,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<AccountEntryState>(
                new InvalidDataException("account entry state is unavailable"));
    }

    private sealed class StatefulActiveCharacterService : IActiveCharacterService
    {
        private readonly Dictionary<AccountId, ActiveCharacterState> states = [];

        public StatefulActiveCharacterService()
        {
        }

        public StatefulActiveCharacterService(AccountId accountId, ActiveCharacterState state) =>
            states.Add(accountId, state);

        public int PublishCount { get; private set; }

        public void Publish(AccountId accountId, ActiveCharacterState character)
        {
            states[accountId] = character;
            PublishCount++;
        }

        public void Publish(SelectedSessionIdentity session, ActiveCharacterState character) =>
            Publish(session.AccountId, character);

        public bool ClearExact(SelectedSessionIdentity session) =>
            states.Remove(session.AccountId);

        public ActiveCharacterState GetRequired(AccountId accountId) => states[accountId];
    }
}
