using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

public sealed class BootstrapGmAdministratorCommand(string loginName, string password, GmPermission permissions) : ICommand<bool>
{
    public string LoginName { get; } = loginName;
    public string Password { get; } = password;
    public GmPermission Permissions { get; } = permissions;
}
public sealed class LoginGmAdministratorCommand(string loginName, string password) : ICommand<GmLoginResult>
{
    public string LoginName { get; } = loginName;
    public string Password { get; } = password;
}
public sealed class LogoutGmAdministratorCommand(string sessionSecret) : ICommand<bool>
{
    public string SessionSecret { get; } = sessionSecret;
}
internal sealed class BootstrapGmAdministratorCommandHandler(IGmAccessDomainService access) : ICommandHandler<BootstrapGmAdministratorCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, BootstrapGmAdministratorCommand command, CancellationToken cancellationToken) =>
        access.BootstrapAsync(command.LoginName, command.Password, command.Permissions, cancellationToken);
}
internal sealed class LoginGmAdministratorCommandHandler(IGmAccessDomainService access) : ICommandHandler<LoginGmAdministratorCommand, GmLoginResult>
{
    public ValueTask<GmLoginResult> HandleAsync(CommandExecutionContext context, LoginGmAdministratorCommand command, CancellationToken cancellationToken) =>
        access.LoginAsync(command.LoginName, command.Password, cancellationToken);
}
internal sealed class LogoutGmAdministratorCommandHandler(IGmAccessDomainService access) : ICommandHandler<LogoutGmAdministratorCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, LogoutGmAdministratorCommand command, CancellationToken cancellationToken) =>
        access.LogoutAsync(command.SessionSecret, cancellationToken);
}
