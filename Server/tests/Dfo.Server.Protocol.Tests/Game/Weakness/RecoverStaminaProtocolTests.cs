using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.RecoverStamina;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.Weakness;

public sealed class RecoverStaminaProtocolTests
{
    [Fact]
    public void Gold_request_is_registered_as_cmd_0009_and_preserves_mode()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            item => item.Selector == 1 && item.Type == 9);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);
        var request = Assert.IsType<RecoverStaminaRequest>(decoder.Decode(GameMessageMetadata.Empty, new byte[] { 0, 0 }));
        Assert.Equal(ClientMessageType.GameRecoverStamina, request.MessageType);
        Assert.Equal(0, request.Mode);
        Assert.Equal(1, Assert.IsType<RecoverStaminaRequest>(decoder.Decode(GameMessageMetadata.Empty, new byte[] { 1, 0 })).Mode);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, new byte[] { 0 }));
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, new byte[] { 0, 0, 0 }));
    }

    [Theory]
    [InlineData(0, "01")]
    [InlineData(18, "0012")]
    [InlineData(22, "0016")]
    [InlineData(4, "0004")]
    public void Success_and_rejections_encode_the_client_ack_contract(byte error, string body)
    {
        var frame = Assert.Single(new RecoverStaminaEncoder().Encode(new RecoverStaminaResponse(error)));
        Assert.Equal(1, frame[0]);
        Assert.Equal(9, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        Assert.Equal(Convert.FromHexString(body), frame[15..]);
    }
}
