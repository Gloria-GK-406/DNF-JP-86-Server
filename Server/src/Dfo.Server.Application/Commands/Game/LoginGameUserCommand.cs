using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Application.Commands.Game;

public sealed record LoginGameUserCommand(
    OnlineUserId OnlineUserId,
    string LoginId,
    string PasswordHash) : ICommand<LoginGameUserResult>, IOnlineUserCommand
{
    public ApplicationCommandType CommandType => ApplicationCommandType.LoginGameUser;
}

public sealed record LoginGameUserResult(AccountId AccountId);

public sealed class LoginGameUserCommandHandler(
    IAccountService accounts) : CommandHandler<LoginGameUserCommand, LoginGameUserResult>
{
    protected override async ValueTask<LoginGameUserResult> HandleCommandAsync(
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
