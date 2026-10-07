using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.LevelUpTicket;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.LevelUpTicket;

public sealed class LevelUpTicketCodecTests
{
    [Fact]
    public void Observed_ticket_request_and_response_are_registered_as_one_cmd_slice()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            value => value.Selector == 1 && value.Type == 0x01A2);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);
        var request = decoder.Decode(GameMessageMetadata.Empty, Convert.FromHexString("050000"));
        Assert.NotNull(request);
        Assert.Contains(provider.GetServices<MessageHandlerRegistration>(), value => value.MessageType == request.MessageType);
        var responseRegistration = Assert.Single(provider.GetServices<ServerMessageEncoderRegistration>(),
            value => value.MessageType == typeof(LevelUpTicketResponse));
        var encoder = (IServerMessageEncoder)provider.GetRequiredService(responseRegistration.EncoderType);
        var frame = Assert.Single(encoder.Encode(new LevelUpTicketResponse(5)));
        Assert.Equal(1, frame[0]);
        Assert.Equal(new byte[] { 0xA2, 1 }, frame[1..3]);
        Assert.Equal(LevelUpTicketPayloadCodec.Encode(new(5)), frame[15..]);
    }

    [Fact]
    public void Committed_success_uses_silent_completion_and_does_not_enter_the_old_activity_popup_branch()
    {
        Assert.Equal(Convert.FromHexString("0000"), LevelUpTicketPayloadCodec.Encode(new(5)));
        Assert.Equal(Convert.FromHexString("0016"), LevelUpTicketPayloadCodec.Encode(new(5, 0x16)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0500")]
    [InlineData("05000000")]
    public void Invalid_length_has_no_complete_ticket_request(string body)
    {
        var request = Assert.IsType<LevelUpTicketRequest>(new LevelUpTicketDecoder().Decode(GameMessageMetadata.Empty, Convert.FromHexString(body)));
        Assert.False(request.Complete);
    }

    [Fact]
    public async Task Silent_completion_is_followed_by_absolute_slot_and_level_updates_for_the_current_session()
    {
        var session = SelectedSessionIdentity.Create(new(1), new(2), new("ticket-test"), 2, 10011);
        var receipt = new OpeningInventoryReceipt(session, [], [OpeningWireSlots.FromMain(5)], [], DateTimeOffset.UnixEpoch);
        var experience = new ExperienceNotification(85, 880603520, new(0, 0, 0, 0), AccountHonorLevel.Initial, ExperienceBonusLines.None);
        var notification = new OpeningCompleted(receipt, [new LevelUpTicketResponse(5)], FollowingMessages: [experience]);
        var delivery = new OpeningCompletedHandler(new global::Dfo.Server.Protocol.Tests.Game.OneSession(session));
        var messages = await delivery.ComposeAsync(new NotificationRecipient(session.CharacterId, session.ConnectionId, session), notification, default);
        Assert.Equal(Convert.FromHexString("0000"), LevelUpTicketPayloadCodec.Encode(Assert.IsType<LevelUpTicketResponse>(messages[0])));
        var item = Assert.Single(Assert.IsType<InventoryDeltaMessage>(messages[1]).Items);
        Assert.Equal(5, item.SlotIndex);
        Assert.Equal(-1, item.Core.ItemId);
        Assert.Same(experience, messages[2]);
        Assert.Empty(await delivery.ComposeAsync(new NotificationRecipient(new CharacterId(999), session.ConnectionId), notification, default));
    }

    [Theory]
    [InlineData("0500")]
    [InlineData("FFFF00")]
    [InlineData("050001")]
    public async Task Invalid_request_gets_a_refusal_without_entering_the_domain(string body)
    {
        var sender = new Sender();
        var handler = new LevelUpTicketHandler(null!, null!, null!, null!, sender,
            null!, null!, null!, null!, NullLogger<LevelUpTicketHandler>.Instance);
        var request = (LevelUpTicketRequest)new LevelUpTicketDecoder().Decode(GameMessageMetadata.Empty, Convert.FromHexString(body));
        await handler.HandleAsync(NetworkDispatchContext.Unknown, request, default);
        Assert.Equal(new LevelUpTicketResponse(request.SourceSlot, 1), Assert.Single(sender.Sent));
    }

    private sealed class Sender : IGameMessageSender
    {
        public List<IGameCmdMessage> Sent { get; } = [];
        public ValueTask SendCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken ct)
        { Sent.Add(message); return ValueTask.CompletedTask; }
        public ValueTask SendNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask SendExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken ct) => throw new NotSupportedException();
    }
}
