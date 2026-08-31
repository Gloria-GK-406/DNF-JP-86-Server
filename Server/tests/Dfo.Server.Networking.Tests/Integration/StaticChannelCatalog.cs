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

/// <summary>
/// A channel script nothing downloads.
/// </summary>
/// <remarks>
/// These compositions assemble the server without <c>InfrastructureModule</c> and hand it a
/// catalogue of their own. The channel rules read the catalogue and the script as one subject, so a
/// graph that supplies only the first will not build -- and admission resolves at startup, which is
/// why the listener never opens rather than one command failing. Registered as a stub rather than a
/// value, because no test on these paths asks the client to download anything.
/// </remarks>
internal sealed class NoChannelScript : IChannelScriptSource
{
    public string Version => throw new NotSupportedException();

    public ValueTask<byte[]> ReadAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
