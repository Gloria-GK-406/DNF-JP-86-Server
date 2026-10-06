using System.Collections.Immutable;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.IllusionTowerInfo;
using Dfo.Server.Protocol.Game.IllusionTowerMap;
using Dfo.Server.Protocol.Game.IllusionTowerRanking;
using Dfo.Server.Protocol.Game.IllusionTowerReward;
using Dfo.Server.Protocol.Game.IllusionTowerStage;
using Dfo.Server.Protocol.Game.IllusionTowerContinue;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.IllusionTower;

public sealed class IllusionTowerCodecTests
{
    [Fact]
    public void Info_and_map_match_the_client_static_layout()
    {
        Assert.Equal("F92A00000F000000", Convert.ToHexString(
            IllusionTowerInfoPayloadCodec.Encode(11001, 15)));
        var room = new TowerRoomSnapshot(9, 31011, 0x12345678, 7,
            TowerRoomPhase.Loading,
            [new TowerActor(33, new TowerActorSeed(4, 15402, 40, 5, true,
                new TowerPosition(1, 2, 3)), false)], [],
            ImmutableHashSet<CharacterId>.Empty);

        var body = IllusionTowerMapPayloadCodec.Encode(room);

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
    public void Ranking_is_the_complete_305_byte_empty_baseline()
    {
        var body = IllusionTowerRankingPayloadCodec.Encode(11001, 8, 1234);

        Assert.Equal(305, body.Length);
        Assert.Equal(1234, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(1)));
        Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(5)));
        Assert.Equal(11001u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(10)));
        Assert.All(body[14..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void Reward_preserves_four_sparse_party_slots_and_candidate_display_values()
    {
        var slots = new Dictionary<byte, ImmutableArray<TowerRewardCandidate>>
        {
            [0] = [new(-1, 0), new(0, 345)],
            [2] = [new(3037, unchecked((int)0xAABBCCDD))],
        };

        var body = IllusionTowerRewardPayloadCodec.Encode(777, slots);

        Assert.Equal("0903000002FFFFFFFF0000000000000000590100000001DD0B0000DDCCBBAA00", Convert.ToHexString(body));
    }

    [Fact]
    public void Stage_decoder_accepts_only_the_two_evidenced_shapes()
    {
        var decoder = new IllusionTowerStageDecoder();
        var start = Assert.IsType<IllusionTowerStageRequest>(
            decoder.Decode(GameMessageMetadata.Empty, [1]));
        Assert.Equal(1, start.Mode);
        Assert.Empty(start.UnknownClearValues);

        var clearBody = new byte[17];
        clearBody[0] = 2;
        for (ushort index = 0; index < 8; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(clearBody.AsSpan(1 + index * 2), (ushort)(index + 10));
        var clear = Assert.IsType<IllusionTowerStageRequest>(
            decoder.Decode(GameMessageMetadata.Empty, clearBody));
        Assert.Equal(Enumerable.Range(10, 8).Select(value => (ushort)value), clear.UnknownClearValues);

        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [1, 0]));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [2]));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, [3]));
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
            .Single(value => value.MessageType == typeof(IllusionTowerContinueNotification));
        var encoder = (IServerMessageEncoder)provider.GetRequiredService(registration.EncoderType);

        var frame = Assert.Single(encoder.Encode(new IllusionTowerContinueNotification(eligible)));

        Assert.Equal((ushort)0x0092, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(expected, Convert.ToHexString(frame.AsSpan(15)));
    }
}
