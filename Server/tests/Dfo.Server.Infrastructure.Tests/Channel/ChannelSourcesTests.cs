using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Dfo.Server.Infrastructure.Tests.Channel;

public sealed class ChannelSourcesTests
{
    [Fact]
    public async Task Module_ProvidesConfiguredScriptAndChannelCatalog()
    {
        var scriptPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(scriptPath, [0x10, 0x20, 0x30]);
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Channel:ScriptVersion"] = "66",
                ["Channel:ScriptPath"] = scriptPath,
                ["Channel:PublicServerIp"] = "192.0.2.10",
                ["Channel:Channels:0:Id"] = "11",
                ["Channel:Channels:0:Name"] = "#ch.11",
                ["Channel:Channels:0:MaximumUsers"] = "500",
                ["Channel:Channels:0:Port"] = "10011",
            });
            new InfrastructureModule().ConfigureServices(builder.Services, builder.Configuration);
            await using var provider = builder.Services.BuildServiceProvider();

            var script = provider.GetRequiredService<IChannelScriptSource>();
            var catalog = provider.GetRequiredService<IChannelCatalog>();

            Assert.Equal("66", script.Version);
            Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, await script.ReadAsync(CancellationToken.None));
            var channel = Assert.Single(await catalog.GetChannelsAsync(CancellationToken.None));
            Assert.Equal(11, channel.ChannelId);
            Assert.Equal("#ch.11", channel.Name);
            Assert.Equal(500, channel.MaximumUsers);
            Assert.Equal(0, channel.CurrentUsers);
            Assert.Equal("192.0.2.10", channel.Host);
            Assert.Equal(10011, channel.Port);
        }
        finally
        {
            File.Delete(scriptPath);
        }
    }
}
