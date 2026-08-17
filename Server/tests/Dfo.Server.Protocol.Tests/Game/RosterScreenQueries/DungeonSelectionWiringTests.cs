using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.RosterScreenQueries;

/// <summary>
/// That the dungeon-selection notifications can actually reach the wire.
/// </summary>
/// <remarks>
/// Written because the first live run logged the handler doing its work and produced no frames at
/// all. Encoding is resolved by CLR type at send time, so a message with no registration fails only
/// at runtime -- and the one place that would have reported it, the dispatch-layer catch, is known
/// not to fire for reasons still unexplained. This test asks the composition root the question
/// directly instead.
/// </remarks>
public sealed class DungeonSelectionWiringTests
{
    private static ServerMessageEncoderRegistration Registration<TMessage>()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        return services
            .Where(value => value.ServiceType == typeof(ServerMessageEncoderRegistration))
            .Select(value => (ServerMessageEncoderRegistration)value.ImplementationInstance!)
            .Single(value => value.MessageType == typeof(TMessage));
    }

    private static byte[] Encode<TMessage>(TMessage message)
        where TMessage : class, IServerNetworkMessage
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Registration<TMessage>();
        var encoder = (IServerMessageEncoder)provider.GetRequiredService(registration.EncoderType);
        return Assert.Single(encoder.Encode(message));
    }

    [Fact]
    public void EachNotificationIsRegisteredForTheGameEndpoint()
    {
        foreach (var registration in new[]
                 {
                     Registration<UserAreaNotification>(),
                     Registration<UserStateNotification>(),
                     Registration<UdpHostNotification>(),
                     Registration<EnterSelectDungeonNotification>(),
                 })
        {
            Assert.Equal(NetworkEndpointKind.Game, registration.Endpoint);
        }
    }

    [Theory]
    [InlineData(0x0017)]
    [InlineData(0x0003)]
    [InlineData(0x001A)]
    [InlineData(0x001B)]
    public void EachNotificationFramesAsANotiOfItsOwnType(int expectedType)
    {
        var spawn = new EntrySpawnState(1, 0, 474, 234, 5, 3, false);
        byte[] frame = expectedType switch
        {
            0x0017 => Encode(new UserAreaNotification(13, spawn, 0xFF)),
            0x0003 => Encode(new UserStateNotification(13, 1)),
            0x001A => Encode(new UdpHostNotification()),
            _ => Encode(new EnterSelectDungeonNotification(13, 1)),
        };

        // Server frame header: selector, u16 type, i32 total length.
        Assert.Equal(0x00, frame[0]);
        Assert.Equal(expectedType, BitConverter.ToUInt16(frame, 1));
        Assert.Equal(frame.Length, BitConverter.ToInt32(frame, 3));
    }
}
