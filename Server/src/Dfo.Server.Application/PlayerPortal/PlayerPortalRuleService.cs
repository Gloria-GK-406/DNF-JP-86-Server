using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.PlayerPortal;

namespace Dfo.Server.Application.PlayerPortal;

/// <summary>The player portal's rules: what settings an administrator may save, and what a player may add or mail.</summary>
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

    /// <summary>
    /// The player's own checks of a mail, before the GM mail rules see it. "At least a body or an attachment" (RQ-011) is
    /// judged here on the request as sent: the GM rules replace a blank body with their default text, after which an
    /// empty mail would pass. A blank body is blank exactly as the GM rules judge it; a title alone is no body.
    /// </summary>
    public void CheckMailContent(PlayerMailRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestId == Guid.Empty || request.CharacterId <= 0 || request.Title is null || request.Body is null
            || request.Items is null || request.Items.Any(item => item is null))
            throw Invalid("请求字段无效。");
        if (string.IsNullOrWhiteSpace(request.Body) && request.Items.Length == 0 && request.Gold <= 0)
            throw Invalid("邮件至少要有正文或附件。");
    }

    /// <summary>The GM mail input of a player's mail: the one recipient, and never any equipment option (SD-8).</summary>
    public GmMailInput ToMailInput(PlayerMailRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(request.Title, request.Body, [request.CharacterId],
            request.Items.Select(item => new GmMailItem(item.ItemId, item.Count)).ToArray(), request.Gold);
    }

    /// <summary>The normalized content a mail's request id is bound to: recipient, text, items in order and gold.</summary>
    public string MailPayloadHash(GmMailInput normalized)
    {
        ArgumentNullException.ThrowIfNull(normalized);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("mail\n" + JsonSerializer.Serialize(normalized))));
    }

    /// <summary>
    /// The mail limits (RQ-020); zero means no limit. <paramref name="mailsToday"/> counts only the succeeded mails of
    /// the current game day (RQ-022).
    /// </summary>
    public void CheckMailLimits(PlayerPortalLimits limits, int gold, long mailsToday)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.MailGoldPerMail > 0 && gold > limits.MailGoldPerMail)
            throw new PlayerPortalException(PlayerPortalRefusal.LimitExceeded, $"单封金币最多 {limits.MailGoldPerMail}。");
        if (limits.MailsPerDay > 0 && mailsToday + 1 > limits.MailsPerDay)
            throw new PlayerPortalException(PlayerPortalRefusal.LimitExceeded, $"今日邮件已达上限（{limits.MailsPerDay} 封）。");
    }

    internal static string CurrencyName(string currency) => currency == TokenCera ? "代币券" : "点券";

    private static PlayerPortalException Invalid(string message) => new(PlayerPortalRefusal.Invalid, message);
}
