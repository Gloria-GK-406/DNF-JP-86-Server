using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Persistence;
using Dfo.Server.Protocol;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TouchSocketSessionClient = TouchSocket.Sockets.ITcpSessionClient;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// The cards of a clear outlive the connection that was shown them.
/// </summary>
/// <remarks>
/// This lives on a real host and a real socket because the thing under test is the
/// <b>disconnect path</b> and nothing else. That a face asked for twice answers the same way is
/// settled by the domain tests; what those cannot settle is whether the server tears the run down
/// when the transport goes away, because a unit test that drops an object reference is asserting
/// its own fixture. The connection here is closed the way a player's is -- the socket goes, the
/// framework notices, and every registered connection observer runs -- and only then does the test
/// ask what the cards hold.
/// <para>
/// <b>What orders this is the reconnect, not the two waits above it.</b> Observing that the run
/// survived would prove nothing if the teardown had not run yet, and the waits were written to
/// say so out loud. They are not what makes it hold, though: <c>ConnectedIdAsync</c> already
/// refuses to name a connection until exactly one client is attached, and with the second socket
/// already connected the count cannot come back to one until the lost one is off the service's
/// list. A count that came back early would name the first connection over again and fail the
/// assertion that the two differ, so there is no reading of it that passes quietly. Deleting both
/// waits leaves this test red against a server that ends the run on disconnect -- three runs out
/// of three, measured rather than argued. They stay as defence, and as the requirement written
/// where it can be read.
/// </para>
/// <para>
/// <b>Where in the observer a run gets ended does not matter to this.</b> An <c>End</c> written
/// into the disconnect observer's first branch and one written after its last statement both turn
/// this red, so what is pinned is the disconnect path as a whole rather than one line of it.
/// </para>
/// </remarks>
public sealed class CardRewardReconnectIntegrationTests
{
    private static readonly MazeCell Cell = new(0, 0);

    private static readonly DungeonLayout Layout = new(
        DungeonId: 144,
        MazeIndex: 0,
        Width: 1,
        Height: 1,
        StartCell: Cell,
        BossCell: Cell,
        StartRoomMapId: 55611,
        Seed: 20260901,
        Rooms: new Dictionary<MazeCell, int> { [Cell] = 55611 },
        BasisLevel: 5,
        ExperienceWeight: 1.45);

    private static readonly CharacterId Character = new(4242);
    private static readonly AccountId Account = new(10038);

    /// <summary>A piece this server has actually seen a dungeon declare.</summary>
    private const int FreePieceId = 10088621;

    /// <summary>A piece dungeon 120 names in its own card declaration.</summary>
    private const int PaidPieceId = 3329;

    /// <summary>What the fixture's clear quoted for a paid flip, fixed with the faces.</summary>
    private const int PaidFlipCost = 580;

    [Fact]
    public async Task A_clears_cards_survive_the_connection_that_was_shown_them()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-reconnect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var channelScriptPath = Path.Combine(directory, "channel_info.etc");
            await File.WriteAllBytesAsync(channelScriptPath, [0x01]);
            var channelPort = GetAvailablePort();
            var gamePort = GetAvailablePort();
            while (gamePort == channelPort)
            {
                gamePort = GetAvailablePort();
            }

            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Networking:Enabled"] = "true",
                ["Networking:ChannelPort"] = channelPort.ToString(),
                ["Networking:GamePorts:0"] = gamePort.ToString(),
                ["Database:Path"] = Path.Combine(directory, "reconnect.db"),
                ["Channel:ScriptPath"] = channelScriptPath,
                ["Game:EndpointHost"] = "127.0.0.1",
            });
            builder.Services.AddSingleton<TimeProvider>(
                new FixedTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)));
            new InfrastructureModule().ConfigureServices(builder.Services, builder.Configuration);
            new PersistenceModule().ConfigureServices(builder.Services, builder.Configuration);
            new ApplicationModule().ConfigureServices(builder.Services, builder.Configuration);
            new NetworkingModule().ConfigureServices(builder.Services, builder.Configuration);
            new ProtocolModule().ConfigureServices(builder.Services, builder.Configuration);

            using var host = builder.Build();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await host.StartAsync(timeout.Token);

            var runs = host.Services.GetRequiredService<IDungeonRunDomainService>();
            var sessions = host.Services.GetRequiredService<ISelectedSessionDomainService>();
            var onlineUsers = host.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            var gameService = host.Services.GetRequiredService<IGameTcpService>();

            // ---- the connection that cleared the dungeon -------------------------------------
            NetworkConnectionId first;
            using (var client = new TcpClient())
            {
                await client.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
                first = await ConnectedIdAsync(gameService, onlineUsers, timeout.Token);

                // Whose connection this is. Registered directly rather than driven through login
                // and character selection over the wire: that flow needs a seeded roster and
                // proves nothing this test is about. What has to be real is the loss of the
                // socket, and it is -- this is the directory the disconnect observer reads to
                // find out whose connection just went away.
                sessions.Select(
                    SelectedSessionIdentity.Create(
                        Account, Character, first, connectionGeneration: 1, gamePort));

                Assert.NotNull(runs.Begin(Character, Layout, difficulty: 1));
                Assert.NotNull(runs.TrySettle(Character));
                Assert.NotNull(
                    runs.FixClearRewardCards(Character, FreeFace(), PaidFace(), PaidFlipCost));
            }

            var shown = Snapshot(runs);

            // ---- the loss --------------------------------------------------------------------
            await WaitUntilAsync(
                () => !onlineUsers.TryGetByConnection(first, out _), timeout.Token);
            await WaitUntilAsync(() => !gameService.GetClients().Any(), timeout.Token);

            // ---- the connection that comes back ----------------------------------------------
            using var reconnected = new TcpClient();
            await reconnected.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
            var second = await ConnectedIdAsync(gameService, onlineUsers, timeout.Token);
            Assert.NotEqual(first, second);
            sessions.Select(
                SelectedSessionIdentity.Create(
                    Account, Character, second, connectionGeneration: 2, gamePort));

            // The run is still this character's, and its faces are the ones they were shown.
            Assert.Equal(shown, Snapshot(runs));

            // and asking for a position hands over what that position held before the loss, which
            // is the requirement rather than the mere survival of the record.
            foreach (var kind in new[] { ClearRewardCardKind.Free, ClearRewardCardKind.Paid })
            {
                foreach (var (index, piece) in Expected(kind))
                {
                    var taken = runs.TryClaimClearRewardCard(Character, kind, index);

                    Assert.NotNull(taken);
                    Assert.Equal(index, taken!.Index);
                    Assert.Equal(piece?.ItemId, taken.Item?.ItemId);
                    Assert.Equal(piece?.Quantity, taken.Item?.Quantity);
                    Assert.Equal(piece?.Rarity, taken.Item?.Rarity);
                    Assert.Equal(piece?.Durability, taken.Item?.Durability);
                }
            }

            await host.StopAsync(timeout.Token);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>What the two faces hold, position by position, written out as plain values.</summary>
    /// <remarks>
    /// Values rather than the records themselves. Comparing the records would compare two
    /// references to one object and pass however the cards had been rebuilt; this cannot, because
    /// a face redrawn on reconnect would carry different numbers even where it carried a face of
    /// the same shape. The price rides along for the same reason it is fixed with the faces.
    /// </remarks>
    private static string Snapshot(IDungeonRunDomainService runs)
    {
        var run = runs.Find(Character);
        Assert.NotNull(run);
        var cards = run!.ClearRewardCards;
        Assert.NotNull(cards);

        var lines = new List<string> { $"price={cards!.PaidFlipCost}" };
        foreach (var kind in new[] { ClearRewardCardKind.Free, ClearRewardCardKind.Paid })
        {
            foreach (var slot in cards.FaceOf(kind).Slots)
            {
                lines.Add(
                    $"{kind}:{slot.Index}="
                    + (slot.Item is { } item
                        ? $"{item.ItemId}x{item.Quantity}/{item.Rarity}/{item.Durability}"
                        : "-"));
            }
        }

        return string.Join('\n', lines);
    }

    private static IEnumerable<(int Index, ClearRewardCardItem? Piece)> Expected(
        ClearRewardCardKind kind)
    {
        foreach (var slot in (kind == ClearRewardCardKind.Paid ? PaidFace() : FreeFace()).Slots)
        {
            yield return (slot.Index, slot.Item);
        }
    }

    /// <summary>
    /// The free face, with a hole in it on purpose.
    /// </summary>
    /// <remarks>
    /// Distinct pieces at distinct positions: "the same thing at the same position" cannot be told
    /// from "something at every position" when every position holds the same thing. The empty
    /// positions travel too -- a position that held nothing has to still hold nothing.
    /// </remarks>
    private static ClearRewardCardFace FreeFace() =>
        new(
            ClearRewardCardKind.Free,
            [
                new ClearRewardCardSlot(
                    0, new ClearRewardCardItem(FreePieceId, 1, DropRarity.Rare, 25)),
                new ClearRewardCardSlot(1, null),
                new ClearRewardCardSlot(
                    2, new ClearRewardCardItem(FreePieceId + 1, 3, DropRarity.Common, 40)),
                new ClearRewardCardSlot(3, null),
            ],
            FaceGold);

    /// <summary>What the hand-written faces say a flip of them pays.</summary>
    private const int FaceGold = 37;

    /// <summary>The paid face, whose holes are at the other positions.</summary>
    private static ClearRewardCardFace PaidFace() =>
        new(
            ClearRewardCardKind.Paid,
            [
                new ClearRewardCardSlot(0, null),
                new ClearRewardCardSlot(
                    1, new ClearRewardCardItem(PaidPieceId, 2, DropRarity.Unique, 12)),
                new ClearRewardCardSlot(2, null),
                new ClearRewardCardSlot(
                    3, new ClearRewardCardItem(PaidPieceId + 7, 1, DropRarity.Epic, 60)),
            ],
            FaceGold);

    private static async Task<NetworkConnectionId> ConnectedIdAsync(
        IGameTcpService gameService,
        IOnlineUserQueryDomainService onlineUsers,
        CancellationToken cancellationToken)
    {
        await WaitUntilAsync(() => gameService.GetClients().Count() == 1, cancellationToken);
        var session = Assert.IsAssignableFrom<TouchSocketSessionClient>(
            gameService.GetClients().Single());
        var connectionId = new NetworkConnectionId($"Game:{session.Id}");
        await WaitUntilAsync(
            () => onlineUsers.TryGetByConnection(connectionId, out _), cancellationToken);
        return connectionId;
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
