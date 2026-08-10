using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.OnlineUsers;
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

public sealed record LoginGameUserResult(OnlineUserId OnlineUserId, AccountId AccountId);

public sealed class LoginGameUserCommandHandler(
    IAccountRepository accounts,
    IOnlineUserService onlineUsers) : CommandHandler<LoginGameUserCommand, LoginGameUserResult>
{
    protected override async ValueTask<LoginGameUserResult> HandleCommandAsync(
        CommandExecutionContext context,
        LoginGameUserCommand command,
        CancellationToken cancellationToken)
    {
        var account = await accounts.GetOrCreateAsync(
            command.LoginId,
            command.PasswordHash,
            cancellationToken).ConfigureAwait(false);
        onlineUsers.Authenticate(command.OnlineUserId, account.AccountId);
        return new LoginGameUserResult(command.OnlineUserId, account.AccountId);
    }
}
