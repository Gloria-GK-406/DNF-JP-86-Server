using System.Net;
using System.Net.Sockets;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application;
using Dfo.Server.Application.Commands;
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
using Dfo.Server.Testing;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// A dungeon run, including its unclaimed cards, ends with the selected connection that owns it.
/// </summary>
/// <remarks>
/// This lives on a real host and a real socket because the thing under test is the
/// <b>disconnect path</b> and nothing else. The first fixed-party dungeon implementation has no
/// in-dungeon reconnect contract: the socket goes, the framework notices, and selected-session
/// cleanup must remove that participant and its per-run cards before a replacement connection can
/// select the same character.
/// <para>
/// <b>The two waits order the assertion after teardown.</b> Merely closing the client would race
/// the observer. The test waits until both online-user ownership and the networking client list no
/// longer contain the old connection, then requires the run to be absent.
/// </para>
/// <para>
/// A later connection is deliberately not a recovery token. Connection generation protects the
/// new owner from stale cleanup; it does not resurrect the previous generation's dungeon state.
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
        Cells: new Dictionary<MazeCell, DungeonCellDefinition> { [Cell] = DungeonCellDefinition.Fixed(55611)},
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
    public async Task A_disconnect_ends_the_run_and_a_replacement_cannot_claim_its_cards()
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

            var builder = Host.CreateApplicationBuilder(["--hostBuilder:reloadConfigOnChange=false"]);
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
            builder.Services.AddComposedDungeonRuns();
            builder.Services.AddFixtureCommand<SeedReconnectRunCommand, DungeonRunState, SeedReconnectRunCommandHandler>(FixtureUnitKind.Transactional);

            using var host = builder.Build();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await host.StartAsync(timeout.Token);

            var runs = host.Services.DungeonRuns();
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
                TestGameplay.Run(host.Services, () =>
                {
                    sessions.Select(
                        SelectedSessionIdentity.Create(
                            Account, Character, first, connectionGeneration: 1, gamePort));
                });

                var run = await DomainCommandFixture.Bus(host.Services)
                    .SendAsync(new SeedReconnectRunCommand(), timeout.Token);
                TestGameplay.Run(host.Services, () =>
                {
                    Assert.NotNull(runs.TryOpenClear(Character, run.RunId, run.RoomVersion));
                    Assert.NotNull(
                        runs.FixClearRewardCards(
                            Character, run.RunId, FreeFace(), PaidFace(), PaidFlipCost));
                    Assert.NotNull(runs.CommitSettlement(runs.TryClaimSettlement(Character)!));
                    Assert.NotNull(runs.TryOpenCardLayout(Character, run.RunId));
                });
            }

            // ---- the loss --------------------------------------------------------------------
            await WaitUntilAsync(
                () => !onlineUsers.TryGetByConnection(first, out _), timeout.Token);
            await WaitUntilAsync(() => !gameService.GetClients().Any(), timeout.Token);
            TestGameplay.Run(host.Services, () =>
            {
                Assert.Null(runs.Find(Character));
            });

            // ---- the connection that comes back ----------------------------------------------
            using var reconnected = new TcpClient();
            await reconnected.ConnectAsync(IPAddress.Loopback, gamePort, timeout.Token);
            var second = await ConnectedIdAsync(gameService, onlineUsers, timeout.Token);
            Assert.NotEqual(first, second);
            TestGameplay.Run(host.Services, () =>
            {
                sessions.Select(
                    SelectedSessionIdentity.Create(
                        Account, Character, second, connectionGeneration: 2, gamePort));

                Assert.Null(runs.Find(Character));
                foreach (var kind in new[] { ClearRewardCardKind.Free, ClearRewardCardKind.Paid })
                {
                    foreach (var (index, _) in Expected(kind))
                    {
                        Assert.Null(runs.TryClaimClearRewardCard(Character, kind, index));
                    }
                }
            });

            await host.StopAsync(timeout.Token);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
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

    private static int GetAvailablePort() => TestTcpPorts.Allocate();

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
    public sealed record SeedReconnectRunCommand : ICommand<DungeonRunState>;
    public sealed class SeedReconnectRunCommandHandler(IDungeonRunDomainService runs)
        : ICommandHandler<SeedReconnectRunCommand, DungeonRunState>
    {
        public ValueTask<DungeonRunState> HandleAsync(CommandExecutionContext context, SeedReconnectRunCommand command, CancellationToken cancellationToken)
            => ValueTask.FromResult(runs.Begin(Character, Layout, difficulty: (DungeonDifficulty)1));
    }

}
