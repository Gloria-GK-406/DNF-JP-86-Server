using System.Buffers.Binary;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game.AccountRoster;

public sealed class OtherCharacterDetailPayloadCodecTests
{
    [Fact]
    public void EmptyProjectionMatchesTheA21SubtypeThreeOffsets()
    {
        var source = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
        var publicState = source.CharacterEntry.Public with
        {
            DerivedStats = ZeroStats(),
            Extras = source.CharacterEntry.Public.Extras with
            {
                ManageLevel = 0,
                CharacterExperience = 0,
                ExtraEquipmentSlotState = 0,
                SkillTreeIndex = 0,
                NameTagItemId = 0,
                NameTagExpireTime = 0,
                SpecialRewardQuestIds = [],
            },
            Guild = new CharacterGuildStandingStorageRecord(0, 0),
            CloneTitleItemId = 0,
        };
        var skills = source.CharacterEntry.Skills with { Pages = [] };

        var body = StateSynchronizationPayloadCodec.EncodeOtherCharacterDetail(
            source.Character,
            publicState,
            skills,
            [],
            source.World.PreparedAt);

        Assert.Equal(165, body.Length);
        Assert.Equal((byte)3, body[0]);
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(1)));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(3)));
        Assert.All(body[5..10], value => Assert.Equal((byte)0, value));
        Assert.Equal(
            checked((ushort)source.Character.Identity.CharacterId.Value),
            BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(10)));
        Assert.Equal(88, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(16)));
        Assert.Equal((ushort)5500, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(76)));
        Assert.Equal((uint)100, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(102)));
        Assert.Equal((byte)0, body[108]);
        Assert.Equal((byte)0, body[109]);
        Assert.Equal((byte)0, body[123]);
        Assert.Equal((byte)0, body[124]);
        Assert.Equal((byte)0x6F, body[159]);
        Assert.Equal((uint)0, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(160)));
        Assert.Equal((byte)0, body[164]);
    }

    [Fact]
    public void BothSkillPagesGuildLevelAndSpecialRewardQuestsAreProjected()
    {
        var source = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
        var pages = new[]
        {
            new CharacterSkillPageState(0, [new CharacterSkillState(0, 179, 7, [])]),
            new CharacterSkillPageState(1, [new CharacterSkillState(0, 174, 1, [])]),
        };
        var publicState = source.CharacterEntry.Public with
        {
            DerivedStats = ZeroStats(),
            Extras = source.CharacterEntry.Public.Extras with
            {
                ManageLevel = 6,
                CharacterExperience = 1_046_561_640,
                ExtraEquipmentSlotState = 3,
                SkillTreeIndex = -1,
                NameTagItemId = 0,
                NameTagExpireTime = 0,
                SpecialRewardQuestIds = [0x34BE, 0x34C0],
            },
            Guild = new CharacterGuildStandingStorageRecord(7, 0),
            CloneTitleItemId = 0,
        };
        var skills = source.CharacterEntry.Skills with { Pages = pages };

        var body = StateSynchronizationPayloadCodec.EncodeOtherCharacterDetail(
            source.Character,
            publicState,
            skills,
            [],
            source.World.PreparedAt);

        Assert.Equal((ushort)6, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(3)));
        Assert.Equal((uint)1_046_561_640, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12)));
        Assert.Equal((byte)3, body[108]);
        Assert.Equal((byte)0xFF, body[122]);
        Assert.Equal((byte)1, body[123]);
        Assert.Equal((ushort)179, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(124)));
        Assert.Equal((byte)7, body[126]);
        Assert.Equal((byte)1, body[127]);
        Assert.Equal((ushort)174, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(128)));
        Assert.Equal((byte)1, body[130]);

        // Two 3-byte skill entries move the fixed post-skill context from 126 to 132.
        var guildLengthOffset = 132 + 27;
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(guildLengthOffset)));
        Assert.Equal((byte)7, body[guildLengthOffset + 4]);
        Assert.Equal((byte)0x6F, body[guildLengthOffset + 6]);
        Assert.Equal((uint)2, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(guildLengthOffset + 7)));
        Assert.Equal((uint)0x34BE, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(guildLengthOffset + 11)));
        Assert.Equal((uint)0x34C0, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(guildLengthOffset + 15)));
        Assert.Equal((byte)0, body[guildLengthOffset + 19]);
        Assert.Equal(guildLengthOffset + 20, body.Length);
    }

    private static CharacterDerivedStatsStorageRecord ZeroStats() => new(
        HpMaximum: 0,
        MpMaximum: 0,
        PhysicalAttack: 0,
        PhysicalDefense: 0,
        MagicalAttack: 0,
        MagicalDefense: 0,
        MoveSpeed: 0,
        AttackSpeed: 0,
        CastSpeed: 0,
        InventoryLimit: 0,
        Progress1: 0,
        Progress2: 0);
}
