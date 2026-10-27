using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

// These current-owner guards validate isolated fixture setup; production admission is covered by the wire flow tests.
internal static class WarehouseDomainTestDriver
{
    public static Task<InventoryExpansionResult> Expand(ShopFixture host, SelectedSessionIdentity owner, ushort type, int value = 0, BusinessSlot? slot = null) =>
        TestGameplay.RunAsync(host.Services, async () =>
        {
            var safety = host.Services.GetRequiredService<SelectedSessionSafety>(); var captured = safety.Capture(owner, null); safety.DemandCurrent(captured);
            var tx = ((DomainTransactionScope)host.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            try
            {
                var domain = host.Services.GetRequiredService<IInventoryExpansionDomainService>(); var at = host.Services.GetRequiredService<TimeProvider>().GetUtcNow();
                var result = type switch
                {
                    0x0131 or 0x0132 => await domain.UpgradeAccountAsync(owner, type == 0x0131, at, default),
                    0x0133 or 0x0134 => await domain.TransferGoldAsync(owner, type == 0x0133, value, at, default),
                    0x0198 or 0x01AF when slot is { } itemSlot => await domain.UseItemAsync(owner, itemSlot, type == 0x0198 ? InventoryExpansionKind.PersonalCargo : InventoryExpansionKind.Main, at, default),
                    _ => new InventoryExpansionResult(InventoryExpansionRejection.InvalidRequest),
                };
                safety.DemandCurrent(captured); await tx.CommitAsync(default); return result;
            }
            catch (Exception cause) { await tx.AbortAsync(cause); throw; }
            finally { await tx.DisposeAsync(); }
        });
}
