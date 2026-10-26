using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Commands;

/// <summary>Only the existing exact-session teardown; it never opens an enclosing transaction or grants normal work.</summary>
public interface ICommandSelectedSessionCleanup
{
    ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(CommandExecutionContext context,
        SelectedSessionIdentity session, bool includeOwner, CommandWorkUnit? action = null,
        CancellationToken cancellationToken = default);
}
