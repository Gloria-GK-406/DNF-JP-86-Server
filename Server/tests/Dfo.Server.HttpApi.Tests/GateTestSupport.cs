using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.HttpApi.Tests;

/// <summary>Gate options read from an in-memory configuration only, never from the process environment.</summary>
internal static class GateTestOptions
{
    public static GateOptions Create(string? token, IDictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?> { [GateOptions.TokenKey] = token };
        if (extra is not null)
        {
            foreach (var (key, value) in extra) values[key] = value;
        }

        return GateOptions.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset now = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

internal sealed class GateLogCapture : ILoggerProvider
{
    private readonly List<string> messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (messages) return messages.ToArray();
        }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(messages);

    public void Dispose() { }

    private sealed class Logger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (messages) messages.Add($"{logLevel}: {formatter(state, exception)} {exception}");
        }
    }
}
