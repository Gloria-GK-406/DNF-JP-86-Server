using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Channel.Script;

public sealed record GetChannelScriptCommand : ICommand<GetChannelScriptResult>;

public sealed record GetChannelScriptResult(byte[] Content);

public sealed class GetChannelScriptCommandHandler(IChannelDirectoryDomainService channels)
    : ICommandHandler<GetChannelScriptCommand, GetChannelScriptResult>
{
    public async ValueTask<GetChannelScriptResult> HandleAsync(
        CommandExecutionContext context,
        GetChannelScriptCommand command,
        CancellationToken cancellationToken) =>
        new(await channels.ReadScriptAsync(cancellationToken).ConfigureAwait(false));
}
