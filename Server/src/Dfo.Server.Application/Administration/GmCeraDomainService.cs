using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Accounts;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Administration;

namespace Dfo.Server.Application.Administration;

/// <summary>
/// VC-6: "attempted" now means every reminder was registered for sending (whether it reaches the client is the
/// outbound sender's business, RQ-036); "offline" means at least one account had no session in the world to remind.
/// A reminder that could not be composed is an error and is thrown after the other accounts were reminded.
/// </summary>
internal sealed class GmCeraReminderDomainService(ISelectedSessionDomainService sessions, IEventService notifications) : IGmCeraReminderDomainService
{
    public async ValueTask<string> RemindAsync(IReadOnlyList<int> accounts, CancellationToken token)
    {
        var result = "attempted";
        List<int>? failed = null;
        foreach (var id in accounts.Distinct())
        {
            var account = new AccountId(id); var session = sessions.Find(account);
            if (session is null) { result = "offline"; continue; }
            var delivery = await notifications.ProjectAsync(new NotificationAudience.TheseSessions([session]), new AccountWalletChanged(account), token);
            if (delivery.Attempts.Any(x => x.Outcome is NotificationOutcome.Failed)) (failed ??= []).Add(id);
            else if (!delivery.Attempts.Any(x => x.Outcome is NotificationOutcome.Delivered)) result = "offline";
        }
        if (failed is not null)
            throw new InvalidOperationException($"The wallet reminder could not be composed for account(s) {string.Join(", ", failed)}.");
        return result;
    }
}

internal sealed class GmCeraDomainService(IGmCeraPersistence persistence, IGmAccessDomainService access, IGmCeraRuleService rules,
    IAccountWalletDomainService wallets, IGmAuditDomainService audit, IDomainTransactionAccessor transactions,
    TimeProvider clock) : IGmCeraDomainService
{
    public async ValueTask<GmCeraQueryResult> QueryAsync(string secret, GmCeraQuery kind, GmPlayerFilter filter, Guid id, GmCeraInput? input, CancellationToken token)
    {
        var actor = await Authorize(secret, token); CheckFilter(filter);
        switch (kind)
        {
            case GmCeraQuery.Accounts:
                var page = await persistence.AccountsAsync(filter, token); var values = new List<GmCeraAccount>();
                foreach (var row in page.Items) values.Add(new(row.AccountId, row.LoginName, await Wallet(row.AccountId, token)));
                return new(new GmPage<GmCeraAccount>(values, page.Total, filter.Page, filter.PageSize, clock.GetUtcNow()));
            case GmCeraQuery.Normalize: RequireId(id); return new(rules.Normalize(input!));
            case GmCeraQuery.Preview:
                var normalized = rules.Normalize(input!); var changes = await Changes(normalized, token);
                return new(new GmCeraPreview(normalized, changes, (long)normalized.Amount * changes.Length));
            case GmCeraQuery.Operations: return new(await persistence.OperationsAsync(actor.Id, filter.Page, filter.PageSize, token));
            case GmCeraQuery.Operation: return new((await persistence.OperationAsync(actor.Id, RequireId(id), token))?.Receipt
                ?? throw Error(GmCeraRefusal.NotFound, "未找到已提交的发放记录；请保留原编号查询或重试。"));
            default: throw Error(GmCeraRefusal.Invalid, "不支持的点券查询。");
        }
    }
    public async ValueTask<GmCeraGrantResult> GrantAsync(string secret, Guid id, GmCeraInput input, CancellationToken token)
    {
        RequireTransaction(); await persistence.AcquireWriteAsync(token);
        var actor = await Authorize(secret, token); RequireId(id); var normalized = rules.Normalize(input);
        var previous = await persistence.OperationAsync(actor.Id, id, token);
        if (previous is not null)
        {
            if (previous.PayloadHash != rules.PayloadHash(normalized)) throw Error(GmCeraRefusal.Conflict, "该编号已用于其它发放内容；请保留原结果，新发放使用新编号。");
            return new(previous.Receipt, true);
        }
        var changes = await Changes(normalized, token);
        foreach (var change in changes)
            if (!await wallets.TryApplyAsync(new(change.AccountId), change.Before, change.After, token))
                throw Error(GmCeraRefusal.Conflict, "账号余额已改变，本批未提交，请重新核对。");
        var receipt = new GmCeraReceipt(id, actor.Id, actor.LoginName, clock.GetUtcNow(), normalized, changes);
        await persistence.SaveOperationAsync(actor.Id, new(rules.PayloadHash(normalized), receipt), token);
        await audit.AppendAsync(new(Guid.NewGuid().ToString("N"), receipt.CommittedAt, "GrantCera", actor.Id, actor.LoginName, id.ToString("N"),
            "Succeeded", $"{normalized.Currency}：每账号增加 {normalized.Amount}，共 {changes.Length} 个账号"), token);
        return new(receipt, false);
    }
    public async ValueTask<bool> RecordFailureAsync(string secret, Guid id, string outcome, string reason, CancellationToken token)
    {
        RequireTransaction(); await persistence.AcquireWriteAsync(token); var actor = await Authorize(secret, token);
        if (outcome is not ("Rejected" or "RolledBack" or "Unknown")) throw new ArgumentException("Invalid outcome.");
        await audit.AppendAsync(new(Guid.NewGuid().ToString("N"), clock.GetUtcNow(), "GrantCera", actor.Id, actor.LoginName, id.ToString("N"), outcome, reason), token);
        return true;
    }
    private async ValueTask<ImmutableArray<GmCeraBalanceChange>> Changes(GmCeraInput input, CancellationToken token)
    {
        var changes = ImmutableArray.CreateBuilder<GmCeraBalanceChange>();
        foreach (var id in input.AccountIds)
        {
            var row = (await persistence.AccountsAsync(new(AccountId: id), token)).Items.SingleOrDefault()
                ?? throw Error(GmCeraRefusal.Invalid, $"账号 {id} 不存在。");
            var decision = rules.Decide(await Wallet(id, token), input);
            changes.Add(new(id, row.LoginName, decision.Before, decision.After));
        }
        return changes.ToImmutable();
    }
    private async ValueTask<AccountCeraBalances> Wallet(int id, CancellationToken token)
    {
        try { return await wallets.ReadAsync(new(id), token) ?? throw Error(GmCeraRefusal.Invalid, $"账号 {id} 不存在。"); }
        catch (Exception error) when (error is InvalidDataException or InvalidCastException or OverflowException or FormatException)
        { throw Error(GmCeraRefusal.Invalid, $"账号 {id} 钱包数据异常。"); }
    }
    private async ValueTask<GmIdentity> Authorize(string secret, CancellationToken token)
    {
        var actor = await access.IdentifyAsync(secret, token) ?? throw Error(GmCeraRefusal.Unauthorized, "会话已失效，请重新登录。");
        if (!actor.Permissions.HasFlag(GmPermission.GrantCera)) throw Error(GmCeraRefusal.Forbidden, "没有发放点券的权限。");
        return actor;
    }
    private static Guid RequireId(Guid id) => id != Guid.Empty ? id : throw Error(GmCeraRefusal.Invalid, "操作编号无效。");
    private void RequireTransaction() { if (transactions.Current is null) throw new InvalidOperationException("Cera grants require a command transaction."); }
    private static void CheckFilter(GmPlayerFilter filter)
    { if (filter.Page is < 1 or > 1000000 || filter.PageSize is < 1 or > 100 || filter.AccountId <= 0 || filter.Q?.Length > 128) throw Error(GmCeraRefusal.Invalid, "查询参数无效。"); }
    private static GmCeraException Error(GmCeraRefusal reason, string message) => new(reason, message);
}
