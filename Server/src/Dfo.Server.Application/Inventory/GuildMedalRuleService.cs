using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class GuildMedalRuleService(IEquipmentDefinitionSource equipment)
{
    public async ValueTask<bool> SupportsAuraAsync(ItemReference medal, DateTimeOffset now, CancellationToken ct) =>
        medal.Snapshot is { LegacyKind: 12, IsPile: false, IsMirror: false } && !medal.Snapshot.Lifetime.IsExpired(now)
        && await equipment.GetEquipmentAsync(medal.ItemId, ct).ConfigureAwait(false) is { Rarity: >= 3 };
}
