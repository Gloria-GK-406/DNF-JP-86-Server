namespace Dfo.Server.Data.Inventory;

/// <summary>
/// The one reading of a stored item deadline. Zero is permanent; any other value is the Unix second at
/// which the item ends, so a negative deadline has already passed.
/// </summary>
/// <remarks>
/// <c>int.MaxValue</c> is an ordinary 2038 deadline, not a permanent marker: the A21 client counts it down
/// as roughly eleven years. Avatars keep their deadline in <see cref="InventoryAvatarDetailState.ExpireDate"/>;
/// every other kind uses the core <see cref="ItemSnapshot.ExpireTime"/>. That split is decided here once.
/// </remarks>
public readonly record struct ItemLifetime
{
    private const byte AvatarLegacyKind = 8;

    private ItemLifetime(int deadline) => Deadline = deadline;

    /// <summary>The stored value: zero when permanent, otherwise the Unix second the item ends.</summary>
    public int Deadline { get; }

    public bool IsPermanent => Deadline == 0;

    public static ItemLifetime Permanent => default;

    public static ItemLifetime At(int deadline) => new(deadline);

    public static ItemLifetime Of(byte legacyKind, int expireTime, int? avatarExpireDate) =>
        new(legacyKind == AvatarLegacyKind ? avatarExpireDate ?? 0 : expireTime);

    public bool IsExpired(DateTimeOffset at) => !IsPermanent && Deadline <= at.ToUnixTimeSeconds();

    /// <summary>A21 remaining-time encoding: zero permanent, positive seconds left, -1 once ended.</summary>
    public int RemainingSeconds(DateTimeOffset at)
    {
        if (IsPermanent) return 0;
        var remaining = (long)Deadline - at.ToUnixTimeSeconds();
        return remaining <= 0 ? -1 : checked((int)remaining);
    }
}
