using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Host.Tests;

public sealed class ScriptChannelConfigurationTests
{
    [Fact]
    public void Shipped_script_matches_upstream_default_channel_scope()
    {
        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources/Channel/channel_info.etc"));
        var channels = ScriptChannelConfiguration.Parse(script, includePvp: false);
        Assert.Equal(new[] { 1, 6, 10, 11, 12, 13, 14, 15, 16, 17, 18, 20, 21, 19, 100, 200 },
            channels.Select(channel => channel.Id));
        Assert.Equal(23, channels.Single(channel => channel.Id == 200).LoginEnvironment);
        Assert.All(channels.Where(channel => channel.Id != 200), channel => Assert.Equal(1, channel.LoginEnvironment));
        var all = ScriptChannelConfiguration.Parse(script, includePvp: true);
        Assert.Equal(28, all.Count);
        Assert.DoesNotContain(all, channel => channel.Id == 201);
        Assert.Equal(13, all.Single(channel => channel.Id == 68).LoginEnvironment);
    }

    [Fact]
    public void Projection_replaces_both_arrays_preserves_other_configuration_and_freezes_snapshot()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[server]\n1\n19 `mirror` 1 `[crack]`\n200 `raid` 23 `[raid]`\n[/server]");
            using var config = new ConfigurationManager();
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Channel:UseScriptCatalog"] = "true",
                ["Channel:ScriptPath"] = path,
                ["Channel:PublicServerIp"] = "192.0.2.1",
                ["Channel:Channels:8:Port"] = "39999",
                ["Networking:GamePorts:8"] = "39999",
                ["Database:Path"] = "retained.db",
            });
            ScriptChannelConfiguration.Apply(config);
            Assert.Equal(2, config.GetSection("Channel:Channels").GetChildren().Count());
            Assert.Equal(new[] { "10019", "10200" }, config.GetSection("Networking:GamePorts").GetChildren().Select(x => x.Value));
            Assert.Equal("19", config["Channel:Channels:0:Id"]);
            Assert.Equal("192.0.2.1", config["Channel:Channels:1:Host"]);
            Assert.Equal("23", config["Channel:Channels:1:LoginEnvironment"]);
            Assert.Equal("retained.db", config["Database:Path"]);
            File.WriteAllText(path, "invalid");
            Assert.Equal("19", config["Channel:Channels:0:Id"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Only_group_one_is_used_duplicates_keep_first_and_invalid_raid_is_ignored()
    {
        const string script = "[server]\n2\n99 `other` 1 `[none]`\n[/server]\n[server]\n1\n19 `mirror` 1 `[crack]`\n19 `duplicate` 23 `[raid]`\n201 `invalid` 32 `[raid]`\n[/server]";
        var channel = Assert.Single(ScriptChannelConfiguration.Parse(script, false));
        Assert.Equal(19, channel.Id);
        Assert.Equal(1, channel.LoginEnvironment);
        Assert.Throws<InvalidOperationException>(() => ScriptChannelConfiguration.Parse("[server]\n1\n[/server]", false));
    }
}
