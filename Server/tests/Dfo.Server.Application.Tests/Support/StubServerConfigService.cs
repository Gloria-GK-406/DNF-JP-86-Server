using Dfo.Server.Abstractions.Configuration;

namespace Dfo.Server.Application.Tests.Support;

internal sealed class StubServerConfigService(
    IReadOnlyDictionary<string, object>? configuredValues = null) : IServerConfigService
{
    private static readonly IReadOnlyDictionary<string, object> Defaults =
        new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["Game:EndpointHost"] = "192.0.2.20",
            ["GameLogin:Environment"] = (byte)1,
            ["GameLogin:ConnectionPort"] = 10128,
            ["GameLogin:EndpointPortA"] = 17200,
            ["GameLogin:EndpointPortB"] = 17200,
        };

    private readonly IReadOnlyDictionary<string, object> values = configuredValues ?? Defaults;

    public T GetConfig<T>(string key) =>
        values.TryGetValue(key, out var value) && value is T typed
            ? typed
            : throw new InvalidOperationException($"No {typeof(T).Name} configuration for '{key}'.");
}
