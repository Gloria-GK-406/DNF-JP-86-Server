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
internal static class EquipmentQualityDomainTestDriver
{
    public static Task<EquipmentQualityResult> Reset(ShopFixture host, SelectedSessionIdentity owner, BusinessSlot target, int expectedId, BusinessSlot material) =>
        TestGameplay.RunAsync(host.Services, async () =>
        {
            var safety = host.Services.GetRequiredService<SelectedSessionSafety>();
            var captured = safety.Capture(owner, null); safety.DemandCurrent(captured);
            var tx = ((DomainTransactionScope)host.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            try
            {
                var result = await host.Services.GetRequiredService<IEquipmentResealDomainService>().TryResealAsync(owner, target, expectedId, material, default)
                    ?? await host.Services.GetRequiredService<IEquipmentQualityDomainService>().RerollAsync(owner, target, expectedId, material, default);
                safety.DemandCurrent(captured); await tx.CommitAsync(default); return result;
            }
            catch (Exception cause) { await tx.AbortAsync(cause); throw; }
            finally { await tx.DisposeAsync(); }
        });
    public static Task<EquipmentQualityResult> Grade(ShopFixture host, SelectedSessionIdentity owner, BusinessSlot target, BusinessSlot material, ushort option) =>
        TestGameplay.RunAsync(host.Services, async () =>
        {
            var safety = host.Services.GetRequiredService<SelectedSessionSafety>();
            var captured = safety.Capture(owner, null); safety.DemandCurrent(captured);
            var tx = ((DomainTransactionScope)host.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            try
            {
                var result = await host.Services.GetRequiredService<IEquipmentQualityDomainService>().RerollMagicSealGradeAsync(owner, target, material, option, default);
                safety.DemandCurrent(captured); await tx.CommitAsync(default); return result;
            }
            catch (Exception cause) { await tx.AbortAsync(cause); throw; }
            finally { await tx.DisposeAsync(); }
        });
}
