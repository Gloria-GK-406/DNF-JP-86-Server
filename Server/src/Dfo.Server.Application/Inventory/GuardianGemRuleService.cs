using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class GuardianGemRuleService(IEquipmentEnchantContentSource content)
{
    public static bool AcceptsSlots(BusinessSlot source, byte socket) =>
        source.Space == BusinessInventorySpace.GuardianGem && source.Index is >= 0 and < 49
        && socket < GuardianGemSockets.Count;

    public static bool AcceptsMedal(ItemSnapshot medal, DateTimeOffset now) =>
        medal is { LegacyKind: 12, IsPile: false } && AvailableForInstallation(medal, now);

    public static bool AcceptsGem(ItemSnapshot gem, DateTimeOffset now) =>
        gem is { LegacyKind: 13, IsPile: true, Count: > 0 } && AvailableForInstallation(gem, now);

    private static bool AvailableForInstallation(ItemSnapshot item, DateTimeOffset now) =>
        !item.IsMirror && item.EquipmentLockId == 0 && item.SortLockFlag == 0 && !item.Lifetime.IsExpired(now);

    public async ValueTask<GuardianGemRejection> CheckSocketAsync(GuardianGemSockets sockets, int expectedGem,
        byte socket, CancellationToken ct)
    {
        if (sockets.ItemIdAt(socket) == expectedGem)
            return GuardianGemRejection.InvalidGem;
        var effects = await content.GuardianGemEffectsAsync(expectedGem, ct).ConfigureAwait(false);
        if (effects.Count == 0)
            return GuardianGemRejection.InvalidGem;
        for (var index = 0; index < GuardianGemSockets.Count; index++)
        {
            var prior = sockets.ItemIdAt(index);
            if (index == socket || prior == 0) continue;
            var existing = await content.GuardianGemEffectsAsync(prior, ct).ConfigureAwait(false);
            if (existing.Count == 0)
                return GuardianGemRejection.InvalidTarget;
            if (effects.Intersect(existing, StringComparer.OrdinalIgnoreCase).Any())
                return GuardianGemRejection.InvalidGem;
        }

        return GuardianGemRejection.None;
    }
}
