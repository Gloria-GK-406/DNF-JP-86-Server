using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.GmWeb;

internal sealed class GmBootstrapService(IConfiguration configuration, ICommandBus commands, IGmCredentialReader credentials,
    ILogger<GmBootstrapService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var name = configuration["GmWeb:Bootstrap:LoginName"];
        var password = configuration["GmWeb:Bootstrap:Password"];
        var rawPermissions = configuration["GmWeb:Bootstrap:Permissions"];
        var permissions = GmPermission.All;
        if (rawPermissions is not null && (!Enum.TryParse(rawPermissions, true, out permissions)
            || permissions == GmPermission.None || (permissions & ~GmPermission.All) != 0))
        { logger.LogWarning("GM bootstrap configuration is incomplete or invalid; no credentials were changed."); return; }
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(password))
        { logger.LogWarning("GM bootstrap credentials are not configured; private access remains restricted."); return; }
        // Hashed off the executor (RQ-007), also when an administrator already exists and the record goes unused.
        var record = await credentials.PrepareBootstrapAsync(password, cancellationToken);
        await commands.SendInternalAsync(new BootstrapGmAdministratorCommand(name, record, permissions),
            new InternalCommandOrigin(GameplayCallSource.StartupRecovery, GameplayCallChain.NewRoot(GameplayCallSource.StartupRecovery), "startup:GmBootstrapService"), cancellationToken);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
