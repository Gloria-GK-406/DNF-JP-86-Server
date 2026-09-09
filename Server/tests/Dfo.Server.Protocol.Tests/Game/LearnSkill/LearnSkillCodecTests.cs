using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.LearnSkill;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.LearnSkill;

public sealed class LearnSkillCodecTests
{
    [Theory]
    [InlineData("00010900000100CBB10216", "9:1")]
    [InlineData("000309000001070000010C00000700AB8A8960", "9:1,7:1,12:7")]
    [InlineData("0001090000080087DFD5B3", "9:8")]
    [InlineData("00010100000200BFD63B2A", "1:2")]
    [InlineData("0005070000014F0000010300000101000002BE000001009928437A", "7:1,79:1,3:1,1:2,190:1")]
    public void Captured_learning_requests_reach_a_registered_message(string hex, string expected)
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            value => value.Selector == 1 && value.Type == 0x001d);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);
        var message = Assert.IsType<LearnSkillRequest>(decoder.Decode(GameMessageMetadata.Empty, Convert.FromHexString(hex)));
        Assert.Equal(ClientMessageType.GameLearnSkill, message.MessageType);
        Assert.Equal((byte)0, message.PageIndex);
        Assert.Equal((byte)0, message.Mode);
        Assert.All(message.Changes, change => Assert.Equal((byte)0, change.Refund));
        Assert.Equal(expected, string.Join(",", message.Changes.Select(change => change.SkillId + ":" + change.Increment)));
    }

    [Theory]
    [InlineData("010109000001007AFC61E4", 1, 9, 0, 1, 0)]
    [InlineData("0001090000010135AF6F6C", 0, 9, 0, 1, 1)]
    [InlineData("00010900010100AE9CAB68", 0, 9, 1, 1, 0)]
    [InlineData("00010900000000769AA39D", 0, 9, 0, 0, 0)]
    [InlineData("0001FF01000100FAFE1D50", 0, 511, 0, 1, 0)]
    public void Valid_fields_are_preserved_for_domain_policy(string hex, byte page, ushort id,
        byte refund, byte increment, byte mode)
    {
        var request = Assert.IsType<LearnSkillRequest>(new LearnSkillMessageDecoder()
            .Decode(GameMessageMetadata.Empty, Convert.FromHexString(hex)));
        Assert.Equal(page, request.PageIndex);
        Assert.Equal(mode, request.Mode);
        Assert.Equal(new CharacterSkillLearningChange(id, refund, increment), Assert.Single(request.Changes));
    }

    [Fact]
    public void Every_changed_byte_of_a_capture_and_wrong_lengths_are_rejected()
    {
        var decoder = new LearnSkillMessageDecoder();
        var captured = Convert.FromHexString("00010100000200BFD63B2A");
        for (var index = 0; index < captured.Length; index++)
        {
            var corrupt = captured.ToArray();
            corrupt[index] ^= 1;
            Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, corrupt));
        }
        foreach (var length in new[] { 0, 1, 2, 3, 7, 10, 12 })
            Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, captured.Concat(new byte[2]).Take(length).ToArray()));
    }

    [Theory]
    [InlineData(99, "C1231A0B")]
    [InlineData(100, "")]
    [InlineData(255, "")]
    public void Checksum_presence_follows_the_client_400_byte_boundary(int count, string suffix)
    {
        // Python hashlib independently computes C1231A0B for this 399-byte logical body.
        var logical = new byte[3 + 4 * count];
        logical[1] = (byte)count;
        for (var index = 0; index < count; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(logical.AsSpan(2 + 4 * index), (ushort)(index + 1));
            logical[5 + 4 * index] = 1;
        }
        var wire = logical.Concat(Convert.FromHexString(suffix)).ToArray();
        var decoder = new LearnSkillMessageDecoder();
        var message = Assert.IsType<LearnSkillRequest>(decoder.Decode(GameMessageMetadata.Empty, wire));
        Assert.Equal(count, message.Changes.Count);
        Assert.Equal((ushort)count, message.Changes[^1].SkillId);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, wire.Concat(new byte[4]).ToArray()));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, wire[..^1]));
    }

    [Fact]
    public void Empty_structurally_valid_request_is_preserved_for_domain_rejection()
    {
        var request = Assert.IsType<LearnSkillRequest>(new LearnSkillMessageDecoder().Decode(GameMessageMetadata.Empty,
            Convert.FromHexString("00000024446FAE")));
        Assert.Empty(request.Changes);
    }

    [Fact]
    public void Success_matches_the_historical_A21_frame_and_failure_has_only_two_bytes()
    {
        var session = SelectedSessionIdentity.Create(new(1), new(2), new("codec"), 1, 10011);
        CharacterSkillState[] skills = [new(3, 7, 1, []), new(4, 79, 1, []), new(5, 3, 1, []),
            new(1, 1, 3, []), new(198, 190, 1, [])];
        var receipt = new CharacterSkillLearningReceipt(session, 0, skills, new(35, 800, 0, 7, []));
        Assert.Equal(Convert.FromHexString("010023000000050307000100044F00010005030001000101000300C6BE000100"),
            LearnSkillResponseEncoder.EncodeBody(new(receipt)));
        Assert.Equal(new byte[] { 0, 3 }, LearnSkillResponseEncoder.EncodeBody(new(null)));
    }

    [Fact]
    public void Response_preserves_existing_custom_command_and_uses_the_requested_page_balance()
    {
        var session = SelectedSessionIdentity.Create(new(1), new(2), new("codec"), 1, 10011);
        var receipt = new CharacterSkillLearningReceipt(session, 1, [new(102, 511, 3, [9, 8])],
            new(1, 70000, 2, -3, []));
        Assert.Equal(Convert.FromHexString("0101FFFF00000166FF010301020908"),
            LearnSkillResponseEncoder.EncodeBody(new(receipt)));
    }
}
