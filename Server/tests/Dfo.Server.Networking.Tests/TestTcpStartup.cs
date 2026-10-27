using System.Collections.Concurrent;
using System.Net;
using Dfo.Server.Networking.Configuration;
using Dfo.Server.Networking.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TouchSocket.Sockets;

namespace Dfo.Server.Networking.Tests;

internal static class TestTcpStartup
{
    internal static void AssertReady(IServiceProvider services, TestTcpStartupLog logs,
        int requestedGamePort, int? httpPort = null)
    {
        var options = services.GetRequiredService<NetworkingOptions>();
        var channel = services.GetRequiredService<IChannelTcpService>();
        var game = services.GetRequiredService<IGameTcpService>();
        var channelEndpoints = Endpoints(channel);
        var gameEndpoints = Endpoints(game);
        Assert.True(options.Enabled && options.GamePorts.Contains(requestedGamePort)
            && Ready(channel, channelEndpoints, [options.ChannelPort])
            && Ready(game, gameEndpoints, options.GamePorts),
            $"TCP startup did not bind the configured listeners. "
            + $"Channel={options.ChannelPort}; Game=[{string.Join(",", options.GamePorts)}]; "
            + $"requestedGame={requestedGamePort}; HTTP={httpPort?.ToString() ?? "none"}. "
            + $"Channel state={channel.ServerState}, endpoints=[{Describe(channelEndpoints)}]; "
            + $"Game state={game.ServerState}, endpoints=[{Describe(gameEndpoints)}].\n"
            + logs.Snapshot());
    }

    private static bool Ready(ITcpService service, EndPoint?[] endpoints, IReadOnlyList<int> ports) =>
        service.ServerState == ServerState.Running && endpoints.Length == ports.Count
        && endpoints.OfType<IPEndPoint>().Where(endpoint => endpoint.Address.Equals(IPAddress.Any))
            .Select(endpoint => endpoint.Port).Order().SequenceEqual(ports.Order());

    private static EndPoint?[] Endpoints(ITcpService service) => service.Monitors.Select(monitor =>
    {
        try { return monitor.Socket.LocalEndPoint; }
        catch (ObjectDisposedException) { return null; }
    }).ToArray();

    private static string Describe(EndPoint?[] endpoints) =>
        string.Join(",", endpoints.Select(endpoint => endpoint?.ToString() ?? "<unbound/disposed>"));
}

/// <summary>Retains the actual startup exception, including errors swallowed by ServiceHost.</summary>
internal sealed class TestTcpStartupLog : ILoggerProvider
{
    private readonly ConcurrentQueue<string> messages = new();

    public ILogger CreateLogger(string categoryName) => new Capture(this, categoryName);
    public void Dispose() { }
    internal string Snapshot() => string.Join(Environment.NewLine, messages);

    internal async Task WaitForAsync(string expected, CancellationToken cancellationToken)
    {
        while (!messages.Any(message => message.Contains(expected, StringComparison.Ordinal)))
            await Task.Delay(10, cancellationToken);
    }

    private sealed class Capture(TestTcpStartupLog owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => owner.messages.Enqueue(
                $"{level}: {category}: {formatter(state, exception)}"
                + (exception is null ? "" : Environment.NewLine + exception));
    }
}
