using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.ReturnToSelection;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.ReturnToSelection;

public sealed class ReturnToSelectionTests
{
    [Fact]
    public void The_observed_empty_request_decodes()
    {
        var decoded = new ReturnToSelectionMessageDecoder().Decode(GameMessageMetadata.Empty, []);
        Assert.IsType<ReturnToSelectionRequest>(decoded);
    }

    [Fact]
    public void A_request_with_an_unexpected_body_is_refused() =>
        Assert.Throws<InvalidDataException>(() =>
            new ReturnToSelectionMessageDecoder().Decode(GameMessageMetadata.Empty, [0]));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_return_batch_uses_the_reference_order_and_cmd_envelope(bool knight)
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var sender = new EncodedSender(provider);
        var delivery = new CharacterSelectionDelivery(sender, new NoCleanupNotices());
        var roster = new AccountCharacterRoster(new AccountId(1), 24, []);

        await delivery.SendAsync(new("return"), new ReturnToSelectionResponse(), roster, knight,
            CancellationToken.None);

        Assert.Equal(knight ? new ushort[] { 0x0245, 7, 2, 0x0289 } : [7, 2, 0x0289],
            sender.Frames.Select(frame => BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))));
        var offset = knight ? 1 : 0;
        if (knight)
        {
            Assert.Equal(0, sender.Frames[0][0]);
            Assert.Equal(new byte[20], sender.Frames[0][15..]);
        }
        Assert.Equal(1, sender.Frames[offset][0]);
        Assert.Equal(new byte[] { 1 }, sender.Frames[offset][15..]);
        Assert.Equal(0, sender.Frames[offset + 1][0]);
        Assert.Equal(0, sender.Frames[offset + 1][7]);
        Assert.Equal(2, sender.Frames[offset + 1][15]);
        Assert.Equal(0, sender.Frames[offset + 2][0]);
    }

    private sealed class NoCleanupNotices : ICleanupNotificationDelivery
    {
        public ValueTask DeliverAsync(SelectedSessionCleanupNotificationPlan plan, CancellationToken ct) =>
            ValueTask.CompletedTask;
    }

    private sealed class EncodedSender(ServiceProvider provider) : IGameMessageSender
    {
        public List<byte[]> Frames { get; } = [];
        public ValueTask SendCmdAsync(NetworkConnectionId connection, IGameCmdMessage message, CancellationToken ct) =>
            Encode(message);
        public ValueTask SendNotiAsync(NetworkConnectionId connection, IGameNotiMessage message, CancellationToken ct) =>
            Encode(message);
        public ValueTask SendExplicitAsync(NetworkConnectionId connection, GameExplicitMessageDelivery delivery, CancellationToken ct) =>
            throw new NotSupportedException();
        private ValueTask Encode(IGameServerMessage message)
        {
            var registration = provider.GetServices<ServerMessageEncoderRegistration>()
                .Single(value => value.MessageType == message.GetType());
            var encoder = (IServerMessageEncoder)provider.GetRequiredService(registration.EncoderType);
            Frames.AddRange(encoder.Encode(message));
            return ValueTask.CompletedTask;
        }
    }
}
