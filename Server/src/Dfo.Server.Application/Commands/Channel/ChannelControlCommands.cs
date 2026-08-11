using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Commands.Channel;

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
