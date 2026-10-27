using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal interface IItemExpirationRuleService
{
    ItemSnapshot Prepare(ItemSnapshot item, ItemLifetimeDefinition lifetime, DateTimeOffset at);
}

internal sealed class ItemExpirationRuleService(IDailyResetRuleService reset) : IItemExpirationRuleService
{
    public ItemSnapshot Prepare(ItemSnapshot item, ItemLifetimeDefinition lifetime, DateTimeOffset at)
    {
        if (!lifetime.IsLimited || item.IsMirror) return item;
        if (!item.Lifetime.IsPermanent) return item;
        long deadline = long.MaxValue;
        if (lifetime.AbsoluteExpiration > 0) deadline = lifetime.AbsoluteExpiration;
        if (lifetime.Days > 0) deadline = Math.Min(deadline, checked(at.ToUnixTimeSeconds() + (long)lifetime.Days * 86400));
        if (lifetime.DailyDelete) deadline = Math.Min(deadline, reset.PeriodStart(at).AddDays(1).ToUnixTimeSeconds());
        var expires = checked((int)Math.Clamp(deadline, 1, int.MaxValue));
        return item.LegacyKind == 8
            ? item with { AvatarDetail = (item.AvatarDetail ?? new(0, 0, new byte[30], 0, 0, 0)) with { ExpireDate = expires } }
            : item with { ExpireTime = expires };
    }
}
