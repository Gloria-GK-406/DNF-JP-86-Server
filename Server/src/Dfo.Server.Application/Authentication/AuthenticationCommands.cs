using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Authentication;

namespace Dfo.Server.Application.Authentication;

public sealed class RegisterAccountCommand(string loginId, string password) : ICommand<RegistrationResult>
{
    public string LoginId { get; } = loginId;
    public string Password { get; } = password;
}

public sealed class AuthenticateAndIssueTicketCommand(string loginId, string password) : ICommand<AuthenticationResult>
{
    public string LoginId { get; } = loginId;
    public string Password { get; } = password;
}

internal sealed class RegisterAccountCommandHandler(IAuthenticationDomainService authentication)
    : ICommandHandler<RegisterAccountCommand, RegistrationResult>
{
    public ValueTask<RegistrationResult> HandleAsync(CommandExecutionContext context, RegisterAccountCommand command, CancellationToken cancellationToken) =>
        authentication.RegisterAsync(command.LoginId, command.Password, cancellationToken);
}

internal sealed class AuthenticateAndIssueTicketCommandHandler(IAuthenticationDomainService authentication)
    : ICommandHandler<AuthenticateAndIssueTicketCommand, AuthenticationResult>
{
    public ValueTask<AuthenticationResult> HandleAsync(CommandExecutionContext context, AuthenticateAndIssueTicketCommand command, CancellationToken cancellationToken) =>
        authentication.AuthenticateAndIssueTicketAsync(command.LoginId, command.Password, cancellationToken);
}
