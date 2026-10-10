using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

/// <summary>Bind a worn avatar to a value snapshot, never to the lifetime of another item.</summary>
internal sealed class AvatarAppearanceRuleService(IAvatarAppearanceDefinitionSource definitions)
{
    public async ValueTask<ItemSnapshot> PrepareAsync(ItemSnapshot item, ItemSnapshot? previous, CancellationToken ct)
    {
        if (item.LegacyKind != 8) return item;
        var clone = await definitions.IsCloneAsync(item.ItemId, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Missing avatar appearance definition: {item.ItemId}.");
        var detail = item.AvatarDetail ?? throw new InvalidDataException("Avatar detail is missing.");
        var appearance = item.ItemId;
        if (clone)
        {
            appearance = 0;
            if (previous is { LegacyKind: 8 })
            {
                appearance = previous.AppearanceItemId ?? (await definitions.IsCloneAsync(previous.ItemId, ct)
                    .ConfigureAwait(false) == true ? previous.AvatarDetail?.ClearAvatarId ?? 0 : previous.ItemId);
            }
        }
        return item with
        {
            AppearanceItemId = appearance,
            AvatarDetail = detail with
            {
                // Legacy protocol field is projected from AppearanceItemId, not stored twice.
                ClearAvatarId = 0,
                Color1 = clone ? appearance > 0 ? previous?.AvatarDetail?.Color1 ?? 0 : (ushort)0 : detail.Color1,
                Color2 = clone ? appearance > 0 ? previous?.AvatarDetail?.Color2 ?? 0 : (ushort)0 : detail.Color2,
            },
        };
    }
}
