using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DisjointItem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Tests.Game.DisjointItem;

public sealed class DisjointItemCodecTests
{
    [Fact]
    public void System_disjoint_is_the_nine_byte_game_command()
    {
        var services = new ServiceCollection();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<GameMessageDecoderRegistration>(),
            value => value.Selector == 1 && value.Type == 0x001A);
        var decoder = (IGameMessageDecoder)provider.GetRequiredService(registration.DecoderType);

        var request = Assert.IsType<DisjointItemRequest>(decoder.Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("090000FFFF00000000")));

        Assert.Equal(ClientMessageType.GameDisjointItem, request.MessageType);
        Assert.Equal(((short)9, InventoryContainerType.Main, (short)-1, 0),
            ((short)request.TargetSlot.Index, request.TargetSlot.Space, request.DisjointToolSlotIndex,
                request.DisjointProviderId));
    }

    [Fact]
    public void Success_ack_names_the_source_and_every_material_landing()
    {
        Assert.Equal(Convert.FromHexString(
                "01090000027900DD0B00000C0000007A005E0C000002000000"),
            DisjointItemResponseEncoder.EncodeBody(new(
                new GameSlot(InventoryContainerType.Main, 9),
                [
                    new(new GameSlot(InventoryContainerType.Main, 121), 3037, 12),
                    new(new GameSlot(InventoryContainerType.Main, 122), 3166, 2),
                ])));
        Assert.Equal(new byte[] { 0, 0x13 },
            DisjointItemResponseEncoder.EncodeBody(new(null, ErrorCode: 0x13)));
    }

    [Fact]
    public async Task Success_completion_also_publishes_committed_inventory_state()
    {
        var session = SelectedSessionIdentity.Create(new(1), new(2), new("disjoint-sync"), 1, 10011);
        var sessions = new SelectedSessionDomainService();
        sessions.Select(session);
        var receipt = new EquipmentDisjointReceipt(
            session,
            new BusinessSlot(BusinessInventorySpace.CarriedEquipment, 0),
            [new(new BusinessSlot(BusinessInventorySpace.Material, 0), 3037, 12)],
            [],
            [
                new(new BusinessSlot(BusinessInventorySpace.CarriedEquipment, 0), null),
                new(new BusinessSlot(BusinessInventorySpace.Consumable, 0),
                    StackableItemCore.Create(2, 2600469, 1)),
                new(new BusinessSlot(BusinessInventorySpace.Material, 0),
                    StackableItemCore.Create(3, 3037, 112)),
            ],
            DateTimeOffset.UnixEpoch);

        var messages = await new EquipmentDisjointCompletedHandler(
            sessions, new SlotProjection()).ComposeAsync(
            new NotificationRecipient(session.CharacterId, session.ConnectionId),
            new EquipmentDisjointCompleted(receipt),
            CancellationToken.None);

        Assert.IsType<DisjointItemResponse>(messages[0]);
        var updates = messages.OfType<InventoryDeltaMessage>().ToArray();
        Assert.Equal(2, updates.Length);
        var cleared = Assert.Single(updates, update => update.Items.Count == 1
            && update.Items[0].SlotIndex == 9);
        Assert.Equal(-1, cleared.Items[0].Core.ItemId);
        Assert.Equal(
            [(65, 2600469, 1), (121, 3037, 112)],
            Assert.Single(updates, update => update != cleared).Items
                .Select(item => (item.SlotIndex, item.Core.ItemId, item.Core.Value)));
    }

    [Fact]
    public async Task Account_material_completion_acks_the_virtual_slot_and_publishes_its_absolute_total()
    {
        var session = SelectedSessionIdentity.Create(new(1), new(2), new("disjoint-account"), 1, 10011);
        var sessions = new SelectedSessionDomainService();
        sessions.Select(session);
        var receipt = new EquipmentDisjointReceipt(
            session,
            new BusinessSlot(BusinessInventorySpace.CarriedEquipment, 0),
            [],
            [new(AccountMaterialKind.ClearCube, 3037, 358, 12, 112)],
            [new(new BusinessSlot(BusinessInventorySpace.CarriedEquipment, 0), null)],
            DateTimeOffset.UnixEpoch);

        var messages = await new EquipmentDisjointCompletedHandler(
            sessions, new SlotProjection()).ComposeAsync(
            new NotificationRecipient(session.CharacterId, session.ConnectionId),
            new EquipmentDisjointCompleted(receipt),
            CancellationToken.None);

        var response = Assert.IsType<DisjointItemResponse>(messages[0]);
        Assert.Equal(
            new DisjointItemMaterial(new GameSlot(InventoryContainerType.Main, 358), 3037, 12),
            Assert.Single(response.Materials!));
        var virtualUpdate = Assert.Single(messages.OfType<InventoryDeltaMessage>(),
            update => update.Items.Any(item => item.SlotIndex == 358));
        var item = Assert.Single(virtualUpdate.Items);
        Assert.Equal((358, 3037, 112), (item.SlotIndex, item.Core.ItemId, item.Core.Value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(10)]
    public void Any_body_length_other_than_nine_is_rejected(int length) =>
        Assert.Throws<InvalidDataException>(() => new DisjointItemMessageDecoder()
            .Decode(GameMessageMetadata.Empty, new byte[length]));

    private sealed class SlotProjection : IInventorySlotProjectionDomainService
    {
        private readonly InventorySlotMappingRuleService mappings = new();

        public GameSlot Project(BusinessSlot slot) => mappings.ToGameSlot(slot);
    }
}
