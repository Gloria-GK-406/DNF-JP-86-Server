using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Commands.Game;

public sealed record GetAccountCharacterRosterCommand(AccountId AccountId)
    : ICommand<AccountCharacterRoster>, IAccountCommand
{
    public ApplicationCommandType CommandType => ApplicationCommandType.GetAccountCharacterRoster;
}

public sealed class GetAccountCharacterRosterCommandHandler(ICharacterService characters)
    : CommandHandler<GetAccountCharacterRosterCommand, AccountCharacterRoster>
{
    protected override ValueTask<AccountCharacterRoster> HandleCommandAsync(
        CommandExecutionContext context,
        GetAccountCharacterRosterCommand command,
        CancellationToken cancellationToken) =>
        characters.GetAccountRosterAsync(command.AccountId, cancellationToken);
}
