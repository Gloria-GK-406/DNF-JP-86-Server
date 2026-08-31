using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Channel;

/// <summary>What this server tells a client about its channels.</summary>
internal sealed class ChannelDirectoryDomainService(
    IChannelContentRuleService channels) : IChannelDirectoryDomainService
{
    public ValueTask<IReadOnlyList<ChannelServerInfo>> ChannelsAsync(
        CancellationToken cancellationToken) => channels.ChannelsAsync(cancellationToken);

    public string ScriptVersion => channels.ScriptVersion;

    public ValueTask<byte[]> ReadScriptAsync(CancellationToken cancellationToken) =>
        channels.ReadScriptAsync(cancellationToken);
}
