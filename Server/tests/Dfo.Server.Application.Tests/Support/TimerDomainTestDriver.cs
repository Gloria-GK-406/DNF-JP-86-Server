using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Support;
// These inventory assertions query domain facts; FIFO and producer scopes are covered by timer tests.
internal static class TimerDomainTestDriver
{
    internal static Task<PremiumSnapshotResult> PremiumAsync(IServiceProvider services, SelectedSessionIdentity session) =>
        TestGameplay.RunAsync(services, async () =>
        {
            var scope = (DomainTransactionScope)services.GetRequiredService<IDomainTransactionAccessor>();
            await using var transaction = scope.Begin(readOnly: true);
            var result = await PremiumSnapshotReader.ReadAsync(services.GetRequiredService<ISelectedSessionDomainService>(),
                services.GetRequiredService<IPremiumServiceDomainService>(), services.GetRequiredService<IPremiumServiceUsageDomainService>(),
                services.GetRequiredService<TimeProvider>(), session, default);
            await transaction.CommitAsync(default); return result;
        });
    internal static Task RecoverAsync(IServiceProvider services) => TestGameplay.RunAsync(services, async () =>
    {
        var scope = (DomainTransactionScope)services.GetRequiredService<IDomainTransactionAccessor>();
        var weakness = services.GetRequiredService<IWeaknessDomainService>();
        await using var transaction = scope.Begin();
        weakness.AdvanceRecovery(); await weakness.RetryPendingWeakness(default); await transaction.CommitAsync(default);
    });
}
