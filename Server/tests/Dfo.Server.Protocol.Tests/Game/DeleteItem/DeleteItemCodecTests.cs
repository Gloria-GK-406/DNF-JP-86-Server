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

namespace Dfo.Server.Protocol.Tests.Game.DeleteItem;

public sealed class DeleteItemCodecTests
{
    [Fact]
    public async Task Discard_sends_the_corrected_quest_snapshot_after_its_success_answer()
    {
        var session = SelectedSessionIdentity.Create(new(1), new(2), new("discard-quest"), 1, 10011);
        var sessions = new SelectedSessionDomainService();
        sessions.Select(session);
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
