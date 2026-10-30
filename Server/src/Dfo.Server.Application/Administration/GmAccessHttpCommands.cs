using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.HttpOperations;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

/// <summary>Opens a GM session for a credential check made off the executor.</summary>
public sealed record LoginGmAdministratorCommand(GmCredentialCheck Check) : HttpOperationCommand;
public sealed record LogoutGmAdministratorCommand(string Secret) : HttpOperationCommand;
public sealed record QueryGmAuditCommand(int Page, int PageSize, GmAuditFilter Filter) : HttpOperationCommand;

/// <summary>Session and audit failures fail the CMD itself; none of them is answered as a business refusal.</summary>
internal sealed class GmAccessHttpCommandHandler(IGmAccessDomainService access, IGmAuditDomainService audit, ICommandWorkUnits units)
    : ICommandHandler<LoginGmAdministratorCommand, HttpRouteReply>, ICommandHandler<LogoutGmAdministratorCommand, HttpRouteReply>,
      ICommandHandler<QueryGmAuditCommand, HttpRouteReply>
{
    public async ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, LoginGmAdministratorCommand command, CancellationToken token)
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        return new(await HttpOperationWork.WriteAsync(units, context, "login-gm-administrator", t => access.LoginAsync(command.Check, t), token).ConfigureAwait(false));
    }

    public async ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, LogoutGmAdministratorCommand command, CancellationToken token)
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        return new(await HttpOperationWork.WriteAsync(units, context, "logout-gm-administrator", t => access.LogoutAsync(command.Secret, t), token).ConfigureAwait(false));
    }

    public async ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, QueryGmAuditCommand command, CancellationToken token)
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Gm);
        return new(await HttpOperationWork.WriteAsync(units, context, "query-gm-audit",
            t => audit.QueryAsync(command.Page, command.PageSize, command.Filter, t), token).ConfigureAwait(false));
    }
}
