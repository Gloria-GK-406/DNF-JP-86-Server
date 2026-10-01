using System.Buffers.Binary;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game;

public sealed class ReplayStateProjectionTests
{
    [Fact]
    public void Current_inventory_has_six_tables_zero_virtual_balances_and_explicit_empty_physical_slots()
    {
        var state = State(ReplayProjectionNeeds.Inventory) with
        {
            Request = new(ReplayProjectionNeeds.Inventory,
                [new(BusinessInventorySpace.QuickSlot, 1), new(BusinessInventorySpace.QuickSlot, 0), new(BusinessInventorySpace.QuickSlot, 1)]),
        };
        var messages = ReplayStateProjectionDelivery.Create(state);
        Assert.Equal(7, messages.Length);
        var snapshots = messages.OfType<InventorySnapshotMessage>().ToArray();
        Assert.Equal(new[] { InventoryContainerType.Main, InventoryContainerType.Avatar, InventoryContainerType.PersonalCargo,
            InventoryContainerType.Equipment, InventoryContainerType.Pet, InventoryContainerType.AccountCargo }, snapshots.Select(x => x.Container.ContainerType));
        var main = Encode(snapshots[0]).AsSpan(15);
        Assert.Equal(15, BinaryPrimitives.ReadUInt16LittleEndian(main[3..])); // 14 virtual plus one actual item.
        Assert.Equal(0, BinaryPrimitives.ReadInt16LittleEndian(main[5..]));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(main[7..])); // ItemId=0 is gold.
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(main[11..]));
        // The six cube and five account material entries are present even when their values are zero.
        Assert.All(Enumerable.Range(4, 11), index => Assert.Equal(0,
            BinaryPrimitives.ReadInt32LittleEndian(Encode(snapshots[0]).AsSpan(15 + 5 + index * 101 + 6, 4))));
        var clear = Encode(messages[^1]).AsSpan(15);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(clear[1..]));
        Assert.Equal(4, BinaryPrimitives.ReadInt16LittleEndian(clear[3..]));
        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(clear[5..]));
    }

    [Theory]
    [InlineData(-1, uint.MaxValue)]
    [InlineData(int.MinValue, 0x80000000u)]
    public void Quest_refresh_preserves_unsigned_trigger_bits(int trigger, uint expected)
    {
        var state = State(ReplayProjectionNeeds.Quests) with
        { Quests = new([new(0, 55, trigger)], [0], []) };
        var messages = ReplayStateProjectionDelivery.Create(state);
        Assert.Equal(3, messages.Length);
        var frame = Encode(messages[0]);
        Assert.Equal(0x023F, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(expected, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(21)));
    }

    [Fact]
    public void Snapshot_owns_pet_avatar_skill_and_character_buffers_in_both_directions()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var extras = new byte[] { 3 };
        var first = source.CharacterEntry.Skills.Pages[0];
        source = source with { CharacterEntry = source.CharacterEntry with
        { Skills = source.CharacterEntry.Skills with { Pages =
            [first with { Skills = [first.Skills[0] with { ExtraValues = extras }] }, source.CharacterEntry.Skills.Pages[1]] } } };
        var pets = source.CharacterEntry.Features.Pets.Select(ReplayPetState.From).ToImmutableArray();
        var inventory = new ReplayInventoryState(source.CharacterInventory, source.AccountInventory);
        var skills = new ReplaySkillState(source.CharacterEntry.Skills);
        var character = new ReplayCharacterState(source.Character, source.CharacterEntry.Public);
        var avatar = source.CharacterInventory.Containers.Single(x => x.ContainerType == InventoryContainerType.Avatar).Items.Single().AvatarDetail!;
        var combo = source.CharacterEntry.Skills.DarkKnightCombos![0].EncodedBody;
        source.CharacterEntry.Features.Pets[0].CreatureText[0] = 99;
        avatar.JewelSocket[0] = 99; extras[0] = 99; combo[0] = 99;
        source.Character.Identity.Name[0] = 99;
        source.CharacterEntry.Public.Appearance.EncodedEntries[0] = 99;
        Assert.Equal(0x50, pets[0].ToState().CreatureText[0]);
        Assert.Equal(0, Avatar(inventory).JewelSocket[0]);
        Assert.Equal(3, skills.ToState().Pages[0].Skills[0].ExtraValues[0]);
        Assert.Equal(1, skills.ToState().DarkKnightCombos![0].EncodedBody[0]);
        Assert.Equal(0x41, character.Character().Identity.Name[0]);
        Assert.Equal(0, character.Public().Appearance.EncodedEntries[0]);
        // Adapters expose legacy mutable arrays, but mutating them cannot change a later replay.
        pets[0].ToState().CreatureText[0] = 77; Avatar(inventory).JewelSocket[0] = 77;
        skills.ToState().DarkKnightCombos![0].EncodedBody[0] = 77;
        character.Character().Identity.Name[0] = 77;
        Assert.Equal(0x50, pets[0].ToState().CreatureText[0]);
        Assert.Equal(0, Avatar(inventory).JewelSocket[0]);
        Assert.Equal(1, skills.ToState().DarkKnightCombos![0].EncodedBody[0]);
        Assert.Equal(0x41, character.Character().Identity.Name[0]);
    }

    [Fact]
    public void Absolute_xp_has_no_earned_bonus_and_premium_uses_the_cmd_registry()
    {
        var messages = ReplayStateProjectionDelivery.Create(State(ReplayProjectionNeeds.Experience | ReplayProjectionNeeds.Premium));
        Assert.Equal(2, messages.Length);
        var xp = Assert.IsType<ExperienceNotification>(messages[0]);
        Assert.Equal(60, xp.Level); Assert.Equal(100u, xp.TotalExperience);
        Assert.Same(ExperienceBonusLines.None, xp.Lines);
        Assert.Equal(0, Encode(xp)[0]);
        var premium = Assert.IsType<PremiumServiceStateMessage>(messages[1]);
        var frame = Encode(premium); Assert.Equal(1, frame[0]);
        Assert.Equal(0x036F, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.DoesNotContain(messages, x => x is CharacterEntryAcceptedMessage);
    }

    [Fact]
    public void Inventory_count_is_u16_and_does_not_inherit_the_pet_or_quest_ack_u8_limit()
    {
        var state = State(ReplayProjectionNeeds.Inventory); var inventory = state.Inventory!.Character();
        var main = inventory.Containers.Single(x => x.ContainerType == InventoryContainerType.Main);
        main = main with { Items = Enumerable.Range(3, 256).Select(slot => main.Items[0] with { SlotIndex = slot }).ToArray() };
        inventory = inventory with { Containers = inventory.Containers.Select(x => x.ContainerType == main.ContainerType ? main : x).ToArray() };
        var messages = ReplayStateProjectionDelivery.Create(state with { Inventory = new(inventory, state.Inventory.Account()) });
        Assert.Equal(270, BinaryPrimitives.ReadUInt16LittleEndian(Encode(messages[0]).AsSpan(18)));
        Assert.All(messages, message => Assert.NotEmpty(Encode(message)));
    }

    [Fact]
    public void A_skill_page_outside_the_real_two_page_layout_is_explicitly_refused()
    {
        var state = State(ReplayProjectionNeeds.Skills);
        var skills = state.Skills!.ToState() with { Pages = [new(2, [])] };
        Assert.Throws<InvalidDataException>(() => ReplayStateProjectionDelivery.Create(state with { Skills = new(skills) }));
    }

    [Fact]
    public void Usage_refresh_keeps_the_existing_reset_then_255_entry_chunks()
    {
        var state = State(ReplayProjectionNeeds.ItemUsage) with
        { ItemUsage = Enumerable.Range(1, 256).Select(id => new ItemUsageState(id, 1, 3, null, null)).ToImmutableArray() };
        var messages = ReplayStateProjectionDelivery.Create(state);
        Assert.Equal(3, messages.Length);
        Assert.Equal(new byte[] { 0, 255, 1 }, messages.Select(message => Encode(message)[15]));
        Assert.All(messages, message => Assert.Equal(0x021E, BinaryPrimitives.ReadUInt16LittleEndian(Encode(message).AsSpan(1))));
    }

    private static InventoryAvatarDetailState Avatar(ReplayInventoryState state) => state.Character().Containers
        .Single(x => x.ContainerType == InventoryContainerType.Avatar).Items.Single().AvatarDetail!;

    private static ReplayStateSnapshot State(ReplayProjectionNeeds needs)
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        return new(SelectedSessionIdentity.Create(source.Account.AccountId, source.Character.Identity.CharacterId, new("replay"), 1, 10011),
            new(needs, []), source.World.PreparedAt, new(source.CharacterInventory, source.AccountInventory),
            source.CharacterEntry.Features.Pets.Select(ReplayPetState.From).ToImmutableArray(), new(source.CharacterEntry.Skills),
            new(source.CharacterEntry.Quests.ActiveQuests.ToImmutableArray(), source.CharacterEntry.Quests.VisibilityFlags.ToImmutableArray(), []),
            source.Character.Progression, source.CharacterEntry.Skills.ProtocolMirror, null, [], [], [],
            new(source.Character, source.CharacterEntry.Public));
    }

    private static byte[] Encode(IGameServerMessage message)
    {
        var services = new ServiceCollection().AddLogging();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = provider.GetServices<ServerMessageEncoderRegistration>().Single(x => x.MessageType == message.GetType());
        return Assert.Single(((IServerMessageEncoder)provider.GetRequiredService(registration.EncoderType)).Encode(message));
    }
}
