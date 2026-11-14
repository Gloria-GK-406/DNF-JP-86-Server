using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Tests.Support;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.MoveItemSpace;

/// <summary>
/// 流浪武士 (female swordman, grow type 4) wears a second beam sword in equipment list slot 24 (#342).
/// </summary>
/// <remarks>
/// Online 0.3.2, 2026-11-14: beam sword 101040191 dragged from the bag to equipment 24 was refused
/// as <c>ItemCannotOccupySlot</c>, and equipment 12 -> 24 as <c>ListNotServed</c>. The allowance is
/// read from the real archive: <c>[support weapon sub type] 5</c> under the character file's fifth
/// grow type block, and each weapon's own <c>[sub type]</c>.
/// </remarks>
public sealed class SecondWeaponTests(ArchiveContentFixture archive) : IClassFixture<ArchiveContentFixture>
{
    private const int Character = 342;
    private const byte FemaleSwordman = 11;
    private const byte Vagabond = 4;

    /// <summary>密制狂暴光剑, <c>[sub type] 5</c>: the beam sword from the online refusal.</summary>
    private const int BeamSword = 101040191;

    /// <summary>密制莹光剑, <c>[sub type] 5</c>, usable by <c>[at swordman]</c>.</summary>
    private const int OtherBeamSword = 101040192;

    /// <summary>A katana, <c>[sub type] 1</c>.</summary>
    private const int Katana = 27850;

    private const short BagSlot = 17;
    private const short MainWeapon = 12;
    private const short SecondWeapon = 24;

    [PvfFact]
    public async Task Vagabond_wears_a_beam_sword_in_the_second_weapon_slot()
    {
        var side = Build(Vagabond);
        side.Seed(InventoryContainerType.Main, BagSlot, BeamSword);

        var result = await side.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, SecondWeapon);

        Assert.Equal(ItemMoveStatus.Moved, result.Status);
        Assert.Equal(BeamSword, (await side.ReadAsync(InventoryContainerType.Equipment, SecondWeapon))?.ItemId);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Main, BagSlot));
    }

    [PvfTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Other_female_swordman_advancements_cannot_wear_a_second_weapon(byte growType)
    {
        var side = Build(growType);
        side.Seed(InventoryContainerType.Main, BagSlot, BeamSword);

        var result = await side.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, SecondWeapon);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, result.Status);
        Assert.Equal(BeamSword, (await side.ReadAsync(InventoryContainerType.Main, BagSlot))?.ItemId);
    }

    [PvfFact]
    public async Task Vagabond_second_weapon_must_be_a_beam_sword()
    {
        var side = Build(Vagabond);
        side.Seed(InventoryContainerType.Main, BagSlot, Katana);

        var result = await side.MoveAsync(InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, SecondWeapon);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, result.Status);
    }

    [PvfFact]
    public async Task Vagabond_moves_the_main_weapon_to_the_second_slot_and_swaps_them_back()
    {
        var side = Build(Vagabond);
        side.Seed(InventoryContainerType.Equipment, MainWeapon, BeamSword);

        Assert.Equal(ItemMoveStatus.Moved, (await side.MoveAsync(
            InventoryContainerType.Equipment, MainWeapon, InventoryContainerType.Equipment, SecondWeapon)).Status);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Equipment, MainWeapon));
        Assert.Equal(BeamSword, (await side.ReadAsync(InventoryContainerType.Equipment, SecondWeapon))?.ItemId);

        side.Seed(InventoryContainerType.Equipment, MainWeapon, OtherBeamSword);
        Assert.Equal(ItemMoveStatus.Moved, (await side.MoveAsync(
            InventoryContainerType.Equipment, SecondWeapon, InventoryContainerType.Equipment, MainWeapon)).Status);
        Assert.Equal(BeamSword, (await side.ReadAsync(InventoryContainerType.Equipment, MainWeapon))?.ItemId);
        Assert.Equal(OtherBeamSword, (await side.ReadAsync(InventoryContainerType.Equipment, SecondWeapon))?.ItemId);
    }

    [PvfFact]
    public async Task A_katana_on_the_main_slot_cannot_be_swapped_into_the_second_slot()
    {
        var side = Build(Vagabond);
        side.Seed(InventoryContainerType.Equipment, MainWeapon, Katana);
        side.Seed(InventoryContainerType.Equipment, SecondWeapon, BeamSword);

        var result = await side.MoveAsync(
            InventoryContainerType.Equipment, SecondWeapon, InventoryContainerType.Equipment, MainWeapon);

        Assert.Equal(ItemMoveStatus.DisplacedCannotOccupySlot, result.Status);
        Assert.Equal(Katana, (await side.ReadAsync(InventoryContainerType.Equipment, MainWeapon))?.ItemId);
        Assert.Equal(BeamSword, (await side.ReadAsync(InventoryContainerType.Equipment, SecondWeapon))?.ItemId);
    }

    [PvfFact]
    public async Task Moving_between_worn_slots_is_refused_for_a_career_without_a_second_weapon()
    {
        var side = Build(1);
        side.Seed(InventoryContainerType.Equipment, MainWeapon, BeamSword);

        var result = await side.MoveAsync(
            InventoryContainerType.Equipment, MainWeapon, InventoryContainerType.Equipment, SecondWeapon);

        Assert.Equal(ItemMoveStatus.ItemCannotOccupySlot, result.Status);
        Assert.Equal(BeamSword, (await side.ReadAsync(InventoryContainerType.Equipment, MainWeapon))?.ItemId);
    }

    [PvfFact]
    public async Task Vagabond_replaces_the_second_weapon_from_the_bag_either_way_round()
    {
        var side = Build(Vagabond);
        side.Seed(InventoryContainerType.Equipment, SecondWeapon, BeamSword);
        side.Seed(InventoryContainerType.Main, BagSlot, OtherBeamSword);

        Assert.Equal(ItemMoveStatus.Moved, (await side.MoveAsync(
            InventoryContainerType.Main, BagSlot, InventoryContainerType.Equipment, SecondWeapon)).Status);
        Assert.Equal(OtherBeamSword, (await side.ReadAsync(InventoryContainerType.Equipment, SecondWeapon))?.ItemId);
        Assert.Equal(BeamSword, (await side.ReadAsync(InventoryContainerType.Main, BagSlot))?.ItemId);

        Assert.Equal(ItemMoveStatus.Moved, (await side.MoveAsync(
            InventoryContainerType.Equipment, SecondWeapon, InventoryContainerType.Main, BagSlot)).Status);
        Assert.Equal(BeamSword, (await side.ReadAsync(InventoryContainerType.Equipment, SecondWeapon))?.ItemId);
        Assert.Equal(OtherBeamSword, (await side.ReadAsync(InventoryContainerType.Main, BagSlot))?.ItemId);
    }

    [PvfFact]
    public async Task Vagabond_takes_the_second_weapon_off()
    {
        var side = Build(Vagabond);
        side.Seed(InventoryContainerType.Equipment, SecondWeapon, BeamSword);

        Assert.Equal(ItemMoveStatus.Moved, (await side.MoveAsync(
            InventoryContainerType.Equipment, SecondWeapon, InventoryContainerType.Main, BagSlot)).Status);
        Assert.Null(await side.ReadAsync(InventoryContainerType.Equipment, SecondWeapon));
        Assert.Equal(BeamSword, (await side.ReadAsync(InventoryContainerType.Main, BagSlot))?.ItemId);
    }

    [PvfFact]
    public async Task The_archive_gives_only_the_vagabond_a_second_weapon_and_the_beam_sword_a_candidate_slot()
    {
        Assert.Equal(new[] { 5 }, await archive.SupportWeapons.SubTypesAsync(new CharacterCareer(FemaleSwordman, Vagabond), default));
        Assert.Equal(new[] { 5 }, await archive.SupportWeapons.SubTypesAsync(new CharacterCareer(FemaleSwordman, Vagabond, 1), default));
        Assert.Empty(await archive.SupportWeapons.SubTypesAsync(new CharacterCareer(FemaleSwordman, 1), default));
        Assert.Empty(await archive.SupportWeapons.SubTypesAsync(new CharacterCareer(12, 1), default));
        Assert.Equal(new(new BusinessSlot(BusinessInventorySpace.WornEquipment, 12), 5),
            await archive.EquipmentSlots.GetSupportWeaponAsync(BeamSword, default));
        Assert.Equal(1, (await archive.EquipmentSlots.GetSupportWeaponAsync(Katana, default))?.SubType);

        var placement = new ItemPlacementRuleService(archive.Placements, archive.EquipmentSlots);
        var second = new BusinessSlot(BusinessInventorySpace.WornEquipment, 12);
        Assert.True(await placement.MayOccupyWornSlotAsync(BeamSword, second, default));
        Assert.True(await placement.MayOccupyWornSlotAsync(BeamSword, new(BusinessInventorySpace.WornEquipment, 0), default));
        Assert.False(await placement.MayOccupyWornSlotAsync(BeamSword, new(BusinessInventorySpace.WornEquipment, 2), default));
    }

    private Side Build(byte growType)
    {
        var character = new CharacterId(Character);
        var containers = new CharacterContainerDomainService(TestRequired.TransactionScope());
        var fixture = new MoveItemFixture();
        foreach (var type in (InventoryContainerType[])[InventoryContainerType.Main, InventoryContainerType.Equipment])
            TestGameplay.Run(() => containers.Register(character, new SlotBook(type, character, fixture)));

        var rules = new EquipmentWearRuleService(null!, archive.EquipmentSlots, archive.SupportWeapons);
        return new Side(
            containers,
            new InventoryMoveDomainService(
                TestRequired.SlotsOver(containers),
                new ItemMovementDomainService(
                    new ItemPlacementRuleService(archive.Placements, archive.EquipmentSlots),
                    new ItemSlotRuleService(),
                    new CareerWearing(new CharacterCareer(FemaleSwordman, growType), rules),
                    mirrors: TestRequired.Absent<IItemMirrorDomainService>()),
                creatures: TestRequired.Absent<Dfo.Server.Abstractions.Creatures.ICreatureRuntimeDomainService>(),
                sessions: TestRequired.Absent<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>(),
                cargo: TestRequired.Absent<ICargoTransferDomainService>()),
            character, fixture);
    }

    private sealed record Side(
        CharacterContainerDomainService Containers, InventoryMoveDomainService Handler,
        CharacterId Character, MoveItemFixture Fixture)
    {
        public void Seed(InventoryContainerType type, short slot, int itemId) =>
            ((SlotBook)Containers.GetContainer(Character, type)).Seed(slot, itemId);

        public ValueTask<ItemMoveResult> MoveAsync(
            InventoryContainerType source, short sourceSlot, InventoryContainerType destination, short destinationSlot) =>
            Fixture.RunAsync(() => Handler.MoveAsync(new InventoryMoveRequest(
                Character,
                A21InventoryLayout.ToBusinessSlot(new GameSlot(source, sourceSlot)),
                1,
                A21InventoryLayout.ToBusinessSlot(new GameSlot(destination, destinationSlot)),
                0,
                0), CancellationToken.None));

        public ValueTask<ItemSnapshot?> ReadAsync(InventoryContainerType type, short slot) =>
            ((SlotBook)Containers.GetContainer(Character, type)).ReadSnapshotAsync(slot, CancellationToken.None);
    }

    private sealed class SlotBook(InventoryContainerType type, CharacterId character, MoveItemFixture fixture)
        : LifecycleTestContainer(type, character, fixture)
    {
        public void Seed(int slot, int itemId) => SeedItem(slot, itemId, 1);
    }

    /// <summary>The wear eligibility of one fixed career, answered by the production rule.</summary>
    private sealed class CareerWearing(CharacterCareer career, EquipmentWearRuleService rules) : IEquipmentWearDomainService
    {
        public ItemSnapshot UnsealForEquip(ItemSnapshot item) => item;

        public ValueTask<bool> CanWearAsync(CharacterId characterId, int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask<bool> MayWearAsSupportWeaponAsync(CharacterId characterId, int itemId, BusinessSlot slot,
            CancellationToken cancellationToken) =>
            rules.MayCarryAsSupportWeaponAsync(career, itemId, slot, cancellationToken);
    }
}
