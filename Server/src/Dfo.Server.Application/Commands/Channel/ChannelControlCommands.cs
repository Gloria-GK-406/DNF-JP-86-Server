using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Commands.Channel;

public sealed record GetChannelScriptVersionCommand : ICommand<GetChannelScriptVersionResult>
{
    public ApplicationCommandType CommandType => ApplicationCommandType.GetChannelScriptVersion;
}

public sealed record GetChannelScriptVersionResult(string Version);

public sealed class GetChannelScriptVersionCommandHandler(IChannelScriptSource source)
    : CommandHandler<GetChannelScriptVersionCommand, GetChannelScriptVersionResult>
{
    protected override ValueTask<GetChannelScriptVersionResult> HandleCommandAsync(
        CommandExecutionContext context,
        GetChannelScriptVersionCommand command,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new GetChannelScriptVersionResult(source.Version));
}

public sealed record GetChannelScriptCommand : ICommand<GetChannelScriptResult>
{
    public ApplicationCommandType CommandType => ApplicationCommandType.GetChannelScript;
}

public sealed record GetChannelScriptResult(byte[] Content);

public sealed class GetChannelScriptCommandHandler(IChannelScriptSource source)
    : CommandHandler<GetChannelScriptCommand, GetChannelScriptResult>
{
    protected override async ValueTask<GetChannelScriptResult> HandleCommandAsync(
        CommandExecutionContext context,
        GetChannelScriptCommand command,
        CancellationToken cancellationToken) =>
        new(await source.ReadAsync(cancellationToken).ConfigureAwait(false));
}

public sealed record GetChannelInfoCommand : ICommand<GetChannelInfoResult>
{
    public ApplicationCommandType CommandType => ApplicationCommandType.GetChannelInfo;
}

public sealed record GetChannelInfoResult(IReadOnlyList<ChannelServerInfo> Channels);

public sealed class GetChannelInfoCommandHandler(IChannelCatalog catalog)
    : CommandHandler<GetChannelInfoCommand, GetChannelInfoResult>
{
    protected override async ValueTask<GetChannelInfoResult> HandleCommandAsync(
        CommandExecutionContext context,
        GetChannelInfoCommand command,
        CancellationToken cancellationToken) =>
        new(await catalog.GetChannelsAsync(cancellationToken).ConfigureAwait(false));
}
