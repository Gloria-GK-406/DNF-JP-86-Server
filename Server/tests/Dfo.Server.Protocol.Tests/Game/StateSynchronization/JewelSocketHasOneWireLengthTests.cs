using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

/// <summary>
/// An avatar's jewel socket is stored in thirty bytes and reaches the wire in eighteen, and every
/// frame that carries it says the same thing about how many.
/// </summary>
/// <remarks>
/// Three frames carry the socket, and they disagreed. The item lists were corrected to A21's
/// eighteen; the worn entry and the mailbox attachment were left at thirty, because nothing
/// connected the three. Nothing could have caught it either: the run declares its own length, so
/// the client honours whatever it is given, and every socket that has been tested live is all
/// zeros, where eighteen zeros and thirty zeros are the same picture.
/// <para>
/// So the assertions here are of two kinds, and both are needed. The per-frame ones pin today's
/// figure at eighteen, which is what the reference A21 server sends -- they go red if the number
/// itself is wrong. <see cref="EveryFrameDeclaresTheSameJewelLength"/> pins nothing but agreement,
/// naming no value at all -- it goes red the day one of the three is changed and the others are
/// not, which is the failure that actually happened.
/// </para>
/// <para>
/// The eighteen is read out of the reference server's source, where all three of its writers share
/// one <c>A21AvatarJewelBytes = 18</c>. It is not measured against a live client, and it cannot be:
/// see the remarks on <see cref="AvatarJewelSocketOnTheWire.WireJewelBytes"/>.
/// </para>
/// </remarks>
public sealed class JewelSocketHasOneWireLengthTests
{
    private static readonly DateTimeOffset PreparedAt =
        new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private const int StoredJewelBytes = 30;

    /// <summary>What the reference A21 server sends, and what these frames are expected to send.</summary>
    /// <remarks>
    /// Written out rather than read from the production constant on purpose: a test that reads the
    /// value it is checking proves only that the value equals itself.
    /// </remarks>
    private const int ExpectedWireJewelBytes = 18;

    /// <summary>A stored socket whose every byte is distinct, so a prefix cannot pass for a suffix.</summary>
    /// <remarks>
    /// All zeros -- which is what every socket in the repository actually holds today -- would let
    /// four different mistakes look identical: sending thirty, sending the last eighteen, sending
    /// eighteen zeros of its own, and sending the right eighteen. These values tell them apart, and
    /// they are high enough to be unlikely to collide with the counts and ids around them.
    /// </remarks>
    private static byte[] StoredSocket() =>
        [.. Enumerable.Range(0, StoredJewelBytes).Select(index => (byte)(0xA0 + index))];

    /// <summary>The twelve stored bytes A21 leaves behind, as a run.</summary>
    private static byte[] BytesLeftBehind() => StoredSocket()[ExpectedWireJewelBytes..];

    [Fact]
    public void TheItemListSendsEighteenOfTheThirtyStoredBytes() =>
        AssertSendsTheFirstEighteen(ItemListFrame());

    [Fact]
    public void TheSupportCharacterFrameSendsEighteenOfTheThirtyStoredBytes() =>
        AssertSendsTheFirstEighteen(SupportCharacterFrame());

    /// <summary>
    /// The worn entry has two senders, and both are checked rather than one standing in for the other.
    /// </summary>
    /// <remarks>
    /// They share a writer today. That is an implementation fact, not a promise: whoever splits them
    /// -- and the two frames do differ elsewhere -- should find out here rather than in a client.
    /// </remarks>
    [Fact]
    public void TheUserInfoFrameSendsEighteenOfTheThirtyStoredBytes() =>
        AssertSendsTheFirstEighteen(UserInfoFrame());

    [Fact]
    public void TheMailboxAttachmentSendsEighteenOfTheThirtyStoredBytes() =>
        AssertSendsTheFirstEighteen(MailboxFrame());

    /// <summary>
    /// The one assertion that survives a change of mind about the number itself.
    /// </summary>
    /// <remarks>
    /// It names no length. Every frame is asked what it declared, and the answers must match. If
    /// eighteen is ever found to be wrong, this stays green while the per-frame assertions above go
    /// red -- which is the right way round, because the thing worth protecting is that the four
    /// senders keep agreeing, not that they agree on eighteen in particular.
    /// </remarks>
    [Fact]
    public void EveryFrameDeclaresTheSameJewelLength()
    {
        var declared = new (string Frame, int Length)[]
        {
            ("NOTI 0x000D/0x000E item list", DeclaredJewelLength(ItemListFrame())),
            ("message 0x019F support character", DeclaredJewelLength(SupportCharacterFrame())),
            ("NOTI 0x0002 subtype 1 user info", DeclaredJewelLength(UserInfoFrame())),
            ("message 0x0061 mailbox", DeclaredJewelLength(MailboxFrame())),
        };

        Assert.All(declared, value => Assert.Equal(declared[0].Length, value.Length));
    }

    /// <summary>The twelve bytes past the wire length reach no frame, in whole or in part.</summary>
    /// <remarks>
    /// Asserting that the first eighteen are present does not by itself rule out all thirty having
    /// been written; the extra twelve would simply sit after them. This looks for them as a run
    /// anywhere in the frame, which a coincidence of twelve consecutive bytes will not produce.
    /// </remarks>
    [Fact]
    public void TheTwelveBytesThatStayBehindReachNoFrame()
    {
        var leftBehind = BytesLeftBehind();
        foreach (var frame in new[]
                 {
                     ItemListFrame(), SupportCharacterFrame(), UserInfoFrame(), MailboxFrame(),
                 })
        {
            Assert.Equal(-1, IndexOfRun(frame, leftBehind));
        }
    }

    /// <summary>
    /// A costume-bar slot holding something that is not an avatar still declares no jewels at all.
    /// </summary>
    /// <remarks>
    /// The zero is a length, and nothing follows it -- so the colour run's own length is the very
    /// next thing on the wire, and its two colours are zero because a thing that is not an avatar
    /// has none to give. Sending zeros of jewel-run length here instead would parse and would be
    /// wrong, and that is what the second assertion rules out: the entry is shorter than the same
    /// slot holding an avatar by the avatar's whole run of bytes, which can only be true if this
    /// branch wrote none of them. It says nothing about how long that run is, and is not meant to.
    /// <para>
    /// The twelve bytes the first assertion looks for are eleven zeros around a four -- loose
    /// enough to turn up elsewhere by accident, so it is a shape check and not the proof.
    /// An earlier draft tried to prove more by searching for the length itself as
    /// <c>[length, 0, 0, 0]</c>; that pattern is three-quarters zeros and was found by coincidence
    /// in a frame that had no jewel run at all, so it reported a failure for the wrong reason.
    /// A search that can fail when the code is right is worse than no search.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnOrdinaryItemInACostumeSlotDeclaresNoJewelsAndGoesStraightToTheColours()
    {
        var withAvatar = SupportCharacterFrame();
        var frame = SupportCharacterFrame(avatarKind: false);

        Assert.NotEqual(-1, IndexOfRun(frame, [0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Equal(withAvatar.Length - DeclaredJewelLength(withAvatar), frame.Length);
    }

    /// <summary>The colour pair keeps its own four-byte run behind its own length, everywhere.</summary>
    [Fact]
    public void TheColoursFollowTheJewelsUnchangedInEveryFrame()
    {
        foreach (var frame in new[]
                 {
                     ItemListFrame(), SupportCharacterFrame(), UserInfoFrame(), MailboxFrame(),
                 })
        {
            var jewels = IndexOfRun(frame, StoredSocket()[..ExpectedWireJewelBytes]);
            Assert.NotEqual(-1, jewels);

            var afterJewels = jewels + ExpectedWireJewelBytes;
            Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(afterJewels)));
            Assert.Equal(0x1122, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(afterJewels + 4)));
            Assert.Equal(0x3344, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(afterJewels + 6)));
        }
    }

    private static void AssertSendsTheFirstEighteen(byte[] frame)
    {
        Assert.Equal(ExpectedWireJewelBytes, DeclaredJewelLength(frame));

        var at = IndexOfRun(frame, StoredSocket()[..ExpectedWireJewelBytes]);
        Assert.NotEqual(-1, at);
        Assert.Equal(ExpectedWireJewelBytes, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(at - 4)));
    }

    /// <summary>What a frame said its jewel run was, found without being told the answer.</summary>
    /// <remarks>
    /// Every offset is tried as a length, and the one that is followed by exactly that many bytes of
    /// the stored socket's own prefix is the run. Nothing here assumes eighteen or thirty, so the
    /// same reader works whatever the writer decided -- which is what lets
    /// <see cref="EveryFrameDeclaresTheSameJewelLength"/> compare frames without naming a length.
    /// </remarks>
    private static int DeclaredJewelLength(byte[] frame)
    {
        var stored = StoredSocket();
        for (var offset = 0; offset + 4 <= frame.Length; offset++)
        {
            var declared = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(offset));
            if (declared is < 1 or > StoredJewelBytes || offset + 4 + declared > frame.Length)
                continue;
            if (frame.AsSpan(offset + 4, declared).SequenceEqual(stored.AsSpan(0, declared)))
                return declared;
        }

        throw new InvalidOperationException("This frame carries no jewel run built from the stored socket.");
    }

    private static int IndexOfRun(byte[] haystack, byte[] needle)
    {
        for (var offset = 0; offset + needle.Length <= haystack.Length; offset++)
        {
            if (haystack.AsSpan(offset, needle.Length).SequenceEqual(needle))
                return offset;
        }

        return -1;
    }

    private static byte[] ItemListFrame() =>
        new InventoryDeltaMessage(
            InventoryContainerType.Avatar,
            [new InventoryItemState(new ItemIdentity(1), 3, Core(8), AvatarFacts())],
            PreparedAt).EncodeBody();

    private static byte[] SupportCharacterFrame(bool avatarKind = true)
    {
        var source = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
        var supportId = new CharacterId(909);
        var support = source.Character with
        {
            Identity = source.Character.Identity with { CharacterId = supportId, Name = [0x53] },
        };
        var entry = source.CharacterEntry with
        {
            CharacterId = supportId,
            Public = source.CharacterEntry.Public with
            {
                Extras = source.CharacterEntry.Public.Extras with { SkillTreeIndex = 0 },
            },
        };
        var inventory = source.CharacterInventory with
        {
            CharacterId = supportId,
            Containers = [new InventoryContainerState(InventoryContainerType.Equipment, 0, WornAvatar(avatarKind))],
        };
        var relation = new AccountCharacterRelationEntryState(
            source.Character.Identity.CharacterId, 0, supportId, 77, 88,
            new AccountSupportCharacterEntryState(support, entry, inventory));

        return StateSynchronizationPayloadCodec.EncodeSupportCharacter(
            source.Character.Identity.CharacterId, [relation], PreparedAt);
    }

    private static byte[] UserInfoFrame()
    {
        var source = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
        return StateSynchronizationPayloadCodec.EncodeExtendedUserState(
            source.Character,
            source.CharacterEntry.Public,
            source.CharacterEntry.Skills,
            WornAvatar(avatarKind: true),
            PreparedAt);
    }

    /// <summary>One item in costume-bar slot 2, an avatar or -- to check the other branch -- not.</summary>
    private static InventoryItemState[] WornAvatar(bool avatarKind) =>
    [
        avatarKind
            ? new InventoryItemState(new ItemIdentity(12), 2, Core(8), AvatarFacts())
            : new InventoryItemState(new ItemIdentity(12), 2, Core(1)),
    ];

    private static byte[] MailboxFrame()
    {
        var attachment = new CharacterMailboxAttachmentEntryState(
            AttachmentId: 5, Ordinal: 0, ItemType: 1, SourceListType: 0, SourceSlotIndex: 0,
            SourceItemUid: 12, ItemTemplateId: 222, ItemKind: "avatar", ItemCount: 1,
            InstanceValue: 9001, Durability: 50, SealFlag: 0, OptionValue: 0, ExpireTime: 300,
            Marker16: 0, PetSerialOrHandle: 0, ItemCore: new byte[82],
            DetailJson: AvatarDetailJson());
        var message = new CharacterMailboxMessageEntryState(
            MessageId: 7, SenderCharacterId: 11, SenderName: Encoding.UTF8.GetBytes("S"),
            MailType: 0, SourceProtocol: 0, Title: Encoding.UTF8.GetBytes("T"),
            Body: Encoding.UTF8.GetBytes("B"), Gold: 0, RemainingSeconds: 300,
            CreatedAtUnixSeconds: 1_700_000_000, LetterState: 0, Attachments: [attachment]);

        return StateSynchronizationPayloadCodec.EncodeMailbox(new CharacterMailboxEntryState(0, [message]));
    }

    private static string AvatarDetailJson() =>
        "{\"Avatar\":{\"ExpireDate\":1700000500,\"ClearAvatarId\":333,\"JewelSocket\":\""
        + Convert.ToBase64String(StoredSocket())
        + "\",\"Color1\":4386,\"Color2\":13124,\"DeleteDate\":0}}";

    private static InventoryAvatarDetailState AvatarFacts() =>
        new(1_700_000_500, 333, StoredSocket(), 0x1122, 0x3344, 0);

    private static InventoryItemCoreState Core(byte kind) => new(
        kind, 222, 9001, 4, 50, 1, 6, 7, 8, 9, -1,
        new ChronicleOptionState(0, 0, 0, 0, 0),
        new ChronicleOptionState(0, 0, 0, 0, 0),
        0, 0, 0, 0, 0,
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        new RandomOptionState(0, 0, 0),
        0, 0xFF, 0, new RandomOptionState(0, 0, 0),
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
