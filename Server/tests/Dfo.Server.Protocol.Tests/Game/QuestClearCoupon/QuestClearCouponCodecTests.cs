using System.Collections.Immutable;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.QuestClearCoupon;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.QuestClearCoupon;

public sealed class QuestClearCouponCodecTests
{
    [Fact]
    public void Fatigue_action_is_separate_from_quest_coupon_and_echoes_its_action()
    {
        var request = QuestClearCouponPayloadCodec.Decode(GameMessageMetadata.Empty,
            Convert.FromHexString("480000000000003A0000000000000000000000"));
        Assert.False(request.IsSupported);
        Assert.True(request.IsFatiguePotion);
        Assert.False((request with { Action = 55 }).IsFatiguePotion);
        Assert.Equal(Convert.FromHexString("014800003A000000"),
            QuestClearCouponPayloadCodec.Encode(new(true, 72, 0) { Action = 58 }));
        Assert.False((request with { Reserved2 = 1 }).IsFatiguePotion);
        Assert.False((request with { Container = 1 }).IsFatiguePotion);
    }
    [Fact]
    public void Cmd_0204_is_registered_as_its_own_slice()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            value => value.Selector == 1 && value.Type == 0x0204);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);
        var request = decoder.Decode(GameMessageMetadata.Empty,
            Convert.FromHexString("450000000000004D0000000000000000000000"));
        Assert.IsType<QuestClearCouponRequest>(request);
        Assert.Contains(provider.GetServices<MessageHandlerRegistration>(),
            value => value.MessageType == request!.MessageType);
    }

    [Fact]
    public void The_observed_A21_coupon_request_is_the_only_supported_shape()
    {
        var request = QuestClearCouponPayloadCodec.Decode(GameMessageMetadata.Empty,
            Convert.FromHexString("450000000000004D0000000000000000000000"));
        Assert.Equal(69, request.SourceSlot);
        Assert.Equal(77u, request.Action);
        Assert.True(request.IsSupported);
    }

    [Theory]
    [InlineData("450000000000004D00000000000000000000")]
    [InlineData("450001000000004D0000000000000000000000")]
    [InlineData("450000000000004C0000000000000000000000")]
    [InlineData("450000010000004D0000000000000000000000")]
    [InlineData("450000000000004D0000000100000000000000")]
    [InlineData("450000000000004D0000000000000001000000")]
    public void Other_lengths_containers_actions_and_reserved_values_are_refused(string hex)
    {
        Assert.False(QuestClearCouponPayloadCodec.Decode(GameMessageMetadata.Empty,
            Convert.FromHexString(hex)).IsSupported);
    }

    [Fact]
    public void The_minimal_success_ack_echoes_slot_container_and_action_without_a_tail()
    {
        Assert.Equal(Convert.FromHexString("014500004D000000"),
            QuestClearCouponPayloadCodec.Encode(new(true, 69, 0)));
    }

    [Fact]
    public void The_minimal_failure_ack_carries_silent_reason_and_zero_detail()
    {
        Assert.Equal(Convert.FromHexString("000245000000"),
            QuestClearCouponPayloadCodec.Encode(new(false, 69, 0)));
    }

    [Fact]
    public async Task Handler_passes_the_exact_decoded_request_and_connection_to_the_whole()
    {
        var commands = new RecordingCommands();
        var handler = new QuestClearCouponHandler(commands);
        var request = QuestClearCouponPayloadCodec.Decode(GameMessageMetadata.Empty,
            Convert.FromHexString("450000000000004C0000000000000000000000"));
        await handler.HandleAsync(NetworkDispatchContext.Unknown, request, CancellationToken.None);
        var command = Assert.IsType<GameQuestClearCouponCommand>(commands.Input);
        Assert.Same(request, command.Input); Assert.Same(NetworkDispatchContext.Unknown.Connection, command.Dispatch.Connection);
    }
    private sealed class RecordingCommands : ICommandBus
    {
        internal object? Input;
        public ValueTask<T> SendAsync<T>(ICommand<T> command, CancellationToken ct = default) where T : notnull
        { Input = command; return ValueTask.FromResult((T)(object)false); }
    }

    [Fact]
    public async Task Committed_coupon_delivery_keeps_ack_inventory_and_quest_projections_in_order_and_session_scoped()
    {
        var session = SelectedSessionIdentity.Create(new AccountId(1), new CharacterId(1123),
            new NetworkConnectionId("quest-clear"), 2, 10011);
        var receipt = new OpeningInventoryReceipt(session, ImmutableArray<OpeningInventoryUpdate>.Empty,
            [new(BusinessInventorySpace.Consumable, 4)], ImmutableArray<OpeningRewardDisplay>.Empty, DateTimeOffset.UnixEpoch);
        var flags = new byte[30_000];
        flags[1016] = 1;
        var following = new IServerNetworkMessage[]
        {
            new QuestVisibilityMessage(flags),
            new AcceptedQuestProgressMessage([new AcceptedQuestProgressState(101, 0)]),
            new AvailableQuestListMessage(2, AvailableQuestEntryState.Empty),
        };
        var completed = new OpeningCompleted(receipt, [new QuestClearCouponResponse(true, 69, 0)],
            FollowingMessages: following);
        var delivery = new OpeningCompletedHandler(new global::Dfo.Server.Protocol.Tests.Game.OneSession(session));

        var messages = await delivery.ComposeAsync(
            new NotificationRecipient(session.CharacterId, session.ConnectionId, session),
            completed, CancellationToken.None);
        Assert.Equal([
            typeof(QuestClearCouponResponse), typeof(InventoryDeltaMessage),
            typeof(QuestVisibilityMessage), typeof(AcceptedQuestProgressMessage),
            typeof(AvailableQuestListMessage),
        ], messages.Select(message => message.GetType()));
        Assert.Equal(69, Assert.Single(Assert.IsType<InventoryDeltaMessage>(messages[1]).Items).SlotIndex);
        Assert.Equal(1, Assert.IsType<QuestVisibilityMessage>(messages[2]).Flags[1016]);

        Assert.Empty(await delivery.ComposeAsync(
            new NotificationRecipient(new CharacterId(999), session.ConnectionId), completed,
            CancellationToken.None));
        var old = SelectedSessionIdentity.Create(session.AccountId, session.CharacterId,
            new NetworkConnectionId("old-quest-clear"), 1, 10011);
        var staleReceipt = receipt with { Session = old };
        Assert.Empty(await delivery.ComposeAsync(
            new NotificationRecipient(old.CharacterId, old.ConnectionId, old),
            completed with { Receipt = staleReceipt }, CancellationToken.None));
    }

    [Fact]
    public void Committed_quest_log_projects_full_flags_and_carried_progress_in_slot_order()
    {
        var messages = QuestClearCouponProjection.FromLog(
            [new ClearedQuest(1016, 1), new ClearedQuest(1776, 2)],
            [new CarriedQuest(3, 2000, 12), new CarriedQuest(1, 101, 5)]);
        var flags = Assert.IsType<QuestVisibilityMessage>(messages[0]).Flags;
        Assert.Equal(30_000, flags.Length);
        Assert.Equal(1, flags[1016]);
        Assert.Equal(2, flags[1776]);
        Assert.Equal(0, flags[101]);
        Assert.Equal([new AcceptedQuestProgressState(101, 5),
            new AcceptedQuestProgressState(2000, 12)],
            Assert.IsType<AcceptedQuestProgressMessage>(messages[1]).Quests);
    }

}
