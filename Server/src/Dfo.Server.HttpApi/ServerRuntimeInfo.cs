using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.HttpApi;

/// <summary>
/// Server facts reported both by the gate status and by GM <c>server-info</c>. They live here because
/// GmWeb references HttpApi and not the other way round; neither endpoint keeps its own reading.
/// </summary>
public static class ServerRuntimeInfo
{
    private const int DefaultGamePort = 10011;
    private const int DefaultChannelPort = 7001;

    /// <summary>Release number of this build; the build metadata after <c>+</c> (the source revision) is dropped.</summary>
    public static string Version { get; } =
        (typeof(ServerRuntimeInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(ServerRuntimeInfo).Assembly.GetName().Version?.ToString()
            ?? "0.0.0").Split('+')[0];

    /// <summary>
    /// Configured <c>Networking:GamePorts</c>, reported as configured even while networking is off:
    /// entries that are not a port are skipped, and none left means <c>[10011]</c>.
    /// </summary>
    public static IReadOnlyList<int> GamePorts(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var ports = configuration.GetSection("Networking:GamePorts").GetChildren()
            .Select(child => Port(child.Value)).OfType<int>().ToArray();
        return ports.Length == 0 ? [DefaultGamePort] : ports;
    }

    /// <summary>Configured <c>Networking:ChannelPort</c>: absent means 7001, present but not a port means null.</summary>
    public static int? ChannelPort(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration["Networking:ChannelPort"] is { } raw ? Port(raw) : DefaultChannelPort;
    }

    private static int? Port(string? raw) =>
        int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535
            ? port
            : null;
}
