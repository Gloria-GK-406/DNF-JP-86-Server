using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.Weakness;

public sealed class PremiumServiceUsageReportTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void Observed_reports_decode_without_treating_the_block_as_entitlements(byte slot)
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            item => item.Selector == 1 && item.Type == 0x036F);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);
        var body = new byte[76];
        body[0] = 1;
        body[2] = slot;
        var report = Assert.IsType<PremiumServiceUsageReport>(decoder.Decode(GameMessageMetadata.Empty, body));
        Assert.Equal(slot, report.Slot);
        Assert.Equal(ClientMessageType.GamePremiumServiceUsageReport, report.MessageType);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, body[..75]));
        body[75] = 1;
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, body));
        body[75] = 0;
        body[0] = 4; // Internet-cafe path is outside this implementation.
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, body));
        body[0] = 1;
        body[2] = 5;
        Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, body));
    }
}
