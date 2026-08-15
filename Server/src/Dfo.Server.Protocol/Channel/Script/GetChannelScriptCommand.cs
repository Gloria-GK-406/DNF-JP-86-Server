using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Channel.Script;

public sealed record GetChannelScriptCommand : ICommand<GetChannelScriptResult>;

public sealed record GetChannelScriptResult(byte[] Content);

public sealed class GetChannelScriptCommandHandler(IChannelScriptSource source)
    : ICommandHandler<GetChannelScriptCommand, GetChannelScriptResult>
{
    public async ValueTask<GetChannelScriptResult> HandleAsync(
        CommandExecutionContext context,
        GetChannelScriptCommand command,
        CancellationToken cancellationToken) =>
        new(await source.ReadAsync(cancellationToken).ConfigureAwait(false));
}
