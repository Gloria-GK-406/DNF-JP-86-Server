namespace Dfo.Server.Data.Inventory;

/// <summary>All required features, at least one optional alternative, and no features outside Allowed.</summary>
public readonly record struct ItemAdmissionRule
{
    public ItemAdmissionRule(ItemTraits allowed, ItemTraits all = ItemTraits.None, ItemTraits any = ItemTraits.None)
    {
        if ((all & allowed) != all)
            throw new ArgumentException("Required features must be allowed.", nameof(all));
        if (any != ItemTraits.None && (any & allowed) == ItemTraits.None)
            throw new ArgumentException("At least one alternative must be allowed.", nameof(any));
        Allowed = allowed;
        All = all;
        Any = any;
    }

    public ItemTraits Allowed { get; }
    public ItemTraits All { get; }
    public ItemTraits Any { get; }

    public bool Matches(ItemTraits flags) =>
        flags != ItemTraits.None
        && (flags & Allowed) == flags
        && (flags & All) == All
        && (Any == ItemTraits.None || (flags & Any) != ItemTraits.None);
}
