using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Authentication;

namespace Dfo.Server.Application.Authentication;

/// <summary>Writes a registration whose password was hashed off the executor (RQ-007).</summary>
public sealed class RegisterAccountCommand(PreparedRegistration registration) : ICommand<RegistrationResult>
{
    public PreparedRegistration Registration { get; } = registration;
    public string LoginId => Registration.LoginId;
}

/// <summary>Issues a ticket for a password verified off the executor, if its record is still current (RQ-007).</summary>
public sealed class AuthenticateAndIssueTicketCommand(VerifiedLogin login) : ICommand<AuthenticationResult>
{
    public VerifiedLogin Login { get; } = login;
    public string LoginId => Login.LoginId;
}

internal sealed class RegisterAccountCommandHandler(IAuthenticationDomainService authentication)
    : ICommandHandler<RegisterAccountCommand, RegistrationResult>
{
    public ValueTask<RegistrationResult> HandleAsync(CommandExecutionContext context, RegisterAccountCommand command, CancellationToken cancellationToken) =>
        authentication.RegisterAsync(command.Registration, cancellationToken);
}

internal sealed class AuthenticateAndIssueTicketCommandHandler(IAuthenticationDomainService authentication)
    : ICommandHandler<AuthenticateAndIssueTicketCommand, AuthenticationResult>
{
    public ValueTask<AuthenticationResult> HandleAsync(CommandExecutionContext context, AuthenticateAndIssueTicketCommand command, CancellationToken cancellationToken) =>
        authentication.AuthenticateAndIssueTicketAsync(command.Login, cancellationToken);
}
