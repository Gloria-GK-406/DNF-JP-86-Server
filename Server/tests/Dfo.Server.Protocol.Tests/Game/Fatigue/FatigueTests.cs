using System.Buffers.Binary;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Protocol.Game.Fatigue;
namespace Dfo.Server.Protocol.Tests.Game.Fatigue;
public sealed class FatigueTests
{
    [Fact]
    public void Login_uses_the_same_three_fields_as_incremental_fatigue()
    {
        var sample = SelectCharacterPayloadTestData.CreateComplete();
        var body = StateSynchronizationPayloadCodec.EncodeEntryAccepted(
            sample.Character with { Fatigue = new CharacterFatigueState(188, 138) { ConsumedToday = 188 } },
            sample.Account, sample.CharacterEntry.Quests, 1, sample.World.PreparedAt);
        Assert.Equal((ushort)138, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(11)));
        Assert.Equal((ushort)188, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(13)));
        Assert.Equal((ushort)188, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(15)));
    }

    [Fact]
    public void A21_projection_is_ten_bytes_and_cumulative_is_not_the_current_balance()
    {
        Assert.Equal(new byte[] { 138, 0, 188, 0, 188, 0, 0, 0, 0, 0 },
            FatigueEncoder.Body(new CharacterFatigueBalance(188, 138) { ConsumedToday = 188 }));
        Assert.Equal(new byte[] { 0, 0, 188, 0, 255, 255, 0, 0, 0, 0 },
            FatigueEncoder.Body(new CharacterFatigueBalance(188, 0) { ConsumedToday = 70000 }));
    }
}
