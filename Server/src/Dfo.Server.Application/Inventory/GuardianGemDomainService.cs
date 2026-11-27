using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class GuardianGemDomainService(IBusinessInventorySlotDomainService inventory,
    GuardianGemRuleService rules, IItemLifecycleDomainService lifecycle, TimeProvider clock)
    : IGuardianGemDomainService
{
    private static readonly BusinessSlot WornMedal = A21InventoryLayout.ToBusinessSlot(A21InventoryLayout.WornGuildMedal);

    public async ValueTask<GuardianGemResult> InstallAsync(SelectedSessionIdentity actor, BusinessSlot source,
        int expectedMedal, int expectedGem, byte socket, CancellationToken ct)
    {
        if (!GuardianGemRuleService.AcceptsSlots(source, socket)
            || inventory.Find(actor.CharacterId, WornMedal) is not { } equipped
            || await equipped.Container.GetAsync(equipped.Slot.Index, ct).ConfigureAwait(false) is not { } medal
            || medal.ItemId != expectedMedal || !GuardianGemRuleService.AcceptsMedal(medal.Snapshot, clock.GetUtcNow()))
            return new(GuardianGemRejection.InvalidTarget);
        if (!GuardianGemSockets.CanEncode(expectedGem) || inventory.Find(actor.CharacterId, source) is not { } tray
            || await tray.Container.GetAsync(source.Index, ct).ConfigureAwait(false) is not { } gem
            || gem.ItemId != expectedGem || !GuardianGemRuleService.AcceptsGem(gem.Snapshot, clock.GetUtcNow()))
            return new(GuardianGemRejection.InvalidGem);

        var sockets = medal.Snapshot.GuardianGems;
        var rejection = await rules.CheckSocketAsync(sockets, expectedGem, socket, ct).ConfigureAwait(false);
        if (rejection != GuardianGemRejection.None)
            return new(rejection);

        var installed = await equipped.Container.UpdateAsync(medal,
            core => core with { GuardianGems = sockets.Install(socket, expectedGem) }, ct).ConfigureAwait(false);
        await ItemContainerOperations.ConsumeAsync(tray.Container, gem, 1, lifecycle, ct).ConfigureAwait(false);
        var remaining = await tray.Container.GetAsync(source.Index, ct).ConfigureAwait(false);
        return new(GuardianGemRejection.None, installed.Snapshot, remaining?.Snapshot);
    }
}
