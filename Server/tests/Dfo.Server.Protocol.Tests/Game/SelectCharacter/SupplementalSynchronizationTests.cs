using System.Buffers.Binary;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game.SelectCharacter;

public sealed class SupplementalSynchronizationTests
{
    [Fact]
    public void ConcreteMessages_EncodeWithoutATypeSwitch()
    {
        var payloads = new SupplementalSynchronizationMessage[]
            { new UserVisibilityMessage(7, 3), new TownDepartureMessage(9) }
            .SelectMany(value => value.EncodePayloads())
            .ToArray();

        Assert.Equal([0x0165, 0x0006], payloads.Select(Type));
        Assert.All(
            new IGameServerMessage[]
                { new UserVisibilityMessage(7, 3), new TownDepartureMessage(9) },
            message => Assert.IsAssignableFrom<IGameNotiMessage>(message));
    }

    [Fact]
    public void CharacterAndAccountSynchronization_KeepsIdentitiesAndValues()
    {
        var visibility = SupplementalSynchronizationCodec.EncodeVisibility(0x1234, 0xA5);
        Assert.Equal((ushort)0x0165, Type(visibility));
        Assert.Equal([0x34, 0x12, 0xA5], Body(visibility));

        var clone = SupplementalSynchronizationCodec.EncodeCloneTitle(0x11223344);
        Assert.Equal((ushort)0x0239, Type(clone));
        Assert.Equal([1, 0x44, 0x33, 0x22, 0x11], Body(clone));

        var honor = SupplementalSynchronizationCodec.EncodeHonor(7, 0x11223344);
        Assert.Equal((ushort)0x0289, Type(honor));
        Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(Body(honor)));
        Assert.Equal(0x11223344u, BinaryPrimitives.ReadUInt32LittleEndian(Body(honor).AsSpan(4)));

        var growth = SupplementalSynchronizationCodec.EncodeGrowthCapsule(
            86, 1000, 1, 2, 3, 4, 5, 6, 7, 8);
        Assert.Equal((ushort)0x0025, Type(growth));
        Assert.Equal(95, Body(growth).Length);
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(Body(growth).AsSpan(0x0D)));
        Assert.Equal(6u, BinaryPrimitives.ReadUInt32LittleEndian(Body(growth).AsSpan(0x3B)));

        var prepared = SelectCharacterPayloadTestData.Create();
        var item = Assert.Single(prepared.CharacterInventory.Containers
            .Single(value => value.ContainerType == Dfo.Server.Data.Inventory.InventoryContainerType.Main).Items);
        var update = new InventoryDeltaMessage(
            Dfo.Server.Data.Inventory.InventoryContainerType.Main, [item], prepared.World.PreparedAt);
        var updateFrame = Assert.Single(
            new GameNotiStateSynchronizationEncoder<InventoryDeltaMessage>().Encode(update));
        Assert.Equal((ushort)0x000E, Type(updateFrame));
        Assert.Equal((byte)Dfo.Server.Data.Inventory.InventoryContainerType.Main, Body(updateFrame)[0]);
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(Body(updateFrame).AsSpan(1)));
    }

    [Fact]
    public void SelectCharacterRejection_EncodesOrderedCommandAndNoticePair()
    {
        var frames = new[]
        {
            Assert.Single(new SupplementalCmdSynchronizationEncoder<
                EntryAdmissionRejectedCommandMessage>().Encode(
                    new EntryAdmissionRejectedCommandMessage(0xFD))),
            Assert.Single(new SupplementalNotiSynchronizationEncoder<
                EntryAdmissionNoticeMessage>().Encode(
                    new EntryAdmissionNoticeMessage("level gate"))),
        };

        Assert.Equal([(byte)1, (byte)0], frames.Select(frame => frame[0]));
        Assert.Equal([0x0004, 0x01F7], frames.Select(Type));
        Assert.Equal([0x00, 0xFD], Body(frames[0]));
        Assert.Equal(0, Body(frames[1])[0]);
        Assert.Equal("level gate", System.Text.Encoding.UTF8.GetString(Body(frames[1]), 5, 10));
    }

    [Fact]
    public void SelectCharacterSupplemental_PetRevivalIsAdjacentOrEmpty()
    {
        Assert.Empty(SupplementalSynchronizationCodec.EncodePetRevival(null));

        var frames = SupplementalSynchronizationCodec.EncodePetRevival(
            new PetRevivalState(0x1122, 0x33445566, 77));

        Assert.Equal([0x006B, 0x0067], frames.Select(Type));
        Assert.Equal([0x22, 0x11], Body(frames[0]));
        Assert.Equal(0x33445566, BinaryPrimitives.ReadInt32LittleEndian(Body(frames[1])));
        Assert.Equal(77, BinaryPrimitives.ReadInt32LittleEndian(Body(frames[1]).AsSpan(4)));
    }

    [Fact]
    public void SelectCharacterSupplemental_TrailingLockIsDistinctFromStaticRow36()
    {
        var result = SelectCharacterPayloadTestData.CreateComplete();
        var frame = SupplementalSynchronizationCodec.EncodeTrailingEquipmentLocks(
            result.CharacterInventory, result.AccountInventory);

        Assert.Equal((ushort)0x00FB, Type(frame));
        Assert.Equal(EncodeStaticNotification(result, 0x00FB), Body(frame));
    }

    [Fact]
    public void SelectCharacterSupplemental_PvpDirectAndPublicationBodiesPreserveExplicitOrder()
    {
        var existingA = Identity(9, 0xA1);
        var existingB = Identity(3, 0xB2);
        var newcomer = Identity(7, 0xC3);
        var seats = Enumerable.Range(0, 8)
            .Select(index => new PvpRoomSeatState((byte)index, (ushort)(100 + index)))
            .ToArray();
        PvpRoomSnapshotState[] rooms =
        [
            new PvpRoomSnapshotState(20, 1, [], 2, 3, 4, 5, seats, true, 6),
            new PvpRoomSnapshotState(10, 0, [0x51], 7, 1, 8, 2, seats, false, 9),
        ];

        var batch = SupplementalSynchronizationCodec.EncodePvpLobby(
            new PvpLobbySynchronizationState([existingA, existingB], rooms, newcomer));

        Assert.Equal([0x0002, 0x0029], batch.DirectPayloads.Select(Type));
        Assert.Equal([0x0002], batch.PublicationPayloads.Select(Type));
        var roster = Body(batch.DirectPayloads[0]);
        Assert.Equal(3, BinaryPrimitives.ReadUInt16LittleEndian(roster.AsSpan(1)));
        Assert.True(Array.IndexOf(roster, (byte)0xA1) < Array.IndexOf(roster, (byte)0xB2));
        Assert.True(Array.IndexOf(roster, (byte)0xB2) < Array.IndexOf(roster, (byte)0xC3));
        var roomBody = Body(batch.DirectPayloads[1]);
        Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(roomBody));
        Assert.Equal(20, BinaryPrimitives.ReadUInt16LittleEndian(roomBody.AsSpan(2)));
        Assert.Equal([0xC3], newcomer.Character.Identity.Name);
    }

    [Fact]
    public void SelectCharacterSupplemental_QuestAndCleanupValuesArePureOptionalBodies()
    {
        Assert.Empty(SupplementalSynchronizationCodec.EncodeAcceptedQuests(null));
        var emptyQuest = Assert.Single(SupplementalSynchronizationCodec.EncodeAcceptedQuests([]));
        Assert.Equal((ushort)0x023F, Type(emptyQuest));
        Assert.Equal([0, 0, 0, 0], Body(emptyQuest));

        var quests = Assert.Single(SupplementalSynchronizationCodec.EncodeAcceptedQuests(
            [new AcceptedQuestProgressState(5, 6), new AcceptedQuestProgressState(7, 8)]));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(Body(quests)));
        Assert.Equal((ushort)5, BinaryPrimitives.ReadUInt16LittleEndian(Body(quests).AsSpan(4)));

        var close = SupplementalSynchronizationCodec.EncodeExpertStoreClose(0x1234);
        var leave = SupplementalSynchronizationCodec.EncodeTownLeave(0x5678);
        Assert.Equal([0x021C, 0x0006], new[] { Type(close), Type(leave) });
        Assert.Equal([0x34, 0x12], Body(close));
        Assert.Equal([0x78, 0x56], Body(leave));
    }

    [Fact]
    public void SelectCharacterSupplemental_RosterRequiresAndEncodesAppearanceForEveryCharacter()
    {
        var roster = new CharacterRosterEntryState(
            24, 24, 2, 100, 3, 200,
            [new CharacterRosterMemberState(
                new CharacterId(10), 1, [0x41], 2, 3, 86, 4, 5, 6,
                [new CharacterRosterAppearanceState(7, 1000, 4, [1, 2, 3, 4], 8, 2000, 9, 10)],
                3000)]);

        var payload = SupplementalSynchronizationCodec.EncodeRoster(roster);
        var body = Body(payload);
        Assert.Equal((ushort)0x0002, Type(payload));
        Assert.Equal(2, body[0]);
        Assert.Contains(BitConverter.GetBytes(1000), body);

        var frame = Assert.Single(
            new SupplementalNotiSynchronizationEncoder<AccountRosterStateMessage>().Encode(
                new AccountRosterStateMessage(roster)));
        Assert.Equal((byte)GameMessageClass.Noti, frame[0]);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7, 4)));
        Assert.Equal(body, Body(frame));

        Assert.Throws<InvalidDataException>(() =>
            SupplementalSynchronizationCodec.EncodeRoster(roster with
            {
                Characters = [roster.Characters[0] with { Appearances = null! }],
            }));
    }

    /// <summary>
    /// An entry's fixed part: the slot, the name's length prefix, and the sixteen bytes after the
    /// name. The name's own bytes, the parts and the tail are counted separately.
    /// </summary>
    private const int RosterEntryFixedWidth = 22;

    private const int RosterHeaderWidth = 18;

    private const int RosterAppearanceWidth = 23;

    private const int RosterEntryTailWidth = 36;

    /// <summary>
    /// The widths close here too: header, entry, twenty-three per appearance, thirty-six of tail.
    /// </summary>
    /// <remarks>
    /// This encoder writes the same subtype as the roster screen's, so the entry tail exists twice
    /// in the repository and both had to move from thirty-two bytes to thirty-six. Only the roster
    /// screen's copy was guarded, which meant this one could be reverted with the whole suite
    /// still green -- and the shape it protects is precisely the one a single character cannot
    /// show, because a short tail loses the second character rather than mangling the first.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SelectCharacterSupplemental_RosterLengthClosesOverTheAppearanceCount(
        int appearances)
    {
        var expected = RosterHeaderWidth + RosterEntryFixedWidth + 1
            + (RosterAppearanceWidth * appearances) + RosterEntryTailWidth;
        var appearance = new CharacterRosterAppearanceState(7, 1000, 4, [1, 2, 3, 4], 8, 2000, 9, 10);
        var roster = new CharacterRosterEntryState(
            24, 24, 2, 100, 3, 200,
            [new CharacterRosterMemberState(
                new CharacterId(10), 1, [0x41], 2, 3, 86, 4, 5, 6,
                [.. Enumerable.Repeat(appearance, appearances)],
                3000)]);

        Assert.Equal(expected, Body(SupplementalSynchronizationCodec.EncodeRoster(roster)).Length);
    }

    /// <summary>
    /// A second character begins where the first one ends, which is what the tail length buys.
    /// </summary>
    [Fact]
    public void SelectCharacterSupplemental_ASecondCharacterStartsWhereTheFirstEnds()
    {
        var appearance = new CharacterRosterAppearanceState(7, 1000, 4, [1, 2, 3, 4], 8, 2000, 9, 10);
        var first = new CharacterRosterMemberState(
            new CharacterId(10), 1, [0x41], 2, 3, 86, 4, 5, 6, [appearance], 3000);
        var roster = new CharacterRosterEntryState(
            24, 24, 2, 100, 3, 200,
            [first, first with { CharacterId = new CharacterId(11), SlotIndex = 2, Name = [0x42] }]);

        var body = Body(SupplementalSynchronizationCodec.EncodeRoster(roster));

        var entryLength = RosterEntryFixedWidth + 1 + RosterAppearanceWidth + RosterEntryTailWidth;
        Assert.Equal(RosterHeaderWidth + (entryLength * 2), body.Length);
        Assert.Equal(0x42, body[RosterHeaderWidth + entryLength + 6]);
    }

    private static byte[] EncodeStaticNotification(SelectCharacterResult result, ushort type)
    {
        Assert.Equal((ushort)0x00FB, type);
        return StateSynchronizationPayloadCodec.EncodeEquipmentLocks(
            result.CharacterInventory.EquipmentLocks,
            result.AccountInventory.Cargo.EquipmentLocks);
    }

    private static ushort Type(byte[] frame) => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1));

    private static byte[] Body(byte[] frame) => frame[GameServerFrameEncoder.HeaderLength..];

    private static ushort Type(GameMessagePayload payload) => payload.Type;

    private static byte[] Body(GameMessagePayload payload) => payload.Body.ToArray();

    private static PvpParticipantEntryState Identity(ushort userId, byte marker)
    {
        var result = SelectCharacterPayloadTestData.CreateComplete();
        var character = result.Character with
        {
            Identity = result.Character.Identity with
            {
                CharacterId = new CharacterId(userId),
                Name = [marker],
            },
        };
        var equipment = result.CharacterInventory.Containers
            .Single(value => value.ContainerType == Dfo.Server.Data.Inventory.InventoryContainerType.Equipment)
            .Items;
        return new(character, result.CharacterEntry.Public, equipment, result.CharacterEntry.Features.Pets);
    }
}
