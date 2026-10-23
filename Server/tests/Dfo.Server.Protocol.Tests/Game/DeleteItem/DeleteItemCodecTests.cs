using Dfo.Server.Protocol.Game;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DeleteItem;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.DeleteItem;

public sealed class DeleteItemCodecTests
{
    [Theory]
    [InlineData(352)]
    [InlineData(365)]
    [InlineData(65535)]
    public async Task Unmapped_slot_is_rejected_without_throwing_or_touching_business_state(int slot)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sender = new CmdSender();
            var handler = new DeleteItemHandler(null!, null!, new InventorySlotMappingRuleService(), null!, null!, sender,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<DeleteItemHandler>.Instance);
            await handler.HandleAsync(new NetworkDispatchContext(new NetworkConnectionContext(new("invalid-slot"),NetworkEndpointKind.Game,10011)),
                new DeleteItemRequest(GameMessageMetadata.Empty,new(InventoryContainerType.Main,slot),1,1,3037,1,0,1),default);
            var rejected=Assert.IsType<DeleteItemResponse>(Assert.Single(sender.Sent));
            Assert.False(rejected.Success);
            Assert.Equal(DeleteItemResponse.CannotDelete,rejected.ErrorCode);
        });
    }

    [Fact]
    public void Cube_debit_ack_preserves_virtual_slot_and_consumed_count()
    {
        Assert.Equal(Convert.FromHexString("0100016601010000000000"),
            DeleteItemResponseEncoder.EncodeBody(new(new(InventoryContainerType.Main,358),1,Success:true)));
    }

    [Fact]
    public async Task Discard_sends_the_corrected_quest_snapshot_after_its_success_answer()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = SelectedSessionIdentity.Create(new(1), new(2), new("discard-quest"), 2, 10011);
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state);
            Dfo.Server.Testing.TestSessions.EnterWorldWithConnection(sessions, state, session);
            var receipt = new ItemDiscardReceipt(
                session, new BusinessSlot(BusinessInventorySpace.Consumable, 0), 5000, 1,
                null, DateTimeOffset.UnixEpoch);

            var messages = await new ItemDiscardCompletedHandler(
                sessions, new SlotProjection()).ComposeAsync(
                new NotificationRecipient(session.CharacterId, session.ConnectionId),
                new ItemDiscardCompleted(receipt,
                    new QuestProgressSnapshot([new CarriedQuest(0, 2001, 1)])),
                CancellationToken.None);

            Assert.IsType<DeleteItemResponse>(messages[0]);
            var progress = Assert.IsType<AcceptedQuestProgressMessage>(messages[1]);
            Assert.Equal(1u, Assert.Single(progress.Quests).TriggerValue);
        });
    }

    /// <summary>
    /// The CMDs a real registration adapter registered, read from its outbox; every call goes to the real protocol
    /// sender over it (TASK-25). A NOTI or an explicit delivery fails where it is registered, as the old double refused one.
    /// </summary>
    private sealed class CmdSender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();
        public CmdSender()
        {
            outbound.RefuseExplicit();
            outbound.FailAny(message => outbound.ExplicitInFlight is null && message is IGameNotiMessage, () => new NotSupportedException());
        }
        public List<IGameServerMessage> Sent => [.. outbound.MessagesAs<IGameServerMessage>()];
        public ValueTask SendCmdAsync(NetworkConnectionId connection, IGameCmdMessage message, CancellationToken ct) =>
            outbound.Game.SendCmdAsync(connection, message, ct);
        public ValueTask SendNotiAsync(NetworkConnectionId connection, IGameNotiMessage message, CancellationToken ct) =>
            outbound.Game.SendNotiAsync(connection, message, ct);
        public ValueTask SendExplicitAsync(NetworkConnectionId connection, GameExplicitMessageDelivery delivery, CancellationToken ct) =>
            outbound.Game.SendExplicitAsync(connection, delivery, ct);
    }

    private sealed class SlotProjection : IInventorySlotProjectionDomainService
    {
        private readonly InventorySlotMappingRuleService mappings = new();

        public GameSlot Project(BusinessSlot slot) => mappings.ToGameSlot(slot);
    }

    [Theory]
    [InlineData("000101004100B4B39800010000000000000000", 65, 10007476)]
    [InlineData("000101000900B06A0000010000000000000000", 9, 27312)]
    public void A21_ordinary_discard_vectors_keep_all_wire_semantics(string body, ushort slot, int itemId)
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            value => value.Selector == 1 && value.Type == 0x0012);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);

        var request = Assert.IsType<DeleteItemRequest>(
            decoder.Decode(GameMessageMetadata.Empty, Convert.FromHexString(body)));

        Assert.Equal(ClientMessageType.GameDeleteItem, request.MessageType);
        Assert.Equal((InventoryContainerType.Main, (byte)1, (ushort)1, slot, itemId, 1, 0u, (byte)0),
            (request.Slot.Space, request.EntryCount, request.OperationType, request.Slot.Index,
                request.ItemId, request.DeleteCount, request.ReservedZero, request.SafeModeSkipIndex));
    }

    [Fact]
    public void Security_exception_context_is_carried_without_changing_the_layout()
    {
        var body = Convert.FromHexString("000101004100B4B39800010000000000000001");
        var request = Assert.IsType<DeleteItemRequest>(
            new DeleteItemMessageDecoder().Decode(GameMessageMetadata.Empty, body));
        Assert.Equal((uint)0, request.ReservedZero);
        Assert.Equal((byte)1, request.SafeModeSkipIndex);
    }

    [Theory]
    [InlineData(18)]
    [InlineData(20)]
    public void Alternate_or_extended_builders_are_not_misread_as_the_ordinary_layout(int length) =>
        Assert.Throws<InvalidDataException>(() =>
            new DeleteItemMessageDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));

    [Fact]
    public void A21_success_and_failure_responses_match_the_client_reads()
    {
        Assert.Equal(Convert.FromHexString("0100010900010000000000"),
            DeleteItemResponseEncoder.EncodeBody(new(
                new GameSlot(InventoryContainerType.Main, 9), 1, Success: true)));
        Assert.Equal(Convert.FromHexString("001700"),
            DeleteItemResponseEncoder.EncodeBody(new(
                new GameSlot(InventoryContainerType.Main, 9),
                ErrorCode: DeleteItemResponse.CannotDelete)));
    }
}
