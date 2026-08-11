using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Commands.Game;

public sealed record SelectCharacterCommand(AccountId AccountId, ushort SlotIndex)
    : ICommand<SelectCharacterResult>, IAccountCommand
{
    public ApplicationCommandType CommandType => ApplicationCommandType.SelectCharacter;
}

public sealed class SelectCharacterCommandHandler(ICharacterEntryService characterEntry)
    : CommandHandler<SelectCharacterCommand, SelectCharacterResult>
{
    protected override ValueTask<SelectCharacterResult> HandleCommandAsync(
        CommandExecutionContext context,
        SelectCharacterCommand command,
        CancellationToken cancellationToken) =>
        characterEntry.EnterAsync(command.AccountId, command.SlotIndex, cancellationToken);
}
