using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Creatures;

internal sealed class CreatureNamingDomainService(
    ICreatureDomainService creatures,
    ISelectedSessionDomainService sessions, TimeProvider clock) : ICreatureNamingDomainService
{
    // A21 Script.pvf: stackable/stackable.lst 25 -> cash/creature/rename_card.stk,
    // [stackable type] [creature] 1. The 70 creature name field is 12 bytes.
    private const int RenameCardTemplateId = 25;
    private const int MaxNameBytes = 12;

    public async ValueTask<CreatureRenameReceipt?> RenameAsync(SelectedSessionIdentity session,
        BusinessSlot sourceSlot, byte[] nameBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(nameBytes);
        if (sessions.FindByCharacter(session.CharacterId) != session
            || sourceSlot.Space != BusinessInventorySpace.CreatureConsumable
            || sourceSlot.Index is < 0 or > 50
            || nameBytes.Length > MaxNameBytes
            || nameBytes.Any(value => value is 0 or < 0x20 or 0x7F))
            return null;

        var store = creatures.GetStore(session.CharacterId);
        var cardContainer = store.FindContainer(BusinessInventorySpace.CreatureConsumable);
        var wornContainer = store.FindContainer(BusinessInventorySpace.WornCreature);
        if (cardContainer is null || wornContainer is null) return null;

        var current = await store.CurrentAsync(cancellationToken).ConfigureAwait(false);
        var worn = await wornContainer.ReadSlotAsync(0, cancellationToken).ConfigureAwait(false);
        var card = await cardContainer.ReadSlotAsync(sourceSlot.Index, cancellationToken)
            .ConfigureAwait(false);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (current is null || worn is null || worn.ItemKind != 5
            || worn.Core.Value != current.ItemUid || worn.ItemId != current.ItemTemplateId
            || IsExpired(worn.Core.ExpireTime, now)
            || card is null || card.ItemKind != 7 || card.ItemId != RenameCardTemplateId
            || !card.IsPile || card.Count <= 0 || IsExpired(card.Core.ExpireTime, now))
            return null;

        var renamed = current with { NameBytes = [.. nameBytes] };
        InventoryItemCoreState? remaining = card.Count > 1
            ? card.Core with { Value = card.Count - 1 }
            : null;
        await store.SaveAsync(renamed, cancellationToken).ConfigureAwait(false);
        if (remaining is null)
            await cardContainer.ClearAsync(sourceSlot.Index, cancellationToken).ConfigureAwait(false);
        else
            await cardContainer.PutAsync(sourceSlot.Index, remaining, cancellationToken)
                .ConfigureAwait(false);

        return new CreatureRenameReceipt(renamed, sourceSlot, remaining);
    }

    private static bool IsExpired(int expiresAt, long now) =>
        expiresAt > 0 && expiresAt != int.MaxValue && expiresAt <= now;
}
