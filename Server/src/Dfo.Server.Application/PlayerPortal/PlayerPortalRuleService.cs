using System.Security.Cryptography;
using System.Text;
using Dfo.Server.Data.PlayerPortal;

namespace Dfo.Server.Application.PlayerPortal;

/// <summary>The player portal's rules: what settings an administrator may save, and what a player may add.</summary>
internal sealed class PlayerPortalRuleService
{
    /// <summary>A limit never exceeds what a wallet balance can hold.</summary>
    internal const long MaximumLimit = int.MaxValue;

    internal const string Cera = "Cera";
    internal const string TokenCera = "TokenCera";

    public PlayerPortalSettings Validate(PlayerSettingsChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Limits is not { } limits) throw new ArgumentException("限额字段缺失。");
        if (change.Revision < 0) throw new ArgumentException("设置版本无效。");
        long[] values = [limits.CeraPerGrant, limits.CeraPerDay, limits.TokenCeraPerGrant, limits.TokenCeraPerDay, limits.MailGoldPerMail, limits.MailsPerDay];
        if (values.Any(value => value is < 0 or > MaximumLimit)) throw new ArgumentException($"限额须为 0..{MaximumLimit} 的整数，0 表示不限。");
        return new(change.Enabled, change.CeraEnabled, change.MailEnabled, limits, change.Revision);
    }

    /// <summary>A player may add only cera or token cera (the Spec's scope), a positive amount, under a request id.</summary>
    public (string Currency, int Amount) NormalizeCera(PlayerCeraRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestId == Guid.Empty) throw Invalid("请求编号无效。");
        if (request.Currency is not (Cera or TokenCera)) throw Invalid("币种无效。");
        if (request.Amount <= 0) throw Invalid("数量须为 1..2147483647 的整数。");
        return (request.Currency, request.Amount);
    }

    /// <summary>The content a request id is bound to; the account is part of the key, not of the content.</summary>
    public string CeraPayloadHash(string currency, int amount) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"cera\n{currency}\n{amount}")));

    /// <summary>
    /// Each currency has its own limits (RQ-020); zero means no limit. <paramref name="usedToday"/> counts only the
    /// succeeded grants of this currency in the current game day (RQ-022).
    /// </summary>
    public void CheckCeraLimits(PlayerPortalLimits limits, string currency, int amount, long usedToday)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var (perGrant, perDay) = currency switch
        {
            Cera => (limits.CeraPerGrant, limits.CeraPerDay),
            TokenCera => (limits.TokenCeraPerGrant, limits.TokenCeraPerDay),
            _ => throw Invalid("币种无效。"),
        };
        if (perGrant > 0 && amount > perGrant)
            throw new PlayerPortalException(PlayerPortalRefusal.LimitExceeded, $"单次最多 {perGrant}。");
        if (perDay > 0 && usedToday + amount > perDay)
            throw new PlayerPortalException(PlayerPortalRefusal.LimitExceeded, $"今日剩余 {Math.Max(0, perDay - usedToday)}。");
    }

    internal static string CurrencyName(string currency) => currency == TokenCera ? "代币券" : "点券";

    private static PlayerPortalException Invalid(string message) => new(PlayerPortalRefusal.Invalid, message);
}
