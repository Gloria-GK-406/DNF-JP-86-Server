using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class GuardianGemRuleService(IEquipmentEnchantContentSource content)
{
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
