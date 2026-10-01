using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Networking.Tests.Integration;

public sealed class RecipeCraftRegistrationTests
{
    [Fact]
    public void Captured_design_drawing_request_is_understood_by_the_game_endpoint()
    {
        var services = new ServiceCollection().AddLogging();
        var configuration = new ConfigurationBuilder().Build();
        new NetworkingModule().ConfigureServices(services, configuration);
        new ProtocolModule().ConfigureServices(services, configuration);
        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetServices<GameMessageDecoderRegistration>()
            .Where(x => x.Selector == 1 && x.Type == 0x0019);
        var decoder = new GameMessageDecoderRegistry(registrations, provider).Find(1, 0x0019);
        Assert.NotNull(decoder);
        Assert.NotNull(decoder.Decode(GameMessageMetadata.Empty,
            Convert.FromHexString("650000000001000001")));
    }
}
