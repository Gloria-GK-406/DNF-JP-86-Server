using System.Globalization;

namespace Dfo.Server.Application.Commands.Channel;

public sealed record EstablishChannelConnectionCommand : ICommand<EstablishChannelConnectionResult>
{
    public ApplicationCommandType CommandType =>
        ApplicationCommandType.EstablishChannelConnection;
}

public sealed record EstablishChannelConnectionResult(string EncryptionKey);

public sealed class EstablishChannelConnectionCommandHandler(TimeProvider timeProvider)
    : CommandHandler<EstablishChannelConnectionCommand, EstablishChannelConnectionResult>
{
    protected override ValueTask<EstablishChannelConnectionResult> HandleCommandAsync(
        CommandExecutionContext context,
        EstablishChannelConnectionCommand command,
        CancellationToken cancellationToken)
    {
        var encryptionKey = timeProvider
            .GetLocalNow()
            .ToString("yyyyMMdd'000006'", CultureInfo.InvariantCulture);
        return ValueTask.FromResult(new EstablishChannelConnectionResult(encryptionKey));
    }
}
