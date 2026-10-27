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
internal sealed class BootstrapGmAdministratorCommandHandler(IGmAccessDomainService access, ICommandWorkUnits units) : ICommandHandler<BootstrapGmAdministratorCommand, bool>
{
    public ValueTask<bool> HandleAsync(CommandExecutionContext context, BootstrapGmAdministratorCommand command, CancellationToken cancellationToken) =>
        units.WriteAsync(context, new("bootstrap-gm-administrator", []),
            (_, ct) => access.BootstrapAsync(command.LoginName, command.PasswordRecord, command.Permissions, ct), cancellationToken);
}
