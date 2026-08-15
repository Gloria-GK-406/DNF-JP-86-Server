using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol.Game.Exit;
using Dfo.Server.Protocol.Game.Login;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Networking.Tests.Runtime;

public sealed class ServerMessageEncoderRegistryTests
{
    [Fact]
    public void ResolvesTheEncoderAndEndpointRegisteredForAMessage()
    {
        var registry = Build(services =>
            services.AddServerMessageEncoder<GameExitResponse, StubExitEncoder>(NetworkEndpointKind.Game));

        var entry = registry.Find(typeof(GameExitResponse));

        Assert.NotNull(entry);
        Assert.IsType<StubExitEncoder>(entry.Encoder);
        Assert.Equal(NetworkEndpointKind.Game, entry.Endpoint);
    }

    /// <summary>
    /// The outbound key space is closed, so a miss is a wiring mistake rather than ordinary
    /// traffic. The registry only reports it; the sender is what turns it into a failure.
    /// </summary>
    [Fact]
    public void ReportsAMissForAnUnregisteredMessage()
    {
        var registry = Build(services =>
            services.AddServerMessageEncoder<GameExitResponse, StubExitEncoder>(NetworkEndpointKind.Game));

        Assert.Null(registry.Find(typeof(GameLoginResponse)));
    }

    [Fact]
    public void RejectsTwoEncodersForTheSameMessage()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Build(services =>
        {
            services.AddServerMessageEncoder<GameExitResponse, StubExitEncoder>(NetworkEndpointKind.Game);
            services.AddServerMessageEncoder<GameExitResponse, OtherStubExitEncoder>(NetworkEndpointKind.Game);
        }));

        Assert.Contains(nameof(GameExitResponse), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedEncoderRejectsAMessageItDoesNotEncode()
    {
        IServerMessageEncoder encoder = new StubExitEncoder();

        var exception = Assert.Throws<InvalidOperationException>(
            () => encoder.Encode(new GameLoginResponse(true)));

        Assert.Contains(nameof(StubExitEncoder), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(GameLoginResponse), exception.Message, StringComparison.Ordinal);
    }

    private static ServerMessageEncoderRegistry Build(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        services.AddSingleton<ServerMessageEncoderRegistry>();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ServerMessageEncoderRegistry>();
    }

    private sealed class StubExitEncoder : ServerMessageEncoder<GameExitResponse>
    {
        protected override IReadOnlyList<byte[]> EncodeMessage(GameExitResponse message) => [[0x01]];
    }

    private sealed class OtherStubExitEncoder : ServerMessageEncoder<GameExitResponse>
    {
        protected override IReadOnlyList<byte[]> EncodeMessage(GameExitResponse message) => [[0x02]];
    }
}
