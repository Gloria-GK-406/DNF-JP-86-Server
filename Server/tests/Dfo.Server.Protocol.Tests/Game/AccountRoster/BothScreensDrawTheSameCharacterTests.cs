using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.AccountRoster;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;

namespace Dfo.Server.Protocol.Tests.Game.AccountRoster;

/// <summary>
/// The character-selection screen and the in-world frame draw one character the same way.
/// </summary>
/// <remarks>
/// They used to answer from two different places: the frame derived the parts from the equipment
/// list inside its own writing loop, and the roster decoded a blob written once when the character
/// was created. One rule answers both now, and these are the checks that hold it to that.
/// <para>
/// <b>Why there are three cases rather than one.</b>
/// <see cref="The_two_screens_draw_the_same_character_part_for_part"/> compares the two sides to
/// each other, which is the property that matters and is also the property a mutation of the shared
/// rule cannot break -- both sides move together. So each side is also pinned on its own against
/// the run the rule names. Mutating the rule turns one of those red on each side; without them, the
/// two would go on agreeing about a wrong picture.
/// </para>
/// </remarks>
public sealed class BothScreensDrawTheSameCharacterTests
{
    /// <summary>One drawn part, in bytes. The same length on both sides, which is the point.</summary>
    private const int PartLength = 23;

    /// <summary>
    /// A worn set that covers the first drawn slot, the weapon, the title,
    /// an excluded equipment slot and the support weapon slot.
    /// Titles belong to the drawn run even when equipped from the title book.
    /// </summary>
    private static readonly (int Slot, int ItemId, byte Attribute, byte Amplify, byte Enchant)[] Worn =
    [
        (0, 413560000, 3, 0, 0),
        (12, 27600, 5, 2, 9),
        (13, 10400, 1, 0, 1),
        (23, 12400, 0, 1, 4),
        (24, 88800, 2, 0, 2),
    ];

    /// <summary>The slots a character wearing all of that is drawn in.</summary>
    private static readonly int[] DrawnSlots = [0, 12, 13, 24];

    /// <summary>
    /// The slot-twelve part, written out rather than recomputed from <see cref="Worn"/>.
    /// </summary>
    /// <remarks>
    /// Item 27600 is <c>D0 6B 00 00</c>; the expansion declares its own length of four and then
    /// four zero bytes; the state byte is the attribute's low five bits doubled plus one for being
    /// amplified, which for attribute five and a non-zero amplify type is eleven; the link item and
    /// the enchant value are zero on both screens; the last byte is the enchant upgrade count.
    /// </remarks>
    private static readonly byte[] SlotTwelveDrawnPart =
    [
        0x0C,
        0xD0, 0x6B, 0x00, 0x00,
        0x04, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x0B,
        0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
        0x09,
    ];

    [Fact]
    public void The_two_screens_draw_the_same_character_part_for_part()
    {
        var inWorld = InWorldParts();
        var selection = SelectionScreenParts();

        Assert.Equal(inWorld.Count, selection.Count);
        for (var index = 0; index < inWorld.Count; index++)
        {
            Assert.Equal(inWorld[index], selection[index]);
        }
    }

    [Fact]
    public void The_in_world_frame_draws_exactly_the_slots_the_rule_names()
    {
        var parts = InWorldParts();

        Assert.Equal(DrawnSlots, parts.Select(part => (int)part[0]));
        Assert.Equal(SlotTwelveDrawnPart, parts[1]);
    }

    [Fact]
    public void The_selection_screen_draws_exactly_the_slots_the_rule_names()
    {
        var parts = SelectionScreenParts();

        Assert.Equal(DrawnSlots, parts.Select(part => (int)part[0]));
        Assert.Equal(SlotTwelveDrawnPart, parts[1]);
    }

    /// <summary>The appearance run out of a subtype-zero USERINFO body.</summary>
    /// <remarks>
    /// The count sits after the reserved block, the character id, the name behind its own
    /// thirty-two-bit length, and the six single bytes that follow -- job, growth, level, the two
    /// PvP grades and the user state. Reached by adding those up rather than by searching for the
    /// bytes, so a layout change ahead of the run fails here instead of finding a false match.
    /// </remarks>
    private static IReadOnlyList<byte[]> InWorldParts()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var body = StateSynchronizationPayloadCodec.EncodeCurrentUserState(
            source.Character, source.CharacterEntry.Public, WornItems(), []);
        var countOffset = StateSynchronizationPayloadCodec.CurrentUserStateCharacterIdOffset
            + sizeof(ushort)
            + sizeof(int) + source.Character.Identity.Name.Length
            + 6;
        return Parts(body, countOffset);
    }

    /// <summary>The appearance parts out of the character-selection roster's body.</summary>
    /// <remarks>
    /// Eighteen bytes of roster header, then this character's slot, its name behind a
    /// thirty-two-bit length, the seven bytes from the two zeroes to the pair after the level, and
    /// the two four-byte honour fields.
    /// </remarks>
    private static IReadOnlyList<byte[]> SelectionScreenParts()
    {
        var name = "1122"u8.ToArray();
        var body = AccountCharacterRosterPayloadCodec.Encode(
            new AccountCharacterRoster(
                new AccountId(1),
                SlotLimit: 32,
                [
                    new CharacterSummary(
                        new CharacterId(1), SlotIndex: 0, name, Job: 13, GrowType: 0, Level: 1,
                        CloneTitleItemId: 0, DrawnAppearance.Of(WornItems())),
                ]));
        var countOffset = 18
            + sizeof(ushort)
            + sizeof(int) + name.Length
            + 7
            + (2 * sizeof(uint));
        return Parts(body, countOffset);
    }

    private static IReadOnlyList<byte[]> Parts(byte[] body, int countOffset)
    {
        var count = body[countOffset];
        return
        [
            .. Enumerable.Range(0, count).Select(index =>
                body[(countOffset + 1 + (index * PartLength))..(countOffset + 1 + ((index + 1) * PartLength))]),
        ];
    }

    private static IReadOnlyList<InventoryItemState> WornItems() =>
    [
        .. Worn.Select((piece, index) => new InventoryItemState(
            new ItemIdentity(index + 1),
            piece.Slot,
            Core(piece.ItemId, piece.Attribute, piece.Amplify, piece.Enchant))),
    ];

    private static InventoryItemCoreState Core(
        int itemId, byte attribute, byte amplifyType, byte enchantUpgradeCount) => new(
        1, itemId, 1, attribute, 50, 0, 0, enchantUpgradeCount, amplifyType, 0, -1,
        new ChronicleOptionState(0, 0, 0, 0, 0),
        new ChronicleOptionState(0, 0, 0, 0, 0),
        0, 0, 0, 0, 0,
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        0, 0xFF, 0, new RandomOptionState(0, 0, 0),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
