using System.Collections.Immutable;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.BossTowerInfo;
using Dfo.Server.Protocol.Game.BossTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.BossTowerReward;
using Dfo.Server.Protocol.Game.IllusionTowerStage;
using Dfo.Server.Protocol.Game.BossTowerContinue;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.BossTower;

public sealed class BossTowerCodecTests
{
    [Fact]
    public void Info_and_map_match_the_client_static_layout()
    {
        Assert.Equal("B10D000008000000", Convert.ToHexString(
            BossTowerInfoPayloadCodec.Encode(3505, 8)));
        var room = new TowerRoomSnapshot(9, 31011, 0x12345678, 7,
            TowerRoomPhase.Loading,
            [new TowerActor(33, new TowerActorSeed(4, 15402, 40, 5, true,
                new TowerPosition(1, 2, 3)), false)], [],
            ImmutableHashSet<CharacterId>.Empty);

        var body = BossTowerMapPayloadCodec.Encode(room);

        Assert.Equal(26, body.Length);
        Assert.Equal((ushort)9, BinaryPrimitives.ReadUInt16LittleEndian(body));
        Assert.Equal(0x12345678u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(2)));
        Assert.Equal(31011u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(6)));
        Assert.Equal(1, body[10]);
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(11)));
        Assert.Equal((ushort)33, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(15)));
        Assert.Equal(15402u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(17)));
        Assert.Equal(new byte[] { 40, 5, 0, 0, 0 }, body[21..]);
    }

    [Fact]
    public void Reward_preserves_eight_sparse_party_slots_and_candidate_display_values()
    {
        var slots = new Dictionary<byte, ImmutableArray<TowerRewardCandidate>>
        {
            [0] = [new(-1, 0), new(0, 345)],
            [2] = [new(3037, unchecked((int)0xAABBCCDD))],
        };

        var body = BossTowerRewardPayloadCodec.Encode(777, slots);

        Assert.Equal("0903000002FFFFFFFF0000000000000000590100000001DD0B0000DDCCBBAA0000000000", Convert.ToHexString(body));
    }

    [Theory]
    [InlineData(false, "00")]
    [InlineData(true, "01")]
    public void Registered_continue_encoder_preserves_the_boolean_byte(bool eligible, string expected)
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = provider.GetServices<ServerMessageEncoderRegistration>()
            .Single(value => value.MessageType == typeof(BossTowerContinueNotification));
        var encoder = (IServerMessageEncoder)provider.GetRequiredService(registration.EncoderType);

        var frame = Assert.Single(encoder.Encode(new BossTowerContinueNotification(eligible)));

        Assert.Equal((ushort)0x0336, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(expected, Convert.ToHexString(frame.AsSpan(15)));
    }
}
