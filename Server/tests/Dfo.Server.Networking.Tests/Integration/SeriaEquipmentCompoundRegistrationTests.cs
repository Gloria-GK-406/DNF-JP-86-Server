using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class SeriaEquipmentCompoundRegistrationTests
{
    [Fact]
    public void Native_single_equipment_request_is_routed_by_the_game_endpoint()
    {
        var services = new ServiceCollection().AddLogging();
        var configuration = new ConfigurationBuilder().Build();
        new NetworkingModule().ConfigureServices(services, configuration);
        new ProtocolModule().ConfigureServices(services, configuration);
        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetServices<GameMessageDecoderRegistration>().Where(x => x.Selector == 1 && x.Type == 0x0197);
        var decoder = new GameMessageDecoderRegistry(registrations, provider).Find(1, 0x0197);
        Assert.NotNull(decoder);
        var message = decoder.Decode(GameMessageMetadata.Empty, Convert.FromHexString("090001000700"));
        Assert.NotNull(message);
        Assert.Equal((ClientMessageType)0x0001_0197, message.MessageType);
    }
}
