using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Channel;

/// <summary>The one reader of what the server declares about its channels.</summary>
internal sealed class ChannelContentRuleService(
    IChannelCatalog channels,
    IChannelScriptSource script) : IChannelContentRuleService
{
    public ValueTask<IReadOnlyList<ChannelServerInfo>> ChannelsAsync(
        CancellationToken cancellationToken) =>
        channels.GetChannelsAsync(cancellationToken);

    public string ScriptVersion => script.Version;

    public ValueTask<byte[]> ReadScriptAsync(CancellationToken cancellationToken) =>
        script.ReadAsync(cancellationToken);
}
