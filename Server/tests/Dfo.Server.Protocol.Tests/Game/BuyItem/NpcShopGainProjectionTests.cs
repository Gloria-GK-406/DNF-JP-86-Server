using System.Buffers.Binary;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.BuyItem;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.BuyItem;

public sealed class NpcShopGainProjectionTests
{
    [Theory]
    [InlineData(0, 1, 6)]
    [InlineData(5, 1, 4)]
    [InlineData(5, 3, 2)]
    public async Task Each_purchase_announces_only_its_added_count_and_keeps_the_final_pile(int initial, int bought, int repeats)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = SelectedSessionIdentity.Create(new(1), new(2), new("gain"), 2, 10011);
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state); Dfo.Server.Testing.TestSessions.EnterWorldWithConnection(sessions, state, session);
            var handler = new NpcShopPurchaseCompletedHandler(sessions);
            var client = new NativeStackConsumer();
            client.Seed(new(BusinessInventorySpace.QuickSlot, 0), 5000, initial);
            for (var index = 1; index <= repeats; index++)
            {
                var total = initial + index * bought;
                var receipt = new NpcShopPurchaseReceipt(session, 5000, bought, 1000 - index, 0, 0, true, [],
                    [Grant(0, total, bought)], DateTimeOffset.UnixEpoch);
                var messages = await handler.ComposeAsync(new(session.CharacterId, session.ConnectionId), new(receipt), default);
                client.Apply(messages);
                Assert.Equal(total, client.Total(5000));
                Assert.Equal(bought, client.Gains.GetValueOrDefault(5000));
                // A repeated absolute projection must neither grant nor announce the purchase twice.
                client.Apply(messages);
                Assert.Equal(total, client.Total(5000));
                Assert.Empty(client.Gains);
            }
        });
    }

    [Fact]
    public async Task Split_stacks_and_material_payment_keep_gross_purchase_gain_without_regranting_the_first_slot()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = SelectedSessionIdentity.Create(new(1), new(2), new("split"), 2, 10011);
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state); Dfo.Server.Testing.TestSessions.EnterWorldWithConnection(sessions, state, session);
            var client = new NativeStackConsumer();
            client.Seed(new(BusinessInventorySpace.QuickSlot, 0), 5000, 9);
            client.Seed(new(BusinessInventorySpace.Material, 0), 1000, 5);
            var receipt = new NpcShopPurchaseReceipt(session, 5000, 3, 99700, 0, 0, true,
                [new(new(BusinessInventorySpace.Material, 0), null)],
                [Grant(0, 10, 1), Grant(1, 2, 2)], DateTimeOffset.UnixEpoch);
            var messages = await new NpcShopPurchaseCompletedHandler(sessions).ComposeAsync(
                new(session.CharacterId, session.ConnectionId), new(receipt), default);
            client.Apply(messages);
            Assert.Equal(0, client.Total(1000)); Assert.Equal(12, client.Total(5000));
            Assert.Equal(new[] { new KeyValuePair<int, int>(5000, 3) }, client.Gains);
            client.Apply(messages);
            Assert.Equal(12, client.Total(5000)); Assert.Empty(client.Gains);
        });
    }

    [Fact]
    public async Task Limited_stack_keeps_the_native_absolute_quota_after_a_zero_increment_ack_and_replay()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = SelectedSessionIdentity.Create(new(1), new(2), new("limited"), 2, 10011);
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var state); Dfo.Server.Testing.TestSessions.EnterWorldWithConnection(sessions, state, session);
            var client = new NativeStackConsumer();
            client.Seed(new(BusinessInventorySpace.QuickSlot, 0), 5000, 2);
            client.Purchased[5000] = 2;
            var receipt = new NpcShopPurchaseReceipt(session, 5000, 3, 99700, 0, 0, true, [],
                [Grant(0, 5, 3)], DateTimeOffset.UnixEpoch) { PurchaseCount = new(5000, 5) };
            var messages = await new NpcShopPurchaseCompletedHandler(sessions).ComposeAsync(
                new(session.CharacterId, session.ConnectionId), new(receipt), default);
            client.Apply(messages);
            Assert.Equal(3, client.Gains[5000]); Assert.Equal(5, client.Total(5000)); Assert.Equal(5, client.Purchased[5000]);
            client.Apply(messages);
            Assert.Empty(client.Gains); Assert.Equal(5, client.Total(5000)); Assert.Equal(5, client.Purchased[5000]);
        });
    }

    private static NpcShopGrantedSlot Grant(int slot, int final, int added) => new(
        new(BusinessInventorySpace.QuickSlot, slot), ItemSnapshot.FromCore(StackableItemCore.Create(2, 5000, final), true), added);

    // A bounded ClientStatic oracle, not an actual client: ACK D35E57..D35F0C replaces tail
    // balances then adds primary Data; NOTI 000E D7E965..D7E99C reports positive absolute differences.
    // Feed production-encoded frames so a projection-order or wire-value regression is observable.
    private sealed class NativeStackConsumer
    {
        private readonly Dictionary<(byte Space, ushort Slot), (int Id, int Count)> piles = [];
        public Dictionary<int, int> Gains { get; } = [];
        public Dictionary<int, int> Purchased { get; } = [];
        public void Seed(BusinessSlot slot, int id, int count)
        {
            var wire = new InventorySlotMappingRuleService().ToGameSlot(slot);
            if (count > 0) piles[((byte)wire.Space, checked((ushort)wire.Index))] = (id, count);
        }
        public int Total(int id) => piles.Values.Where(value => value.Id == id).Sum(value => value.Count);
        public void Apply(IReadOnlyList<IServerNetworkMessage> messages)
        {
            Gains.Clear();
            foreach (var message in messages)
            {
                byte[] frame = message switch
                {
                    BuyItemResponse ack => Assert.Single(new BuyItemResponseEncoder().Encode(ack)),
                    InventoryDeltaMessage delta => Assert.Single(new InventoryDeltaEncoder().Encode(delta)),
                    ShopPurchaseCountResponse counts => Assert.Single(new ShopPurchaseCountEncoder().Encode(counts)),
                    _ => throw new NotSupportedException(),
                };
                var body = frame.AsSpan(15);
                if (BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)) == 0x02CC)
                {
                    var size = BinaryPrimitives.ReadInt32LittleEndian(body[1..]);
                    for (var i = 0; i < size; i++)
                        Purchased[BinaryPrimitives.ReadInt32LittleEndian(body[(5 + i * 8)..])] =
                            BinaryPrimitives.ReadInt32LittleEndian(body[(9 + i * 8)..]);
                    continue;
                }
                if (frame[0] == 1)
                {
                    Assert.Equal(1, body[0]);
                    for (var i = 0; i < body[118]; i++)
                    {
                        var id = BinaryPrimitives.ReadInt32LittleEndian(body[(119 + i * 8)..]);
                        var absolute = BinaryPrimitives.ReadInt32LittleEndian(body[(123 + i * 8)..]);
                        var key = piles.Keys.FirstOrDefault(key => piles[key].Id == id);
                        if (piles.ContainsKey(key))
                        {
                            if (absolute == 0) piles.Remove(key); else piles[key] = (id, absolute);
                        }
                    }
                    var slot = BinaryPrimitives.ReadUInt16LittleEndian(body[17..]);
                    var item = BinaryPrimitives.ReadInt32LittleEndian(body[19..]);
                    var added = BinaryPrimitives.ReadInt32LittleEndian(body[23..]);
                    if (Purchased.ContainsKey(item)) Purchased[item] += added;
                    var own = ((byte)InventoryContainerType.Main, slot);
                    var old = piles.GetValueOrDefault(own);
                    piles[own] = (item, old.Id == item ? old.Count + added : added);
                }
                else
                {
                    var count = BinaryPrimitives.ReadUInt16LittleEndian(body[1..]);
                    for (var i = 0; i < count; i++)
                    {
                        var record = body[(3 + i * 101)..];
                        var slot = BinaryPrimitives.ReadUInt16LittleEndian(record);
                        var id = BinaryPrimitives.ReadInt32LittleEndian(record[2..]);
                        var absolute = BinaryPrimitives.ReadInt32LittleEndian(record[6..]);
                        var key = (body[0], slot);
                        if (id == 0) continue; // wallet projection, no item gain
                        if (id < 0) { piles.Remove(key); continue; }
                        var old = piles.GetValueOrDefault(key);
                        var gain = Math.Max(0, absolute - (old.Id == id ? old.Count : 0));
                        if (gain > 0) Gains[id] = Gains.GetValueOrDefault(id) + gain;
                        piles[key] = (id, absolute);
                    }
                }
            }
        }
    }
}
