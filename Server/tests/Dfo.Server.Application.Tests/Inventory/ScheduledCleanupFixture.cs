using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

// Domain/transaction fixture; actual scheduled-CMD and hosted timing coverage have separate entry tests.
internal static class ScheduledCleanupFixture
{
    internal static ValueTask<bool> RunSessionAsync(IServiceProvider services, SelectedSessionIdentity session) =>
        RunDomainAsync(services, session, token => services.GetRequiredService<IOnlineExpiredItemCleanupDomainService>().CleanupAsync(session, token));
    internal static ValueTask<bool> RunDomainAsync(IServiceProvider services, SelectedSessionIdentity session, Func<CancellationToken, ValueTask<bool>> action) =>
        new(Dfo.Server.Testing.TestGameplay.RunAsync(services, async () =>
        {
            if (!services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().IsInWorld(session))
                throw new InvalidOperationException("The domain fixture requires the exact in-world session, as the former fine admission did.");
            var request = new CommandRequestContext.TrustedInternal(new(GameplayCallSource.Timer, GameplayJob.Current!.Value, "test:scheduled-cleanup-domain"));
            var context = new CommandExecutionContext(Guid.NewGuid(), typeof(ScheduledCleanupFixture), typeof(ScheduledCleanupFixture), CommandExecutionKind.Direct) { Request = request };
            try
            {
                return await services.GetRequiredService<ICommandWorkUnits>().WriteAsync(context, new("test:scheduled-session", [new(session, null)]),
                    (_, token) => action(token));
            }
            finally { await request.DrainOperationsAsync(); request.Close(); }
        }));
}
