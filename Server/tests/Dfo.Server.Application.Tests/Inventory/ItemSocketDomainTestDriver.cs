using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

internal static class ItemSocketDomainTestDriver
{
    public static Task<ItemSocketResult> Apply(ShopFixture host, SelectedSessionIdentity owner, ItemSocketInput input) =>
        TestGameplay.RunAsync(host.Services, async () =>
        {
            var safety = host.Services.GetRequiredService<SelectedSessionSafety>(); var captured = safety.Capture(owner, null); safety.DemandCurrent(captured);
            using var footprint = new TestEffectFootprints.Lease(owner);
            var tx = ((DomainTransactionScope)host.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            try
            {
                var result = await host.Services.GetRequiredService<IItemSocketDomainService>().ApplyAsync(owner, input, footprint, default);
                safety.DemandCurrent(captured); await tx.CommitAsync(default); return result;
            }
            catch (Exception cause) { await tx.AbortAsync(cause); throw; }
            finally { await tx.DisposeAsync(); }
        });
}
