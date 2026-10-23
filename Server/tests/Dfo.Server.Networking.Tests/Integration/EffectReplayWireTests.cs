using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Dfo.Server.Networking.Tests.Characterization;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// AC-009, the A half's item 3 (TASK-31 section 5.3): a client that resends an effect request it already had
/// settled, on the same connection and with the same frame sequence, gets a replay through the real TCP path:
/// no second settlement, no second charge, and the connection stays open.
/// </summary>
/// <remarks>
/// The request is USE_STACKABLE on a lucky star consumable (10002998), the real PVF item
/// <c>LuckyStarRentalWireTests</c> uses: one use credits the account one star and takes one from the stack.
/// The host is <see cref="CharacterizationHost"/>, used as it is. The game protocol cannot put an item into a
/// bag, so the stack comes from the character initialization setting, written once into SQLite before the
/// character is created: that is the server's own way of giving a new character items.
/// </remarks>
[Collection(CharacterizationCollection.Name)]
public sealed class EffectReplayWireTests
{
    private const int LuckyStar = 10002998;
    private const ushort UseStackable = 0x002C;

    [PvfFact]
    public async Task Resending_a_committed_effect_request_on_the_same_connection_replays_without_a_second_charge_and_keeps_the_connection()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        await server.ExecuteSqlAsync(
            "INSERT INTO initialization_settings(kind,revision,input,updated_at,updated_by) VALUES('character',1," +
            $"'{{\"Level\":1,\"PersonalCargoLevel\":8,\"Items\":[{{\"ItemId\":{LuckyStar},\"Count\":2}}]}}',0,'EffectReplayWireTests');",
            timeout.Token);
        var (mId, ticket) = await server.RegisterAndIssueTicketAsync("effect-replay", timeout.Token);
        var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(IPAddress.Loopback, server.GamePort, timeout.Token);
        using var player = new WireClient("player", tcp);
        await player.LoginAsync(mId, ticket, timeout.Token);
        await player.CreateCharacterAsync("replayer", timeout.Token);
        await player.SelectCharacterAsync(timeout.Token);
        var (slot, before) = Stack(server);
        Assert.Equal(2, before);
        Assert.Equal(0, LuckyStars(server));
        var body = new byte[11];
        BinaryPrimitives.WriteInt16LittleEndian(body, slot);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7), LuckyStar);

        await player.SendAsync(UseStackable, body, timeout.Token);
        var sequence = SequenceOf(player);
        var answered = await player.ReadUntilAsync(frame => frame.Is(0x01, UseStackable), timeout.Token);
        await player.FenceAsync(timeout.Token);
        Assert.Equal(1, Stack(server).Count);
        Assert.Equal(1, LuckyStars(server));

        // The same request again, with the sequence it first went out with: the client resending a request
        // whose answer it did not see.
        var seen = player.Frames.Count();
        await tcp.GetStream().WriteAsync(Frame(UseStackable, sequence, body), timeout.Token);
        await player.FenceAsync(timeout.Token);
        var replayed = player.Frames.Skip(seen).ToArray();

        Assert.DoesNotContain(replayed, frame => frame.Is(0x01, UseStackable));
        Assert.Contains(replayed, frame => frame.IsNoti);
        Assert.Equal(1, Stack(server).Count);
        Assert.Equal(1, LuckyStars(server));
        Assert.True(answered.Body.Length > 0);
        await player.SendAsync(0x0003, [], timeout.Token);
        var exit = await player.ReadUntilAsync(frame => frame.Is(0x01, 0x0003), timeout.Token);
        Assert.Equal([1], exit.Body.ToArray());
        Assert.False(player.IsClosed);
    }

    /// <summary>The sequence of the frame <paramref name="client"/> sent last; the client keeps it private.</summary>
    private static ushort SequenceOf(WireClient client)
    {
        var field = typeof(WireClient).GetField("sequence", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (ushort)field.GetValue(client)!;
    }

    private static byte[] Frame(ushort type, ushort sequence, byte[] body)
    {
        var frame = new byte[14 + body.Length];
        frame[0] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), checked((uint)frame.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(12), sequence);
        body.CopyTo(frame.AsSpan(14));
        return frame;
    }

    private static (short Slot, int Count) Stack(CharacterizationHost server)
    {
        using var connection = new SqliteConnection($"Data Source={server.DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT s.slot_index, s.count FROM item_slots s JOIN items i ON s.core_uid=i.item_uid " +
            "WHERE s.owner_scope='character' AND s.list_type=0 AND substr(hex(i.item_core),3,8)='" +
            Convert.ToHexString(BitConverter.GetBytes(LuckyStar)) + "'";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read(), "the lucky star stack is not in the bag");
        var stack = (checked((short)reader.GetInt64(0)), checked((int)reader.GetInt64(1)));
        Assert.False(reader.Read(), "the lucky stars are in more than one slot");
        return stack;
    }

    private static long LuckyStars(CharacterizationHost server)
    {
        using var connection = new SqliteConnection($"Data Source={server.DatabasePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT lucky_star FROM accounts";
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
