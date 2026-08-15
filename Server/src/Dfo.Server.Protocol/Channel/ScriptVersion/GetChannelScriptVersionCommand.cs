using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Channel.ScriptVersion;

public sealed record GetChannelScriptVersionCommand : ICommand<GetChannelScriptVersionResult>;

public sealed record GetChannelScriptVersionResult(string Version);

public sealed class GetChannelScriptVersionCommandHandler(IChannelScriptSource source)
    : ICommandHandler<GetChannelScriptVersionCommand, GetChannelScriptVersionResult>
{
    public ValueTask<GetChannelScriptVersionResult> HandleAsync(
        CommandExecutionContext context,
        GetChannelScriptVersionCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new GetChannelScriptVersionResult(source.Version));
}
