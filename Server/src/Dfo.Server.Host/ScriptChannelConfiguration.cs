using System.Globalization;
using System.Text;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Host;

/// <summary>Projects one startup script snapshot into matching public channels and listeners.</summary>
internal static class ScriptChannelConfiguration
{
    public static void Apply(ConfigurationManager configuration)
    {
        if (!configuration.GetValue<bool>("Channel:UseScriptCatalog"))
            return;

        var configuredPath = configuration["Channel:ScriptPath"] ?? "Resources/Channel/channel_info.etc";
        var path = Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
        // Latin1, as the channel content reader decodes the same file; every field read here is ASCII.
        var channels = ChannelScript.Listeners(ChannelScript.Parse(Encoding.Latin1.GetString(File.ReadAllBytes(path))),
            configuration.GetValue<bool>("Channel:IncludePvpChannels"));
        var host = configuration["Channel:PublicServerIp"] ?? "127.0.0.1";
        // Replace both arrays completely; configuration providers otherwise merge array indices.
        var values = configuration.AsEnumerable()
            .Where(pair => !pair.Key.StartsWith("Channel:Channels", StringComparison.OrdinalIgnoreCase)
                && !pair.Key.StartsWith("Networking:GamePorts", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < channels.Count; index++)
        {
            var channel = channels[index];
            var prefix = $"Channel:Channels:{index}";
            values[$"{prefix}:Id"] = channel.Id.ToString(CultureInfo.InvariantCulture);
            values[$"{prefix}:Name"] = $"#ch.{channel.Id}";
            values[$"{prefix}:Host"] = host;
            values[$"{prefix}:Port"] = (10000 + channel.Id).ToString(CultureInfo.InvariantCulture);
            values[$"{prefix}:MaximumUsers"] = channel.MaximumUsers.ToString(CultureInfo.InvariantCulture);
            values[$"{prefix}:LoginEnvironment"] = channel.LoginEnvironment.ToString(CultureInfo.InvariantCulture);
            values[$"Networking:GamePorts:{index}"] = values[$"{prefix}:Port"];
        }

        configuration.Sources.Clear();
        configuration.AddInMemoryCollection(values);
    }
}
