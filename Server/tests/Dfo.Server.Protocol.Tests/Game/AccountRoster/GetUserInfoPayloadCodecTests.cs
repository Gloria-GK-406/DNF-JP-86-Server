using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.AccountRoster;

namespace Dfo.Server.Protocol.Tests.Game.AccountRoster;

[Trait("Category", "Unit")]
public sealed class GetUserInfoPayloadCodecTests
{
    // Real captured body (protocol record E-001, 2026-08-14 Windows client): target 0xFFFF
    // (the no-target sentinel) and mode 2, the account-scope roster query.
    private static readonly byte[] ObservedBody = [0xFF, 0xFF, 0x02];

    [Fact]
    public void Decode_MapsTheObservedAccountRosterQuery()
    {
        var metadata = new GameMessageMetadata(0x01, 0x0008, 0xB6BFEB18, 3);

        var message = GetUserInfoPayloadCodec.Decode(metadata, ObservedBody);

        var request = Assert.IsType<GetAccountCharacterRosterRequest>(message);
        Assert.Equal(ClientMessageType.GameGetAccountCharacterRoster, request.MessageType);
        Assert.Equal(metadata, request.Metadata);
    }

    [Fact]
    public void Decode_RejectsBodiesThatAreNotThreeBytes()
    {
        // All five client builder sites write exactly one u16 and one u8, so a longer body is
        // not this message even when its first three bytes would parse.
        Assert.Null(GetUserInfoPayloadCodec.Decode(GameMessageMetadata.Empty, []));
        Assert.Null(GetUserInfoPayloadCodec.Decode(GameMessageMetadata.Empty, [0xFF, 0xFF]));
        Assert.Null(GetUserInfoPayloadCodec.Decode(GameMessageMetadata.Empty, [0xFF, 0xFF, 0x02, 0x00]));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x01)]
    [InlineData(0x04)]
    [InlineData(0xFF)]
    public void Decode_IgnoresModesWithNoTypedMessage(byte mode)
    {
        // Modes 0/1 have no authorized typed flow in this task, and unknown modes must decode to
        // nothing rather than being mistaken for either supported query.
        var message = GetUserInfoPayloadCodec.Decode(GameMessageMetadata.Empty, [0x34, 0x12, mode]);

        Assert.Null(message);
    }

    [Fact]
    public void Decode_MapsModeThreeToAnExactTargetDetailQuery()
    {
        var metadata = new GameMessageMetadata(0x01, 0x0008, 0x01020304, 7);

        var message = GetUserInfoPayloadCodec.Decode(metadata, [0x34, 0x12, 0x03]);

        var request = Assert.IsType<GetOtherCharacterDetailRequest>(message);
        Assert.Equal(ClientMessageType.GameGetOtherCharacterDetail, request.MessageType);
        Assert.Equal((ushort)0x1234, request.TargetWireUserId);
        Assert.Equal(metadata, request.Metadata);
    }

    [Fact]
    public void Decode_RejectsModeThreeWithTheNoTargetSentinel() =>
        Assert.Null(GetUserInfoPayloadCodec.Decode(
            GameMessageMetadata.Empty, [0xFF, 0xFF, 0x03]));

    [Fact]
    public void Decode_RejectsAccountRosterModeCarryingARealTarget()
    {
        // Both mode-2 builder sites push the sentinel as an immediate, and targeted queries are
        // gated to 0..0xFFFE, so mode 2 with a real target cannot come from this client.
        var message = GetUserInfoPayloadCodec.Decode(GameMessageMetadata.Empty, [0x00, 0x00, 0x02]);

        Assert.Null(message);
    }

    [Fact]
    public void NoTargetSentinelMatchesTheObservedBody()
    {
        Assert.Equal(GetUserInfoPayloadCodec.NoTarget, (ushort)(ObservedBody[0] | (ObservedBody[1] << 8)));
        Assert.Equal(GetUserInfoPayloadCodec.BodyLength, ObservedBody.Length);
    }
}
