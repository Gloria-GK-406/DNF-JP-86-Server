using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure;
using Dfo.Server.Networking;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Persistence;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Protocol.Game;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TouchSocketSessionClient = TouchSocket.Sockets.ITcpSessionClient;

namespace Dfo.Server.Protocol.Tests.Game;

/// <summary>
/// A real server, over a real socket, moving a real row -- and the bytes that come back.
/// </summary>
/// <remarks>
/// This is the walkthrough of the item path, done by a program instead of a person. Everything
/// between the socket and the database is the production article: TouchSocket's framing, the
/// decoder registry, the message handler, the command bus with its unit of work, the character's
/// own container over the SQLite store, and the encoder that writes the acknowledgement.
///
/// It is what the other item tests cannot reach. The codec tests encode a response object; the
/// handler tests drive a fake inventory; even the byte-level end-to-end test opens the use case
/// boundary by hand. Only here does the boundary come from the pipeline deciding that this command
/// is one that needs it -- which is exactly what changed when the two phase commands became
/// transactional.
///
/// What it still does not cover is the client: how the game draws what these bytes describe is not
/// something a socket can be asked.
/// </remarks>
public sealed class ItemPathLiveTests
{
    private const int PotionId = 8474;

    /// <summary>The live drag, captured on 2026-08-18: quick slot named as source, potion's as destination.</summary>
    private static readonly byte[] ObservedRequestBody =
    [
        0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x41, 0x00, 0x1A, 0x21,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    [Fact]
    public async Task AnItemDraggedOverASocketLandsInTheDatabaseAndIsAcknowledgedByteForByte()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-live-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "live.db");
        try
        {
            var channelScriptPath = Path.Combine(directory, "channel_info.etc");
            await File.WriteAllBytesAsync(channelScriptPath, [0x01]);
            var channelPort = FreePort();
            var gamePort = FreePort();
            while (gamePort == channelPort)
            {
                gamePort = FreePort();
            }

            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Networking:Enabled"] = "true",
                ["Networking:ChannelPort"] = channelPort.ToString(),
                ["Networking:GamePorts:0"] = gamePort.ToString(),
                ["Database:Path"] = databasePath,
                ["Channel:ScriptPath"] = channelScriptPath,
                ["Game:EndpointHost"] = "192.0.2.20",
            });
            new InfrastructureModule().ConfigureServices(builder.Services, builder.Configuration);
            new PersistenceModule().ConfigureServices(builder.Services, builder.Configuration);
            new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
            new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);
            new ProtocolModule().ConfigureServices(builder.Services, builder.Configuration);

            using var host = builder.Build();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await host.StartAsync(timeout.Token);

            // The character and its potion exist before anyone connects, exactly as they would
            // after an earlier session put them there. Asking for a repository is what puts the
            // database in shape -- the stores declare no table of their own, so a host that has
            // resolved nothing else has nothing for them to write into.
            _ = host.Services.GetRequiredService<ICharacterRepository>();
            await SeedAsync(databasePath);

            var gameService = host.Services.GetRequiredService<IGameTcpService>();
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
            await WaitUntilAsync(() => gameService.GetClients().Count() == 1, timeout.Token);
            var session = Assert.IsAssignableFrom<TouchSocketSessionClient>(
                gameService.GetClients().Single());
            var connectionId = new NetworkConnectionId($"Game:{session.Id}");

            Bind(host.Services, connectionId);

            await using var stream = client.GetStream();
            await stream.WriteAsync(
                Frame(GameProtocol.MoveItemSpaceType, ObservedRequestBody), timeout.Token);
            // The connection is greeted the moment it opens, so the acknowledgement is not the
            // first thing on the wire. Read past whatever is not it.
            var acknowledgement = await ReadFrameOfTypeAsync(
                stream, GameProtocol.MoveItemSpaceType, timeout.Token);

            Assert.Equal(
                GameServerFrameEncoder.Encode(
                    0x01,
                    GameProtocol.MoveItemSpaceType,
                    [0x01, 0x00, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x41, 0x00]),
                acknowledgement);
            Assert.Equal([(3, PotionId, 1)], await ReadRowsAsync(databasePath));

            await host.StopAsync(timeout.Token);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }
        }
    }

    /// <summary>
    /// Gives the connection the character it would have after selecting one, and the character the
    /// container entry would have registered.
    /// </summary>
    /// <remarks>
    /// Selecting a character is a seven-command workflow of its own with its own tests; driving it
    /// over the socket here would test that instead of this. What this needs from it is only its
    /// two outcomes, so those are established directly.
    /// </remarks>
    private static void Bind(IServiceProvider services, NetworkConnectionId connectionId)
    {
        var character = new CharacterId(60);
        services.GetRequiredService<ICharacterContainerDomainService>().Register(
            character,
            new PersistentItemContainer(
                ContainerId.ForCharacter(character, InventoryContainerType.Main),
                character,
                new ContainerShape(InventoryContainerType.Main, 0),
                services.GetRequiredService<IItemStore>(),
                services.GetRequiredService<IDomainTransactionAccessor>(),
                null!));
        services.GetRequiredService<ISelectedSessionDirectory>().RegisterReplacing(
            new SelectedSessionIdentity(
                new AccountId(6), character, connectionId, 0, WireUserId: 60, ListenerPort: 0));
    }

    private static byte[] Frame(ushort type, byte[] body)
    {
        var frame = new byte[GameClientFrameRequestInfo.HeaderLength + body.Length];
        frame[0] = 0x01;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), type);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(3), (uint)frame.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(7), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(11), 1);
        body.CopyTo(frame, GameClientFrameRequestInfo.HeaderLength);
        return frame;
    }

    private static async Task<byte[]> ReadFrameOfTypeAsync(
        NetworkStream stream, ushort type, CancellationToken token)
    {
        while (true)
        {
            var frame = await ReadFrameAsync(stream, token);
            if (BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)) == type)
            {
                return frame;
            }
        }
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken token)
    {
        var header = new byte[GameServerFrameEncoder.HeaderLength];
        await stream.ReadExactlyAsync(header, token);
        var total = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(3, 4));
        var frame = new byte[checked((int)total)];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(header.Length), token);
        return frame;
    }

    private static async Task SeedAsync(string databasePath)
    {
        await using var connection = Open(databasePath);
        await using (var character = connection.CreateCommand())
        {
            character.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'live')";
            await character.ExecuteNonQueryAsync();
        }

        await using var item = connection.CreateCommand();
        item.CommandText =
            "INSERT INTO character_new_items(owner_scope,owner_id,character_id,list_type,"
            + "slot_index,item_core) VALUES('character',60,60,0,65,@core)";
        item.Parameters.AddWithValue(
            "@core", InventoryItemCore.Encode(Core()));
        await item.ExecuteNonQueryAsync();
    }

    private static async Task<List<(int Slot, int ItemId, int Count)>> ReadRowsAsync(string databasePath)
    {
        await using var connection = Open(databasePath);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT slot_index,item_core FROM character_new_items "
            + "WHERE character_id=60 AND list_type=0 ORDER BY slot_index";
        var rows = new List<(int, int, int)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var core = InventoryItemCore.Decode((byte[])reader.GetValue(1));
            rows.Add((reader.GetInt32(0), core.ItemId, core.Value));
        }

        return rows;
    }

    private static InventoryItemCoreState Core() =>
        new(2, PotionId, 1, 0, 0, 0, 0, 0, 0, 0, -1,
            new ChronicleOptionState(0, 0, 0, 0, 0), new ChronicleOptionState(0, 0, 0, 0, 0),
            0, 0, 0, 0, 0,
            new RandomOptionState(0, 0, 0), new RandomOptionState(0, 0, 0),
            new RandomOptionState(0, 0, 0), 0, byte.MaxValue, 0, new RandomOptionState(0, 0, 0),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        return connection;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken token)
    {
        while (!condition())
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(25, token);
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
