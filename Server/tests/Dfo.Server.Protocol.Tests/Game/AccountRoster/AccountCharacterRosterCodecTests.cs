using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Protocol.Game.AccountRoster;

namespace Dfo.Server.Protocol.Tests.Game.AccountRoster;

public sealed class AccountCharacterRosterCodecTests
{
    /// <summary>
    /// A roster carrying one character, transcribed from a session the A21 client accepted.
    /// </summary>
    /// <remarks>
    /// The whole body is kept as one literal rather than split into per-field assertions. The
    /// defect this guards against is a length change -- the entry tail was four bytes short and
    /// the two bytes after the level were wrong -- and a length change moves everything after it.
    /// Per-field assertions walk past that; a full-body comparison cannot.
    /// </remarks>
    private static readonly byte[] KnownGoodOneCharacterBody =
    [
        0x02, 0x20, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0x31, 0x31, 0x32, 0x32, 0x00, 0x00, 0x0D, 0x00,
        0x01, 0x0B, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x0C, 0x80, 0x80, 0xCB,
        0x06, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    private static CharacterRosterAppearanceState TheAppearanceThatSessionCarried() =>
        new(
            Slot: 0x0C,
            DisplayItemId: 0x06CB8080,
            ExpansionLength: 4,
            ExpansionData: [0x00, 0x00, 0x00, 0x00],
            State: 0,
            LinkItemId: 0,
            EnchantValue: 0,
            Flag20: 0);

    private static CharacterSummary TheCharacterThatSessionCarried(
        IReadOnlyList<CharacterRosterAppearanceState> appearances) =>
        new(
            new CharacterId(1),
            SlotIndex: 0,
            Name: "1122"u8.ToArray(),
            Job: 13,
            GrowType: 0,
            Level: 1,
            CloneTitleItemId: 0,
            Appearances: appearances);

    private static AccountCharacterRoster Roster(params CharacterSummary[] characters) =>
        new(new AccountId(10038), SlotLimit: 32, characters);

    [Fact]
    public void One_character_reproduces_the_body_the_client_accepted()
    {
        var body = AccountCharacterRosterPayloadCodec.Encode(
            Roster(TheCharacterThatSessionCarried([TheAppearanceThatSessionCarried()])));

        Assert.Equal(KnownGoodOneCharacterBody, body);
    }

    /// <summary>
    /// An empty roster is the eighteen-byte header alone, which is what a fresh account gets.
    /// </summary>
    [Fact]
    public void An_empty_roster_is_the_header_alone()
    {
        var body = AccountCharacterRosterPayloadCodec.Encode(Roster());

        Assert.Equal(KnownGoodOneCharacterBody.AsSpan(0, 16).ToArray().Concat(new byte[] { 0x00, 0x00 }), body);
    }

    /// <summary>
    /// The widths close: header, entry, twenty-three per appearance, thirty-six of tail.
    /// </summary>
    /// <remarks>
    /// Stated as arithmetic rather than as one more literal, because the arithmetic is the claim.
    /// A tail or appearance whose length drifts shows up here even when no transcribed sample
    /// happens to cover that shape.
    /// </remarks>
    [Theory]
    [InlineData(0, 18 + 26 + 36)]
    [InlineData(1, 18 + 26 + 23 + 36)]
    [InlineData(3, 18 + 26 + (23 * 3) + 36)]
    public void The_body_length_closes_over_the_appearance_count(int appearances, int expected)
    {
        var body = AccountCharacterRosterPayloadCodec.Encode(
            Roster(TheCharacterThatSessionCarried(
                [.. Enumerable.Repeat(TheAppearanceThatSessionCarried(), appearances)])));

        Assert.Equal(expected, body.Length);
    }

    /// <summary>
    /// Two characters both survive, which is what the tail's length is really protecting.
    /// </summary>
    /// <remarks>
    /// With a short tail the client reads the second entry from four bytes inside the first one's
    /// close. Note this arrangement does not by itself decide the tail's length -- both characters
    /// carry parts, so a different split of the same fifty-nine bytes lands the second entry in
    /// the same place. The test below is the one that decides it.
    /// </remarks>
    [Fact]
    public void A_second_character_starts_where_the_first_one_ends()
    {
        var one = TheCharacterThatSessionCarried([TheAppearanceThatSessionCarried()]);
        var two = one with { SlotIndex = 1, Name = "3344"u8.ToArray() };

        var body = AccountCharacterRosterPayloadCodec.Encode(Roster(one, two));

        var entryLength = 26 + 23 + 36;
        Assert.Equal(18 + (entryLength * 2), body.Length);
        Assert.Equal(2, body[16]);
        Assert.Equal("3344"u8.ToArray(), body.AsSpan(18 + entryLength + 6, 4).ToArray());
    }

    /// <summary>
    /// A bare character in the middle, which is the only shape that pins the tail's length.
    /// </summary>
    /// <remarks>
    /// A character with no parts is <c>26 + tail</c> and nothing else, so where the next entry
    /// starts depends on the tail alone. That is what makes this arrangement decide between the
    /// twenty-three/thirty-six split and any other pair summing to fifty-nine: with a bare entry
    /// last, or with only one character, the client stops after <c>count</c> entries and the
    /// difference is unobservable.
    ///
    /// The live run behind this: three characters, the middle one bare, encoded to 250 bytes and
    /// drawn correctly by the A21 client. Under a twenty-seven/thirty-two split the same roster
    /// would be 246, and the client would read the third entry's slot out of the second one's
    /// close.
    /// </remarks>
    [Fact]
    public void A_bare_character_between_two_dressed_ones_keeps_the_third_aligned()
    {
        var dressed = TheCharacterThatSessionCarried([TheAppearanceThatSessionCarried()]);
        var bare = dressed with { SlotIndex = 1, Name = "3344"u8.ToArray(), Appearances = [] };
        var last = dressed with { SlotIndex = 2, Name = "5566"u8.ToArray() };

        var body = AccountCharacterRosterPayloadCodec.Encode(Roster(dressed, bare, last));

        var dressedLength = 26 + 23 + 36;
        var bareLength = 26 + 36;
        Assert.Equal(18 + dressedLength + bareLength + dressedLength, body.Length);
        Assert.Equal(3, body[16]);
        Assert.Equal("3344"u8.ToArray(), body.AsSpan(18 + dressedLength + 6, 4).ToArray());
        Assert.Equal(
            "5566"u8.ToArray(),
            body.AsSpan(18 + dressedLength + bareLength + 6, 4).ToArray());
    }

    [Fact]
    public void An_appearance_whose_expansion_is_not_four_bytes_is_refused()
    {
        var appearance = TheAppearanceThatSessionCarried() with { ExpansionData = [0x00, 0x00] };

        Assert.Throws<InvalidDataException>(
            () => AccountCharacterRosterPayloadCodec.Encode(
                Roster(TheCharacterThatSessionCarried([appearance]))));
    }
}
