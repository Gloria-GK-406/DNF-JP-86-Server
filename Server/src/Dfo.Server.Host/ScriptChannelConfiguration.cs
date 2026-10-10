using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Host;

/// <summary>Projects one startup script snapshot into matching public channels and listeners.</summary>
internal static partial class ScriptChannelConfiguration
{
    public static void Apply(ConfigurationManager configuration)
    {
        if (!configuration.GetValue<bool>("Channel:UseScriptCatalog"))
            return;

        var configuredPath = configuration["Channel:ScriptPath"] ?? "Resources/Channel/channel_info.etc";
        var path = Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
        var channels = Parse(File.ReadAllText(path), configuration.GetValue<bool>("Channel:IncludePvpChannels"));
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
            values[$"{prefix}:MaximumUsers"] = (channel.Id is 20 or 21 ? 150 : channel.Id == 200 ? 250 : 100).ToString(CultureInfo.InvariantCulture);
            values[$"{prefix}:LoginEnvironment"] = channel.LoginEnvironment.ToString(CultureInfo.InvariantCulture);
            values[$"Networking:GamePorts:{index}"] = values[$"{prefix}:Port"];
        }

        configuration.Sources.Clear();
        configuration.AddInMemoryCollection(values);
    }

    internal static IReadOnlyList<ScriptChannel> Parse(string script, bool includePvp)
    {
        var result = new List<ScriptChannel>();
        var ids = new HashSet<int>();
        var inServer = false;
        var group = -1;
        foreach (var raw in script.Split('\n'))
        {
            var line = raw.Trim();
            if (line == "[server]") { inServer = true; group = -1; continue; }
            if (line == "[/server]") { inServer = false; continue; }
            if (!inServer) continue;
            if (group == -1)
            {
                if (int.TryParse(line, out var parsedGroup)) group = parsedGroup;
                continue;
            }
            if (group != 1) continue;
            var match = ChannelRow().Match(line);
            if (!match.Success) continue;
            var id = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var type = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var category = match.Groups[3].Value;
            if (id is < 1 or > 255 || type is < 0 or > 255
                || (category == "[raid]" && type != 23)) continue;
            var pvp = type is 8 or 13 or 24;
            if (pvp && !includePvp) continue;
            if (ids.Add(id)) result.Add(new ScriptChannel(id, (byte)(pvp || type == 23 ? type : 1)));
        }
        if (result.Count == 0)
            throw new InvalidOperationException("Channel script has no supported channels in server group 1.");
        return result;
    }

    [GeneratedRegex(@"^(\d+)\s+`[^`]*`\s+(\d+)\s+`([^`]*)`")]
    private static partial Regex ChannelRow();

    internal sealed record ScriptChannel(int Id, byte LoginEnvironment);
}
