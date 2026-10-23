using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Protocol.Game.Login;

public sealed record RedeemGameLoginCommand(
    OnlineUserId OnlineUserId,
    string MId,
    string Ticket) : ICommand<RedeemGameLoginResult>;

public sealed record RedeemGameLoginResult(AccountId? AccountId)
{
    public bool Succeeded => AccountId.HasValue;
}

public sealed class RedeemGameLoginCommandHandler(
    IGameLoginAuthenticationDomainService authentication)
    : ICommandHandler<RedeemGameLoginCommand, RedeemGameLoginResult>
{
    public async ValueTask<RedeemGameLoginResult> HandleAsync(
        CommandExecutionContext context,
        RedeemGameLoginCommand command,
        CancellationToken cancellationToken)
    {
        var accountId = await authentication.AuthenticateSessionAsync(
            command.OnlineUserId,
            command.MId,
            command.Ticket,
            cancellationToken).ConfigureAwait(false);
        return new RedeemGameLoginResult(accountId);
    }
}
