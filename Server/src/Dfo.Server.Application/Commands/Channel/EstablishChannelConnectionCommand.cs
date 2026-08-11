using Dfo.Server.Abstractions.Channel;

namespace Dfo.Server.Application.Commands.Channel;

public sealed record EstablishChannelConnectionCommand : ICommand<EstablishChannelConnectionResult>;

public sealed record EstablishChannelConnectionResult(string EncryptionKey);

public sealed class EstablishChannelConnectionCommandHandler(IChannelEncryptionKeyProvider keyProvider)
    : ICommandHandler<EstablishChannelConnectionCommand, EstablishChannelConnectionResult>
{
    public ValueTask<EstablishChannelConnectionResult> HandleAsync(
        CommandExecutionContext context,
        EstablishChannelConnectionCommand command,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(
            new EstablishChannelConnectionResult(keyProvider.GetCurrentKey()));
    }
}
