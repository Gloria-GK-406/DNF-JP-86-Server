using Dfo.Server.Abstractions.Execution;
using static Dfo.Server.Networking.Tests.Integration.LifecycleWireHost;

namespace Dfo.Server.Networking.Tests.Integration;

/// <summary>
/// AC-007, shutdown order (RQ-009): stopping the host with players still connected runs every
/// teardown as a lifecycle CMD while the executor still runs, and only then stops the executor.
/// </summary>
public sealed class ShutdownOrderWireTests
{
    [Fact]
    public async Task Stopping_the_host_runs_every_open_connections_teardown_on_the_executor_before_it_stops()
    {
        const int players = 3;
        var server = await LifecycleWireHost.StartAsync();
        try
        {
            // A slow teardown write, so a stop that does not wait for it would show.
            server.Recorder.DisconnectDelay = TimeSpan.FromMilliseconds(200);
            var connected = new List<Dfo.Server.Data.Networking.NetworkConnectionId>();
            for (var i = 0; i < players; i++) connected.Add((await server.ConnectGameAsync()).Id);
            var executor = server.Executor;

            await server.StopAsync(server.Timeout);

            Assert.True(executor.Completion.IsCompleted);
            var teardowns = server.Recorder.Events.Where(x => x.Kind == "disconnect-enter").ToArray();
            Assert.Equal(connected.Select(x => x.Value).Order(), teardowns.Select(x => x.Connection.Value).Order());
            Assert.All(teardowns, teardown =>
            {
                Assert.True(teardown.OnExecutor);
                var chain = Assert.NotNull(teardown.Chain);
                Assert.Equal(GameplayCallSource.Lifecycle, chain.Source);
                Assert.Equal("lifecycle:disconnected", chain.CommandType);
                Assert.Equal(teardown.Connection, chain.SourceConnection);
                Assert.False(teardown.Flag, "the executor had stopped before this teardown ran");
            });
            Assert.Equal(players, server.Recorder.Events.Count(x => x.Kind == "disconnect-exit"));
            Assert.Equal(0, server.Log.Count("did not run"));
            Assert.Equal(0, server.Log.Count("did not drain within"));
            Assert.Equal(1, server.Log.Count("The gameplay executor stopped after draining its producers"));
        }
        finally
        {
            await server.DisposeAsync();
        }
    }
}
