using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.HttpOperations;
using Dfo.Server.Data.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Authentication;

/// <summary>Stores a launcher registration whose password record was prepared off the executor.</summary>
public sealed record RegisterAccountCommand(PreparedRegistration Registration) : HttpOperationCommand;

/// <summary>Issues a launcher ticket for a login verified off the executor.</summary>
public sealed record AuthenticateAccountCommand(VerifiedLogin Login) : HttpOperationCommand;

internal sealed class AccountHttpCommandHandler(IAuthenticationDomainService authentication, ICommandWorkUnits units)
    : ICommandHandler<RegisterAccountCommand, HttpRouteReply>, ICommandHandler<AuthenticateAccountCommand, HttpRouteReply>
{
    public async ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, RegisterAccountCommand command, CancellationToken token)
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Http);
        return new(await HttpOperationWork.WriteAsync(units, context, "register-account",
            t => authentication.RegisterAsync(command.Registration, t), token).ConfigureAwait(false));
    }

    public async ValueTask<HttpRouteReply> HandleAsync(CommandExecutionContext context, AuthenticateAccountCommand command, CancellationToken token)
    {
        HttpOperationWork.DemandOrigin(context, GameplayCallSource.Http);
        return new(await HttpOperationWork.WriteAsync(units, context, "authenticate-and-issue-ticket",
            t => authentication.AuthenticateAndIssueTicketAsync(command.Login, t), token).ConfigureAwait(false));
    }
}

public static class AccountHttpCommandRegistration
{
    public static IServiceCollection AddAccountHttpCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddInternalCommandHandler<RegisterAccountCommand, HttpRouteReply, AccountHttpCommandHandler>();
        services.AddInternalCommandHandler<AuthenticateAccountCommand, HttpRouteReply, AccountHttpCommandHandler>();
        return services;
    }
}
