using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Networking.Tests.Runtime;

public sealed class ClientMessageDecoderRegistryTests
{
    [Fact]
    public void GameRegistry_ResolvesTheDecoderRegisteredForAWireKey()
    {
        var registry = BuildGameRegistry(services =>
            services.AddGameMessageDecoder<StubGameDecoder>(0x01, 0x1234, ClientMessageType.GameLogin));

        Assert.IsType<StubGameDecoder>(registry.Find(0x01, 0x1234));
    }

    /// <summary>
    /// The wire key space is open, so an unregistered key is ordinary traffic rather than a fault.
    /// The registry reports a miss and leaves the drop decision to the caller.
    /// </summary>
    [Fact]
    public void GameRegistry_ReportsAMissForAnUnregisteredWireKey()
    {
        var registry = BuildGameRegistry(services =>
            services.AddGameMessageDecoder<StubGameDecoder>(0x01, 0x1234, ClientMessageType.GameLogin));

        Assert.Null(registry.Find(0x01, 0x9999));
        Assert.Null(registry.Find(0x02, 0x1234));
    }

    [Fact]
    public void GameRegistry_RejectsTwoDecodersOnTheSameWireKey()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => BuildGameRegistry(services =>
        {
            services.AddGameMessageDecoder<StubGameDecoder>(0x01, 0x1234, ClientMessageType.GameLogin);
            services.AddGameMessageDecoder<OtherStubGameDecoder>(0x01, 0x1234, ClientMessageType.GameExit);
        }));

        Assert.Contains("0x01/0x1234", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChannelRegistry_ResolvesAndReportsMissesByMessageNumber()
    {
        var registry = BuildChannelRegistry(services =>
            services.AddChannelMessageDecoder<StubChannelDecoder>(0x42, ClientMessageType.ChannelConnect));

        Assert.IsType<StubChannelDecoder>(registry.Find(0x42));
        Assert.Null(registry.Find(0x43));
    }

    [Fact]
    public void ChannelRegistry_RejectsTwoDecodersOnTheSameMessageNumber()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => BuildChannelRegistry(services =>
        {
            services.AddChannelMessageDecoder<StubChannelDecoder>(0x42, ClientMessageType.ChannelConnect);
            services.AddChannelMessageDecoder<OtherStubChannelDecoder>(0x42, ClientMessageType.ChannelInfo);
        }));

        Assert.Contains("0x42", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Registration declares the produced catalogue members alongside the wire key. Without that
    /// declaration the startup coverage check has nothing to close over.
    /// </summary>
    [Fact]
    public void Registration_DeclaresEveryProducedMessageType()
    {
        var services = new ServiceCollection();
        services.AddGameMessageDecoder<StubGameDecoder>(
            0x01, 0x1234, ClientMessageType.GameLogin, ClientMessageType.GameExit);

        var productions = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ClientMessageProductionRegistration>()
            .ToArray();

        Assert.Equal(2, productions.Length);
        Assert.All(productions, production => Assert.Equal(typeof(StubGameDecoder), production.DecoderType));
        Assert.Contains(productions, production => production.MessageType == ClientMessageType.GameLogin);
        Assert.Contains(productions, production => production.MessageType == ClientMessageType.GameExit);
    }

    [Fact]
    public void Registration_RejectsADecoderThatDeclaresNoProducedMessageType()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(
            () => services.AddGameMessageDecoder<StubGameDecoder>(0x01, 0x1234));

        Assert.Contains(nameof(StubGameDecoder), exception.Message, StringComparison.Ordinal);
    }

    private static GameMessageDecoderRegistry BuildGameRegistry(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        services.AddSingleton<GameMessageDecoderRegistry>();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<GameMessageDecoderRegistry>();
    }

    private static ChannelMessageDecoderRegistry BuildChannelRegistry(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        services.AddSingleton<ChannelMessageDecoderRegistry>();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ChannelMessageDecoderRegistry>();
    }

    private sealed class StubGameDecoder : IGameMessageDecoder
    {
        public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => null;
    }

    private sealed class OtherStubGameDecoder : IGameMessageDecoder
    {
        public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => null;
    }

    private sealed class StubChannelDecoder : IChannelMessageDecoder
    {
        public IClientNetworkMessage? Decode(ChannelMessageMetadata metadata, ReadOnlySpan<byte> body) => null;
    }

    private sealed class OtherStubChannelDecoder : IChannelMessageDecoder
    {
        public IClientNetworkMessage? Decode(ChannelMessageMetadata metadata, ReadOnlySpan<byte> body) => null;
    }
}
