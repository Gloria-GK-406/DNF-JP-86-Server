using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Protocol.Channel.Info;

public sealed record GetChannelInfoCommand : ICommand<GetChannelInfoResult>;

public sealed record GetChannelInfoResult(IReadOnlyList<ChannelServerInfo> Channels);

public sealed class GetChannelInfoCommandHandler(IChannelCatalog catalog)
    : ICommandHandler<GetChannelInfoCommand, GetChannelInfoResult>
{
    public async ValueTask<GetChannelInfoResult> HandleAsync(
        CommandExecutionContext context,
        GetChannelInfoCommand command,
        CancellationToken cancellationToken) =>
        new(await catalog.GetChannelsAsync(cancellationToken).ConfigureAwait(false));
}
