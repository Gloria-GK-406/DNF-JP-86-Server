using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.GmWeb;

internal sealed class GmBootstrapService(IConfiguration configuration, ICommandBus commands, ILogger<GmBootstrapService> logger) : IHostedService
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
        await commands.SendAsync(new BootstrapGmAdministratorCommand(name, password, permissions), cancellationToken);
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
