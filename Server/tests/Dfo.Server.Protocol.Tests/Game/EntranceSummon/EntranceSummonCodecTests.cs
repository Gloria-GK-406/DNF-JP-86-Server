using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.EntranceSummon;

namespace Dfo.Server.Protocol.Tests.Game.EntranceSummon;

/// <summary>Bytes of the boss room entrance condition frames (#357), as the A21 client reads and builds them.</summary>
[Trait("Category", "Unit")]
public sealed class EntranceSummonCodecTests
{
    [Fact]
    public void The_summon_request_reads_the_clients_nineteen_bytes()
    {
        byte[] body =
        [
            0x00, 0x00,             // kind: monster
            0x90, 0x0E, 0x01, 0x00, // code 69264
            0x07, 0x00, 0x00, 0x00, // request id 7
            0xD6, 0x42, 0x00, 0x00, // map 17110
            0x01, 0x00, 0x01, 0x00, // cell (1,1)
            0x01,                   // count
        ];
        Assert.Equal(new DungeonEntranceSummonRequest(0, 69264, 7, 17110, 1, 1, 1), EntranceSummonPayloadCodec.Decode(body));
        Assert.Throws<InvalidDataException>(() => EntranceSummonPayloadCodec.Decode(body.AsSpan(0, 18)));
    }

    [Fact]
    public void An_accepted_summon_echoes_the_request_and_lists_each_actor()
    {
        var bytes = EntranceSummonPayloadCodec.Encode(new EntranceSummonResponse(7, [new(0x42DD, 69264, 3, 29)]));
        Assert.Equal(
            new byte[] { 0x01, 0x07, 0x00, 0x00, 0x00, 0x01, 0xDD, 0x42, 0x90, 0x0E, 0x01, 0x00, 0x03, 0x1D, 0x00 },
            bytes);
    }

    [Fact]
    public void A_refused_summon_returns_the_request_id_after_the_refusal()
    {
        Assert.Equal(new byte[] { 0x00, EntranceSummonPayloadCodec.RefusalReason, 0x07, 0x00, 0x00, 0x00 },
            EntranceSummonPayloadCodec.Encode(new EntranceSummonResponse(7, [])));
    }

    [Fact]
    public void The_icon_frame_lists_each_target_cell_and_code()
    {
        var payload = new DungeonEntranceTargetIconsEncoder().Encode(new DungeonEntranceTargetIconsNotification(
            [new(56611, new MazeCell(1, 1), 17110), new(56617, new MazeCell(3, 2), 17116)]));
        Assert.Equal(new byte[] { 0x02, 0x00, 0x01, 0x01, 0x23, 0xDD, 0x00, 0x00, 0x03, 0x02, 0x29, 0xDD, 0x00, 0x00 },
            Assert.Single(payload)[15..]);
    }

    [Fact]
    public void The_condition_passed_frame_is_five_zero_bytes()
    {
        var payload = new DungeonEntranceConditionPassedEncoder().Encode(new DungeonEntranceConditionPassedNotification());
        Assert.Equal(new byte[5], Assert.Single(payload)[15..]);
    }
}
