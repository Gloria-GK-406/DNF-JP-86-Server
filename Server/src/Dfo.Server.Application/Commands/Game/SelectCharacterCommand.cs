using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Commands.Game;

public sealed record SelectCharacterCommand(AccountId AccountId, ushort SlotIndex)
    : ICommand<SelectCharacterResult>, IAccountCommand;

public sealed class SelectCharacterCommandHandler(ICharacterEntryService characterEntry)
    : ICommandHandler<SelectCharacterCommand, SelectCharacterResult>
{
    public ValueTask<SelectCharacterResult> HandleAsync(
        CommandExecutionContext context,
        SelectCharacterCommand command,
        CancellationToken cancellationToken) =>
        characterEntry.EnterAsync(command.AccountId, command.SlotIndex, cancellationToken);
}
