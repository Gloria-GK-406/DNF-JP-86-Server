using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

/// <summary>Creates the first administrator from a password record prepared off the executor; null records are refused.</summary>
public sealed class BootstrapGmAdministratorCommand(string loginName, string? passwordRecord, GmPermission permissions) : ICommand<bool>
{
    public string LoginName { get; } = loginName;
    public string? PasswordRecord { get; } = passwordRecord;
    public GmPermission Permissions { get; } = permissions;
}
/// <summary>Audits a GM login and opens its session, if the off-executor credential check still holds.</summary>
public sealed class LoginGmAdministratorCommand(GmCredentialCheck check) : ICommand<GmLoginResult>
{
    public GmCredentialCheck Check { get; } = check;
}
public sealed class LogoutGmAdministratorCommand(string sessionSecret) : ICommand<bool>
{
    public string SessionSecret { get; } = sessionSecret;
}
internal sealed class BootstrapGmAdministratorCommandHandler(IGmAccessDomainService access, ICommandWorkUnits units) : ICommandHandler<BootstrapGmAdministratorCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, BootstrapGmAdministratorCommand command, CancellationToken cancellationToken) =>
        units.WriteAsync(context, new("bootstrap-gm-administrator", []),
            (_, ct) => access.BootstrapAsync(command.LoginName, command.PasswordRecord, command.Permissions, ct), cancellationToken);
}
internal sealed class LoginGmAdministratorCommandHandler(IGmAccessDomainService access) : ICommandHandler<LoginGmAdministratorCommand, GmLoginResult>
{
    public ValueTask<GmLoginResult> HandleAsync(CommandExecutionContext context, LoginGmAdministratorCommand command, CancellationToken cancellationToken) =>
        access.LoginAsync(command.Check, cancellationToken);
}
internal sealed class LogoutGmAdministratorCommandHandler(IGmAccessDomainService access) : ICommandHandler<LogoutGmAdministratorCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, LogoutGmAdministratorCommand command, CancellationToken cancellationToken) =>
        access.LogoutAsync(command.SessionSecret, cancellationToken);
}
