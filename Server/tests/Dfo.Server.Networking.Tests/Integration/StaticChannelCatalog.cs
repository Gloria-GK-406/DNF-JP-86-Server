using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Networking.Tests.Integration;

internal sealed class StaticChannelCatalog(params ChannelServerInfo[] channels) : IChannelCatalog
{
    public ValueTask<IReadOnlyList<ChannelServerInfo>> GetChannelsAsync(
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ChannelServerInfo>>(channels);

    public static StaticChannelCatalog ForGamePort(int gamePort, int channelId = 11) =>
        new(new ChannelServerInfo(
            channelId,
            $"#ch.{channelId}",
            MaximumUsers: 500,
            CurrentUsers: 0,
            Host: "127.0.0.1",
            Port: gamePort));
}
