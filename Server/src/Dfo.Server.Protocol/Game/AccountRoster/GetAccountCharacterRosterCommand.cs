using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Game.AccountRoster;

public sealed record GetAccountCharacterRosterCommand(AccountId AccountId)
    : ICommand<AccountCharacterRoster>, IAccountCommand;

public sealed class GetAccountCharacterRosterCommandHandler(ICharacterService characters)
    : ICommandHandler<GetAccountCharacterRosterCommand, AccountCharacterRoster>
{
    public ValueTask<AccountCharacterRoster> HandleAsync(
        CommandExecutionContext context,
        GetAccountCharacterRosterCommand command,
        CancellationToken cancellationToken) =>
        characters.GetAccountRosterAsync(command.AccountId, cancellationToken);
}
