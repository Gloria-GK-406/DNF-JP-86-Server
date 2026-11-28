using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.LuckyCharacter;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;

namespace Dfo.Server.Protocol.Tests.Game.LuckyCharacter;

[Trait("Category", "Unit")]
public sealed class LuckyCharacterCodecTests
{
    [Fact]
    public void Returning_to_town_reprojects_the_current_period_after_town_state()
    {
        var seed = SelectCharacterPayloadTestData.CreateComplete();
        var session = Dfo.Server.Data.World.SelectedSessionIdentity.Create(seed.Account.AccountId,
            seed.Character.Identity.CharacterId, new("lucky-town-test"), 1, 10010);
        var stopped = seed with
        {
            Events = Dfo.Server.Data.Events.EventPlayerSnapshot.Empty with
            {
                LuckyCharacters = new(3, []),
                LuckyFatigueState = 0,
            },
        };
        var frames = TownReturnProjection.Frames(session, stopped, seed.Character.Position);
        Assert.Equal(new byte[4], Assert.IsType<LuckyCharacterListMessage>(frames[^3]).EncodeBody());
        Assert.IsType<SkillStateMessage>(frames[^2]);
        Assert.Equal(new byte[3], Assert.IsType<FatigueAccelerationStateMessage>(frames[^1]).EncodeBody());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Entry_projects_the_list_before_skill_recalculation_and_fatigue_state(byte state)
    {
        var seed = SelectCharacterPayloadTestData.CreateComplete();
        var configured = seed with
        {
            Events = Dfo.Server.Data.Events.EventPlayerSnapshot.Empty with
            {
                LuckyCharacters = new(2, state == 0 ? [] : [seed.Character.Identity.Job]),
                LuckyFatigueState = state,
            },
        };
        var baseline = SelectedCharacterStateMessageFactory.Create(seed, FriendListRefresh.None);
        var entry = SelectedCharacterStateMessageFactory.Create(configured, FriendListRefresh.None);
        Assert.Empty(baseline.OfType<LuckyCharacterListMessage>());
        var types = entry.Select(message => message.Type).ToArray();
        Assert.Equal(Array.IndexOf(types, (ushort)0x0013) - 1, Array.IndexOf(types, (ushort)0x0406));
        Assert.True(Array.IndexOf(types, (ushort)0x0013) < Array.IndexOf(types, (ushort)0x01EB));
        Assert.Equal(Assert.Single(baseline.OfType<SkillStateMessage>()).EncodeBody(),
            Assert.Single(entry.OfType<SkillStateMessage>()).EncodeBody());
        Assert.Equal(state, Assert.Single(entry.OfType<FatigueAccelerationStateMessage>()).State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void List_uses_little_endian_rows_for_every_stage_and_can_clear(int jobs)
    {
        var notice = new LuckyCharacterListMessage(new(7, [.. Enumerable.Range(10, jobs)]));
        var body = notice.EncodeBody();
        Assert.Equal((ushort)0x0406, notice.Type);
        Assert.Equal(4 + 8 * jobs * 15, body.Length);
        Assert.Equal(jobs * 15, BinaryPrimitives.ReadInt32LittleEndian(body));
        for (var row = 0; row < jobs * 15; row++)
        {
            var offset = 4 + row * 8;
            Assert.Equal(10 + row / 15, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(offset)));
            Assert.Equal(((row % 15 / 5) << 4) | (row % 5),
                BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(offset + 4)));
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Client_sends_the_desired_state(byte value, bool enabled)
    {
        var decoder = new LuckyCharacterBurningDecoder();
        var request = Assert.IsType<LuckyCharacterBurningRequest>(decoder.Decode(GameMessageMetadata.Empty, [value]));
        Assert.Equal(enabled, request.Enabled);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, []));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [2]));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [0, 0]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Fatigue_notice_has_a_zero_threshold_and_one_state_byte(byte state) =>
        Assert.Equal(new byte[] { 0, 0, state }, new FatigueAccelerationStateMessage(0, state).EncodeBody());
}
