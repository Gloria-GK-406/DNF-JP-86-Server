using Dfo.Server.Abstractions.Channel;

namespace Dfo.Server.Application.Commands.Channel;

public sealed record EstablishChannelConnectionCommand : ICommand<EstablishChannelConnectionResult>
{
    public ApplicationCommandType CommandType =>
        ApplicationCommandType.EstablishChannelConnection;
}

public sealed record EstablishChannelConnectionResult(string EncryptionKey);

public sealed class EstablishChannelConnectionCommandHandler(IChannelEncryptionKeyProvider keyProvider)
    : CommandHandler<EstablishChannelConnectionCommand, EstablishChannelConnectionResult>
{
    protected override ValueTask<EstablishChannelConnectionResult> HandleCommandAsync(
        CommandExecutionContext context,
        EstablishChannelConnectionCommand command,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(
            new EstablishChannelConnectionResult(keyProvider.GetCurrentKey()));
    }
}
