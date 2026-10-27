using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Characters;
using Dfo.Server.Networking.Tests.Characterization;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// AC-007, shutdown: a player still in town when the host stops keeps the place it was standing,
/// because teardown writes it back before the executor stops (RQ-009).
/// </summary>
[Collection(CharacterizationCollection.Name)]
public sealed class ShutdownPositionPersistenceTests
{
    private const short SentinelX = 4321;
    private const short SentinelY = 1234;

    [PvfFact]
    public async Task A_player_in_town_when_the_host_stops_has_the_runtime_position_written_back()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        var stopped = false;
        try
        {
            using var player = await server.EnterWorldAsync("player", "shutdown-player", "Stopper", timeout.Token);
            var host = HostOf(server);
            var (characterId, _) = await ReadPositionAsync(server.DatabasePath, timeout.Token);
            // Read before stopping: afterwards the executor takes no more work.
            var runtime = TestGameplay.Run(host.Services, () => host.Services
                .GetRequiredService<IOnlineCharacterRuntimeDomainService>().PositionOf(new CharacterId(characterId)));
            Assert.NotNull(runtime);
            Assert.False(runtime!.X == SentinelX && runtime.Y == SentinelY);

            // Justified direct write: only teardown writes the runtime position back, so the sentinel
            // being overwritten is what shows that teardown ran and committed during shutdown.
            await server.ExecuteSqlAsync(
                $"UPDATE characters SET pos_x={SentinelX}, pos_y={SentinelY} WHERE character_id={characterId}",
                timeout.Token);

            stopped = true;
            await host.StopAsync(timeout.Token);

            var (_, stored) = await ReadPositionAsync(server.DatabasePath, timeout.Token);
            Assert.Equal(runtime, stored);
        }
        finally
        {
            try
            {
                await server.DisposeAsync();
            }
            // Disposal still runs; it only repeats the stop, which TouchSocket refuses once stopped.
            catch (AggregateException error) when (stopped && error.InnerExceptions.All(x => x is ObjectDisposedException))
            {
            }
        }
    }

    // CharacterizationHost keeps its host private and stops it only on disposal, which also deletes
    // the database this test has to read afterwards.
    private static IHost HostOf(CharacterizationHost server) =>
        (IHost)typeof(CharacterizationHost).GetField("host", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(server)!;

    private static async Task<(int Id, CharacterPositionState Position)> ReadPositionAsync(
        string path, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT character_id, town_id, area_id, pos_x, pos_y, direction FROM characters WHERE delete_flag=0";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));
        var row = (reader.GetInt32(0), new CharacterPositionState(
            checked((byte)reader.GetInt32(1)), checked((byte)reader.GetInt32(2)),
            checked((short)reader.GetInt32(3)), checked((short)reader.GetInt32(4)), checked((byte)reader.GetInt32(5))));
        Assert.False(await reader.ReadAsync(cancellationToken));
        return row;
    }
}
