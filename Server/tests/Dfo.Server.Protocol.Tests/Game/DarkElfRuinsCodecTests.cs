using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.DarkElfRuins;
using Dfo.Server.Abstractions.Networking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Protocol.Tests.Game;

[Trait("Category", "Unit")]
public sealed class DarkElfRuinsCodecTests
{
    [Fact]
    public async Task Reward_refresh_uses_absolute_changed_slots_for_native_gain_feedback_without_replaying_a_full_bag()
    {
        await Dfo.Server.Testing.TestGameplay.RunAsync(async () =>
        {
            var sessions = Dfo.Server.Testing.TestSessions.Directory(out var online);
            var session = Dfo.Server.Data.World.SelectedSessionIdentity.Create(new(1), new(2), new("ruins-gain"), 2, 10011);
            Dfo.Server.Testing.TestSessions.EnterWorldWithConnection(sessions, online, session);
            var rewardSlots = new[] { new Dfo.Server.Data.Inventory.BusinessSlot(Dfo.Server.Data.Inventory.BusinessInventorySpace.Consumable, 0),
                new Dfo.Server.Data.Inventory.BusinessSlot(Dfo.Server.Data.Inventory.BusinessInventorySpace.Material, 0) };
            var items = new[] { (rewardSlots[0], 10155137, 3), (rewardSlots[1], 10155143, 8) };
            var cores = items.Select(x => new Dfo.Server.Data.Inventory.InventoryItemState(null, A21InventoryLayout.ToGameSlot(x.Item1).Index,
                Dfo.Server.Data.Inventory.StackableItemCore.Create(2, x.Item2, x.Item3))).ToArray();
            var binding = Dfo.Server.Testing.ProtocolAlgorithmFixture.Decorate(Dfo.Server.Testing.ProtocolAlgorithmFixture.Default<IBusinessInventorySlotDomainService>(),
                (method, args) =>
                {
                    var slot = (Dfo.Server.Data.Inventory.BusinessSlot)args[1]!;
                    var row = items.Single(x => x.Item1 == slot);
                    var container = Dfo.Server.Testing.ProtocolAlgorithmFixture.Decorate(Dfo.Server.Testing.ProtocolAlgorithmFixture.Default<IBusinessItemContainer>(),
                        (m, a) => (true, ValueTask.FromResult<ItemReference?>(new ItemReference(new object(), default, slot.Index, 0,
                            ItemSnapshot.FromCore(Dfo.Server.Data.Inventory.StackableItemCore.Create(2, row.Item2, row.Item3), true), slot))));
                    return (true, new BoundInventorySlot(slot, container));
                });
            var handler = Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<RuinsInventoryRefreshHandler>(sessions, binding);
            var frames = await handler.ComposeAsync(new(session.CharacterId, session.ConnectionId, session),
                new(rewardSlots.Concat([rewardSlots[0]]).ToArray(), DateTimeOffset.UnixEpoch), default);
            var delta = Assert.IsType<InventoryDeltaMessage>(Assert.Single(frames));
            Assert.Equal(cores.Select(x => (x.SlotIndex, x.Core.ItemId, x.Core.Value)), delta.Items.Select(x => (x.SlotIndex, x.Core.ItemId, x.Core.Value)));
            var encoded = Assert.Single(new InventoryDeltaEncoder().Encode(delta));
            Assert.Equal(0x000E, System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(encoded.AsSpan(1, 2)));
            // Native 000E gain text uses new absolute count minus the old pile, not the new total.
            Assert.Equal(new[] { 1, 1 }, delta.Items.Select((x, i) => x.Core.Value - new[] { 2, 7 }[i]));
            Assert.Empty(await handler.ComposeAsync(new(new(999), session.ConnectionId), new(rewardSlots, DateTimeOffset.UnixEpoch), default));
        });
    }

    [Fact]
    public void Start_map_projects_frozen_passive_objects_and_regenerates_only_script_declared_entries()
    {
        var cell = new MazeCell(0, 0);
        var layout = new DungeonLayout(5008, 1, 1, 1, cell, cell, 53000, 1,
            new Dictionary<MazeCell, DungeonCellDefinition> { [cell] = DungeonCellDefinition.Fixed(53000) })
            { RandomObjects = [new(cell, 258, 262, 57618, 200, 1, true), new(new(1, 0), 0, 0, 57625, 200, 1, true)] };
        var run = new DungeonRunState(layout, cell, 53000, new HashSet<MazeCell> { cell }, DateTimeOffset.UtcNow,
            CurrentCellState: new(cell, 53000, 0, 1, [], []));
        foreach (var first in new[] { true, false })
        {
            var bytes = DungeonEntryPayloadCodec.EncodeStartMap(run, first, [], 1, []);
            Assert.Equal(Convert.FromHexString("0101020100000601000012E10000C800000001000000FF"), bytes[^23..]);
        }
        run = run with { Layout = layout with { RandomObjects = [layout.RandomObjects[0] with { Regenerate = false }] } };
        Assert.Equal(new byte[] { 0, 0xFF }, DungeonEntryPayloadCodec.EncodeStartMap(run, false, [], 1, [])[^2..]);
    }
    [Fact]
    public void Every_ruins_projection_has_a_registered_game_encoder_including_the_boss_preamble()
    {
        var services = new ServiceCollection().AddLogging();
        new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        var rows = provider.GetServices<ServerMessageEncoderRegistration>();
        IServerNetworkMessage[] messages = [new RuinsBossNotification(1), new RuinsByteNotification(0x02FB, 2),
            new RuinsLicenseNotification(new(1, 2, 1, 0, [5006, 5016, 5026])),
            new RuinsClearNotification(new(5006, false, 0, new(10155137, 1), [new(10155143, 1)], [])),
            new RuinsAcknowledgement(0x032D)];
        foreach (var message in messages)
        {
            var row = Assert.Single(rows, x => x.MessageType == message.GetType());
            Assert.Equal(NetworkEndpointKind.Game, row.Endpoint);
            Assert.Single(((IServerMessageEncoder)provider.GetRequiredService(row.EncoderType)).Encode(message));
        }
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void Result_report_preserves_the_exact_counted_rows_without_treating_them_as_reward_authority(int count)
    {
        var body = new byte[5 + count * 6]; body[0] = (byte)count;
        var value = Assert.IsType<RuinsPlayResultRequest>(new RuinsPlayResultDecoder().Decode(GameMessageMetadata.Empty, body));
        Assert.Equal(body, value.Report);
        Assert.Throws<InvalidDataException>(() => new RuinsPlayResultDecoder().Decode(GameMessageMetadata.Empty, body.Concat(new byte[1]).ToArray()));
    }

    [Fact]
    public void Reward_request_is_empty_and_clear_summary_has_the_proven_twenty_one_byte_shape()
    {
        Assert.IsType<RuinsRewardRequest>(new RuinsRewardDecoder().Decode(GameMessageMetadata.Empty, []));
        Assert.Throws<InvalidDataException>(() => new RuinsRewardDecoder().Decode(GameMessageMetadata.Empty, new byte[1]));
        var reward = new DarkElfRuinsReward(5026, false, 30818, new(10155139, 1), [new ItemDeliveryGrant(10155143, 1)], []);
        Assert.Equal(Convert.FromHexString("006278000083F49A000100000087F49A0001000000"), new RuinsClearNotification(reward).EncodeBody());
        Assert.Equal(new byte[] { 1 }, new RuinsAcknowledgement(0x032D).EncodeBody());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Native_three_star_condition_receives_the_persisted_two_star_clear_count_in_each_region(int clears)
    {
        var selection = new DarkElfRuinsSelection(4, 0, 2, clears, [5007, 5017, 5027]);
        var body = new RuinsLicenseNotification(selection).EncodeBody();
        Assert.Equal(38, body.Length);
        for (var i = 0; i < 3; i++)
        {
            var record = body.AsSpan(2 + i * 12, 12);
            Assert.Equal(selection.DungeonIds[i], System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(record));
            Assert.Equal(2, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(record[4..]));
            var nativeCount = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(record[8..]);
            Assert.Equal(clears, nativeCount);
            // A21 00551099..A3 clips the matching row's third word to ETC Field5 - 1 (4 - 1).
            Assert.Equal($"{clears}/3", $"{Math.Min(nativeCount, 3)}/3");
        }
    }

    [Fact]
    public void Shared_license_and_clear_count_project_consistently_for_all_three_regions()
    {
        var selection = new DarkElfRuinsSelection(1, 2, 2, 1, [5007, 5017, 5027]);
        var body = new RuinsLicenseNotification(selection).EncodeBody();
        Assert.Equal(38, body.Length);
        Assert.Equal(Convert.FromHexString("03008F1300000200000001000000991300000200000001000000A31300000200000001000000"), body);
        Assert.Equal(new byte[] { 2 }, new RuinsByteNotification(0x02FB, selection.Remaining).EncodeBody());
    }
}
