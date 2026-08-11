using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Application.Commands.Game;

public sealed record LoginGameUserCommand(
    OnlineUserId OnlineUserId,
    string LoginId,
    string PasswordHash) : ICommand<LoginGameUserResult>, IOnlineUserCommand;

public sealed record LoginGameUserResult(AccountId AccountId);

public sealed class LoginGameUserCommandHandler(
    IAccountService accounts) : ICommandHandler<LoginGameUserCommand, LoginGameUserResult>
{
    public async ValueTask<LoginGameUserResult> HandleAsync(
        CommandExecutionContext context,
        LoginGameUserCommand command,
        CancellationToken cancellationToken)
    {
        var accountId = await accounts.LoginAsync(
            command.OnlineUserId,
            command.LoginId,
            command.PasswordHash,
            cancellationToken).ConfigureAwait(false);
        return new LoginGameUserResult(accountId);
    }
}
