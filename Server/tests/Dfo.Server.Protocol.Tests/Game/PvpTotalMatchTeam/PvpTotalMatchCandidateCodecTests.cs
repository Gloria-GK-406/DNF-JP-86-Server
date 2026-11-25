using System.Buffers.Binary;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Protocol.Game.PvpTotalMatchTeam;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;

namespace Dfo.Server.Protocol.Tests.Game.PvpTotalMatchTeam;

public sealed class PvpTotalMatchCandidateCodecTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A21_reads_appearance_and_equipment_before_skills_and_the_next_candidate(bool withAppearance)
    {
        var seed = SelectCharacterPayloadTestData.CreateComplete();
        var baseline = seed.CharacterEntry.Public;
        var career = new CharacterCareer(1);
        CharacterRosterAppearanceState[] parts =
        [
            new(9, 2120, 4, [5, 0, 3, 0], 7, 2100, 0, 0),
            new(12, 12700, 4, [0, 0, 0, 0], 1, 0, 0, 2),
        ];
        var state = baseline with
        {
            Display = withAppearance
                ? new(new(career), career, false, false, false, 0, 81956, parts)
                : null,
            CloneTitleItemId = 81956,
            Extras = baseline.Extras with { SpecialRewardQuestIds = [307] },
        };
        PvpTotalMatchCandidate Candidate(byte name) => new(
            seed.Character with { Identity = seed.Character.Identity with { Name = [name] } },
            state, [], new CharacterSkillPageState(1, [new(7, 101, 2, [])]));

        var frame = Assert.Single(new PvpTotalMatchCharacterInfoEncoder()
            .Encode(new PvpTotalMatchCharacterInfo(11, [Candidate(65), Candidate(66), Candidate(67)], seed.World.PreparedAt)));
        Assert.Equal(0x031A, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        using var stream = new MemoryStream(frame) { Position = 15 };
        using var reader = new BinaryReader(stream);
        Assert.Equal(1, reader.ReadByte());
        Assert.Equal(11, reader.ReadUInt16());
        Assert.Equal(3, reader.ReadByte());

        for (var index = 0; index < 3; index++)
        {
            Assert.Equal(index, reader.ReadByte());
            Assert.Equal(1, reader.ReadInt32());
            Assert.Equal(65 + index, reader.ReadByte());
            Assert.Equal(3, reader.ReadBytes(3).Length);
            Assert.Equal(88, reader.ReadInt32());
            Assert.Equal(88, reader.ReadBytes(88).Length);
            Assert.Equal(state.Extras.ExtraEquipmentSlotState, reader.ReadByte());
            Assert.Equal(withAppearance ? parts.Length : 0, reader.ReadByte());
            if (withAppearance)
            {
                foreach (var part in parts)
                {
                    Assert.Equal(part.Slot, reader.ReadByte());
                    Assert.Equal(part.DisplayItemId, reader.ReadInt32());
                    Assert.Equal(4, reader.ReadInt32());
                    Assert.Equal(part.ExpansionData, reader.ReadBytes(4));
                    Assert.Equal(part.State, reader.ReadByte());
                    Assert.Equal(part.LinkItemId, reader.ReadInt32());
                    if (part.Slot == 9)
                    {
                        Assert.Equal(4, reader.ReadInt32());
                        Assert.Equal(part.ExpansionData, reader.ReadBytes(4));
                    }
                    Assert.Equal(part.EnchantValue, reader.ReadUInt32());
                    Assert.Equal(part.Flag20, reader.ReadByte());
                }
            }
            Assert.Equal(8, reader.ReadBytes(8).Length);
            Assert.Equal(0, reader.ReadByte());
            // 0x01183318 reads this even when the equipment count is zero.
            Assert.Equal(81956u, reader.ReadUInt32());
            Assert.Equal(0, reader.ReadUInt16());
            Assert.Equal(1, reader.ReadByte());
            Assert.Equal(1, reader.ReadByte());
            Assert.Equal(7, reader.ReadByte());
            Assert.Equal(101, reader.ReadUInt16());
            Assert.Equal(2, reader.ReadByte());
            Assert.Equal(1u, reader.ReadUInt32());
            Assert.Equal(307u, reader.ReadUInt32());
            Assert.Equal(28, reader.ReadBytes(28).Length);
        }

        Assert.Equal(stream.Length, stream.Position);
    }
}
