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

    [Fact]
    public void Shared_license_projects_all_three_regions_without_inventing_the_opaque_third_word()
    {
        var selection = new DarkElfRuinsSelection(1, 2, 2, 1, [5007, 5017, 5027]);
        var body = new RuinsLicenseNotification(selection).EncodeBody();
        Assert.Equal(38, body.Length);
        Assert.Equal(Convert.FromHexString("03008F1300000200000000000000991300000200000000000000A31300000200000000000000"), body);
        Assert.Equal(new byte[] { 2 }, new RuinsByteNotification(0x02FB, selection.Remaining).EncodeBody());
    }
}
