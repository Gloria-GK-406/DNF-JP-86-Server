using Dfo.Server.Data.PlayerPortal;

namespace Dfo.Server.Application.PlayerPortal;

/// <summary>The player portal's rules: what settings an administrator may save.</summary>
internal sealed class PlayerPortalRuleService
{
    /// <summary>A limit never exceeds what a wallet balance can hold.</summary>
    internal const long MaximumLimit = int.MaxValue;

    public PlayerPortalSettings Validate(PlayerSettingsChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Limits is not { } limits) throw new ArgumentException("限额字段缺失。");
        if (change.Revision < 0) throw new ArgumentException("设置版本无效。");
        long[] values = [limits.CeraPerGrant, limits.CeraPerDay, limits.TokenCeraPerGrant, limits.TokenCeraPerDay, limits.MailGoldPerMail, limits.MailsPerDay];
        if (values.Any(value => value is < 0 or > MaximumLimit)) throw new ArgumentException($"限额须为 0..{MaximumLimit} 的整数，0 表示不限。");
        return new(change.Enabled, change.CeraEnabled, change.MailEnabled, limits, change.Revision);
    }
}
