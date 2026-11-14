using System.Globalization;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.PlayerPortal;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.PlayerPortal;

namespace Dfo.Server.Application.PlayerPortal;

/// <remarks>Same level as the daily refill: only a full-permission administrator reads or changes the portal (RQ-020).</remarks>
internal sealed class PlayerPortalAdministrationDomainService(IGmAccessDomainService access, IPlayerPortalPersistence portal,
    PlayerPortalRuleService rules, IGmAuditDomainService audit, TimeProvider clock) : IPlayerPortalAdministrationDomainService
{
    private async ValueTask<GmIdentity> AuthorizeAsync(string secret, CancellationToken token)
    {
        var actor = await access.IdentifyAsync(secret, token) ?? throw new GmMailException(GmMailRefusal.Unauthorized, "会话已失效。");
        if (!actor.Permissions.HasFlag(GmPermission.All)) throw new GmMailException(GmMailRefusal.Forbidden, "仅全权限管理员可管理玩家后台。");
        return actor;
    }

    public async ValueTask<PlayerPortalSettings> SettingsAsync(string gmSecret, CancellationToken token)
    {
        await AuthorizeAsync(gmSecret, token);
        return await portal.ReadSettingsAsync(token);
    }

    public async ValueTask<PlayerPortalSettings> ChangeSettingsAsync(string gmSecret, PlayerSettingsChange change, CancellationToken token)
    {
        await portal.AcquireWriteAsync(token);
        var actor = await AuthorizeAsync(gmSecret, token);
        var settings = rules.Validate(change);
        if (!await portal.SaveSettingsAsync(settings, change.Revision, token))
            throw new PlayerPortalException(PlayerPortalRefusal.SettingsConflict, "设置已被其他管理员修改，请刷新后重试。");
        // Closing ends every player session in the same transaction (RQ-021); while closed none can be made.
        var ended = settings.Enabled ? 0 : await portal.DeleteAllSessionsAsync(token);
        var saved = await portal.ReadSettingsAsync(token);
        var limits = saved.Limits;
        var reason = string.Create(CultureInfo.InvariantCulture,
            $"总开关 {Switch(saved.Enabled)}；点券 {Switch(saved.CeraEnabled)}；邮件 {Switch(saved.MailEnabled)}；限额 点券 {limits.CeraPerGrant}/{limits.CeraPerDay}，代币券 {limits.TokenCeraPerGrant}/{limits.TokenCeraPerDay}，邮件金币 {limits.MailGoldPerMail}，每日封数 {limits.MailsPerDay}；版本 {saved.Revision}");
        if (!saved.Enabled) reason += string.Create(CultureInfo.InvariantCulture, $"；清除玩家会话 {ended} 个");
        await audit.AppendAsync(new(Guid.NewGuid().ToString("N"), clock.GetUtcNow(), "ChangePlayerPortal", actor.Id, actor.LoginName,
            "玩家后台设置", "Succeeded", reason), token);
        return saved;
    }

    public async ValueTask<IReadOnlyList<PlayerPortalSessionEntry>> SessionsAsync(string gmSecret, CancellationToken token)
    {
        await AuthorizeAsync(gmSecret, token);
        return await portal.ListActiveSessionsAsync(clock.GetUtcNow(), token);
    }

    public async ValueTask<int> KickAsync(string gmSecret, int? accountId, CancellationToken token)
    {
        await portal.AcquireWriteAsync(token);
        var actor = await AuthorizeAsync(gmSecret, token);
        if (accountId is <= 0) throw new ArgumentException("账号 ID 必须为正整数。");
        var ended = accountId is { } id ? await portal.DeleteAccountSessionsAsync(id, token) : await portal.DeleteAllSessionsAsync(token);
        await audit.AppendAsync(new(Guid.NewGuid().ToString("N"), clock.GetUtcNow(), "KickPlayerSessions", actor.Id, actor.LoginName,
            accountId is { } target ? PlayerPortalAudit.AccountTarget(target) : "全部玩家", "Succeeded",
            string.Create(CultureInfo.InvariantCulture, $"踢下线会话 {ended} 个")), token);
        return ended;
    }

    private static string Switch(bool on) => on ? "开" : "关";
}
