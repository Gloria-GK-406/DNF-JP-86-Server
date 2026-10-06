using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Creatures;

/// <summary>The state of one creature item. Its item UID is its only instance identity.</summary>
public sealed record CreatureState(
    int ItemUid,
    int ItemTemplateId,
    int Stomach,
    int Experience,
    int Level,
    byte[] NameBytes,
    int ModeFlag,
    int Mode1Field0A,
    int Mode1Field0B,
    int TailFlag)
{
    public static CreatureState FromItem(ItemSnapshot item)
    {
        var detail = item.CreatureDetail ?? throw new InvalidDataException("Creature detail is missing.");
        var uid = item.ItemUid?.Value ?? throw new InvalidDataException("Creature identity is missing.");
        return new(uid, item.ItemId, detail.Field04, detail.ProgressValue, detail.FieldAfterValue,
            [.. detail.CreatureText], detail.ModeFlag, detail.Mode1Field0A, detail.Mode1Field0B, detail.TailFlag);
    }

    public InventoryCreatureDetailState ToDetail() => new(Stomach, checked((byte)ModeFlag), Experience,
        Mode1Field0A, Mode1Field0B, Level, [.. NameBytes], checked((byte)TailFlag));

    // Online time accounting may retain fractions; storage still carries the legacy whole value.
    private readonly double? exactStomach;
    private readonly int? exactStomachBase;
    public double ExactStomach
    {
        get => this.exactStomachBase == Stomach ? this.exactStomach ?? Stomach : Stomach;
        init
        {
            this.exactStomach = value;
            this.exactStomachBase = Stomach;
        }
    }
}

public interface ICreatureDomainStore : IDomainStore<ICreatureDomainStoreSession>;

public interface ICreatureDomainStoreSession
{
    ValueTask<CreatureState?> ReadAsync(CharacterId owner, int itemUid, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<CreatureState>> ReadAllAsync(CharacterId owner, CancellationToken cancellationToken);
    ValueTask<CreatureState?> ReadCurrentAsync(CharacterId owner, CancellationToken cancellationToken);
    ValueTask<ItemReference?> FindItemAsync(CharacterId owner, int itemUid, CancellationToken cancellationToken);
    ValueTask<ItemReference> ReadItemAsync(ItemReference reference, CancellationToken cancellationToken);
    ValueTask<ItemReference> WriteAsync(ItemReference reference, CreatureState creature,
        int? expiration, CancellationToken cancellationToken);
    void ReviveIfEmpty(CharacterId owner, int itemUid);
}
