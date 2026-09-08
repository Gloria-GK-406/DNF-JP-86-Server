using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Protocol.Game.AccountRoster;

public sealed record GetAccountCharacterRosterCommand(AccountId AccountId)
    : ICommand<AccountCharacterRoster>, IAccountCommand;

/// <remarks>
/// The one caller of the roster that carries appearances. This frame draws every character on the
/// account, so it is the one that pays for working out what each of them wears; the rest of the
/// server asks the plain roster, which does not.
/// </remarks>
public sealed class GetAccountCharacterRosterCommandHandler(ICharacterDomainService characters)
    : ICommandHandler<GetAccountCharacterRosterCommand, AccountCharacterRoster>
{
    public ValueTask<AccountCharacterRoster> HandleAsync(
        CommandExecutionContext context,
        GetAccountCharacterRosterCommand command,
        CancellationToken cancellationToken) =>
        characters.GetAccountRosterWithAppearanceAsync(command.AccountId, cancellationToken);
}
