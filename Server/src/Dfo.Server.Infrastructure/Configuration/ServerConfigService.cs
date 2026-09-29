using System.ComponentModel;
using Dfo.Server.Abstractions.Configuration;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Infrastructure.Configuration;

public sealed class ServerConfigService : IServerConfigService
{
    private readonly IReadOnlyDictionary<string, string> values;

    public ServerConfigService(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var snapshot = configuration.AsEnumerable()
            .Where(entry => entry.Value is not null)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value!,
                StringComparer.OrdinalIgnoreCase);
        snapshot.TryAdd("Game:DailyResetTime", "06:00:00");
        snapshot.TryAdd("Premium:BlackDiamondEnabled", "true");
        snapshot.TryAdd("Premium:BlackDiamondExtraFatigue", "32");
        snapshot.TryAdd("Fatigue:BaseTotal", "156");
        values = snapshot;
    }

    public T GetConfig<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!values.TryGetValue(key, out var rawValue))
        {
            throw new InvalidOperationException(
                $"Required server configuration '{key}' is missing.");
        }

        if (typeof(T) == typeof(string))
        {
            return (T)(object)rawValue;
        }

        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        try
        {
            object? converted;
            if (targetType.IsEnum)
            {
                converted = Enum.Parse(targetType, rawValue, ignoreCase: true);
            }
            else
            {
                var converter = TypeDescriptor.GetConverter(targetType);
                converted = converter.ConvertFromInvariantString(rawValue);
            }

            return converted is T value
                ? value
                : throw new InvalidOperationException(
                    $"Server configuration '{key}' cannot be converted to {typeof(T).Name}.");
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Server configuration '{key}' cannot be converted to {typeof(T).Name}.",
                exception);
        }
    }
}
