using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using System.Reflection;
using Dfo.Server.Protocol.Game.PackageOpening;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

internal static class OpeningDomainTestDriver
{
    // Four exact, pure conversion signatures from the production slice. No capability or state is reflected.
    private static readonly Type Slots = typeof(PackageOpeningRequest).Assembly.GetType("Dfo.Server.Protocol.Game.OpeningWireSlots", throwOnError: true)!;
    private static readonly Func<int, BusinessSlot> MainSlot = Slots.GetMethod("FromMain", [typeof(int)])!.CreateDelegate<Func<int, BusinessSlot>>();
    private static readonly Func<PackageOpeningInput, BusinessPackageOpeningInput> PackageSlots = Slots.GetMethod("Business", [typeof(PackageOpeningInput)])!.CreateDelegate<Func<PackageOpeningInput, BusinessPackageOpeningInput>>();
    private static readonly Func<AvatarPackageOpeningInput, BusinessAvatarPackageOpeningInput> AvatarSlots = Slots.GetMethod("Business", [typeof(AvatarPackageOpeningInput)])!.CreateDelegate<Func<AvatarPackageOpeningInput, BusinessAvatarPackageOpeningInput>>();
    private static readonly Func<MagicBoxOpeningInput, BusinessMagicBoxOpeningInput> BoxSlots = Slots.GetMethod("Business", [typeof(MagicBoxOpeningInput)])!.CreateDelegate<Func<MagicBoxOpeningInput, BusinessMagicBoxOpeningInput>>();
    public static Task<PackageOpeningResult> Package(ShopFixture host, SelectedSessionIdentity owner, PackageOpeningInput input, AvatarPackageOpeningInput? avatarInput = null) =>
        TestGameplay.RunAsync(host.Services, async () =>
        {
            var services = host.Services;
            var safety = services.GetRequiredService<SelectedSessionSafety>();
            var recipient = safety.Capture(owner, null);
            safety.DemandCurrent(recipient);
            using var footprint = new TestEffectFootprints.Lease(owner);
            var transaction = ((DomainTransactionScope)services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            try
            {
                var result = avatarInput is { } options ? await services.GetRequiredService<IAvatarPackageOpeningDomainService>().OpenAsync(owner, AvatarSlots(options), host.Clock.GetUtcNow(), footprint, default) : await services.GetRequiredService<IPackageOpeningDomainService>().OpenAsync(owner, PackageSlots(input), host.Clock.GetUtcNow(), footprint, default);
                safety.DemandCurrent(recipient);
                await transaction.CommitAsync(default);
                return result;
            }
            catch (Exception cause) { await transaction.AbortAsync(cause); throw; }
            finally { await transaction.DisposeAsync(); }
        });
    public static Task<PackageOpeningResult> Avatar(ShopFixture host, SelectedSessionIdentity owner, AvatarPackageOpeningInput input) =>
        TestGameplay.RunAsync(host.Services, async () =>
        {
            var services = host.Services;
            var safety = services.GetRequiredService<SelectedSessionSafety>();
            var recipient = safety.Capture(owner, null);
            safety.DemandCurrent(recipient);
            using var footprint = new TestEffectFootprints.Lease(owner);
            var transaction = ((DomainTransactionScope)services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            try
            {
                var result = input.Choices.IsEmpty ? await services.GetRequiredService<IPackageOpeningDomainService>().OpenAsync(owner, new(MainSlot(input.SourceSlot), []), host.Clock.GetUtcNow(), footprint, default) : await services.GetRequiredService<IAvatarPackageOpeningDomainService>().OpenAsync(owner, AvatarSlots(input), host.Clock.GetUtcNow(), footprint, default);
                safety.DemandCurrent(recipient);
                await transaction.CommitAsync(default);
                return result;
            }
            catch (Exception cause) { await transaction.AbortAsync(cause); throw; }
            finally { await transaction.DisposeAsync(); }
        });
    public static Task<MagicBoxOpeningResult> Box(ShopFixture host, SelectedSessionIdentity owner, MagicBoxOpeningInput input) =>
        TestGameplay.RunAsync(host.Services, async () =>
        {
            var services = host.Services;
            var safety = services.GetRequiredService<SelectedSessionSafety>();
            var recipient = safety.Capture(owner, null);
            safety.DemandCurrent(recipient);
            using var footprint = new TestEffectFootprints.Lease(owner);
            var transaction = ((DomainTransactionScope)services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
            try
            {
                var result = await services.GetRequiredService<IMagicBoxOpeningDomainService>().OpenAsync(owner, BoxSlots(input), host.Clock.GetUtcNow(), footprint, default);
                safety.DemandCurrent(recipient);
                await transaction.CommitAsync(default);
                return result;
            }
            catch (Exception cause) { await transaction.AbortAsync(cause); throw; }
            finally { await transaction.DisposeAsync(); }
        });
    public static Task<RecipeCraftReceipt> Recipe(ShopFixture host, SelectedSessionIdentity owner, BusinessSlot slot, int count) =>
        Run(host, owner, footprint => host.Services.GetRequiredService<IRecipeCraftDomainService>().CraftAsync(owner, slot, count, host.Clock.GetUtcNow(), footprint, default));
    public static Task<LotteryOpeningResult> Lottery(ShopFixture host, SelectedSessionIdentity owner, int slot, ushort mode) =>
        Run(host, owner, footprint => host.Services.GetRequiredService<ILotteryOpeningDomainService>().ExecuteAsync(owner, MainSlot(slot), mode, footprint, default));
    public static Task<VendingMachineResult> Vending(ShopFixture host, SelectedSessionIdentity owner, int machine, int group, int slot) =>
        Run(host, owner, footprint => host.Services.GetRequiredService<IVendingMachineDomainService>().UseAsync(owner, machine, group, MainSlot(slot), footprint, default));
    public static Task<EpicCraftReceipt> Epic(ShopFixture host, SelectedSessionIdentity owner, int equipment) =>
        Run(host, owner, footprint => host.Services.GetRequiredService<IEpicCraftDomainService>().CraftAsync(owner, equipment, host.Clock.GetUtcNow(), footprint, default));
    private static Task<T> Run<T>(ShopFixture host, SelectedSessionIdentity owner, Func<Dfo.Server.Abstractions.Effects.IEffectFootprintRecorder, ValueTask<T>> action) =>
        TestGameplay.RunAsync(host.Services, async () =>
        {
            var safety = host.Services.GetRequiredService<SelectedSessionSafety>();
            var recipient = safety.Capture(owner, null); safety.DemandCurrent(recipient);
            using var footprint = new TestEffectFootprints.Lease(owner);
            await using var transaction = host.Services.GetRequiredService<DomainTransactionScope>().Begin();
            try { var result = await action(footprint); safety.DemandCurrent(recipient); await transaction.CommitAsync(default); return result; }
            catch (Exception cause) { await transaction.AbortAsync(cause); throw; }
        });
}
