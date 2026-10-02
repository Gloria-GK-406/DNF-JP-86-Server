using System.Globalization;
using System.Text;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;

namespace Dfo.Server.Application.Administration;

internal sealed class GmOfficialMailDomainService(IGmMailPersistence persistence, IGmAccessDomainService access,
    IGmMailRuleService rules, IMailboxDomainService mailbox, IWorldItemTable worldItems,
    ICharacterTable characters, IGmAuditDomainService audit, IDomainTransactionAccessor transactions,
    TimeProvider clock) : IGmOfficialMailDomainService
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static GmMailInput Empty => new("", "", [], [], 0);
    public async ValueTask<GmMailQueryResult> QueryAsync(string secret, GmMailQuery kind, GmPlayerFilter filter, Guid id, GmMailInput? input, CancellationToken token)
    {
        var actor = await AuthorizeAsync(secret, token);
        CheckFilter(filter);
        switch (kind)
        {
            case GmMailQuery.Draft: return new(await persistence.DraftAsync(actor.Id, token) ?? new GmMailDocument(Guid.Empty, "草稿", 0, Empty));
            case GmMailQuery.Templates: return new(await persistence.TemplatesAsync(actor.Id, token));
            case GmMailQuery.Recipients:
                var page = await persistence.RecipientsAsync(filter, token);
                return new(new GmPage<GmMailRecipient>(page.Items.Select(Recipient).ToArray(), page.Total, filter.Page, filter.PageSize, clock.GetUtcNow()));
            case GmMailQuery.Operations: return new(await persistence.OperationsAsync(actor.Id, filter.Page, filter.PageSize, token));
            case GmMailQuery.Operation:
                return new((await persistence.OperationAsync(actor.Id, RequireId(id), token))?.Receipt ?? throw Error(GmMailRefusal.NotFound, "未找到已提交的发送记录；请保留原编号查询或重试。"));
            case GmMailQuery.Prepare:
                var normalized = rules.Normalize(input!, true); var key = RequireId(id);
                var old = await persistence.OperationAsync(actor.Id, key, token);
                if (old is not null)
                {
                    Match(old, normalized);
                    return new(new GmMailPreparation(key, normalized, old.Receipt.Recipients.Select(x => new AccountId(x.AccountId)).Distinct().ToArray()));
                }
                var targets = await TargetsAsync(normalized, token);
                return new(new GmMailPreparation(key, normalized, targets.Select(x => new AccountId(x.AccountId)).Distinct().ToArray()));
            case GmMailQuery.Preview:
                var content = rules.Normalize(input!, true);
                var recipients = await TargetsAsync(content, token);
                var grants = await rules.PrepareItemsAsync(content, clock.GetUtcNow(), token);
                var text = rules.DisplayText(content);
                return new(new GmMailPreview(content, text, Utf8.GetByteCount(text), recipients.Select(Recipient).ToArray(), grants.Select(x => x.Preview).ToArray(),
                    (long)content.Gold * recipients.Count, content.Items.Sum(x => (long)x.Count) * recipients.Count));
            default: throw Error(GmMailRefusal.Invalid, "不支持的邮件查询。");
        }
    }
    public async ValueTask<GmMailDocument> EditAsync(string secret, GmMailEdit kind, Guid id, GmMailEditorRequest request, CancellationToken token)
    {
        RequireTransaction(); await persistence.AcquireWriteAsync(token);
        var actor = await AuthorizeAsync(secret, token);
        var expectedRevision = request.ExpectedRevision ?? throw Error(GmMailRefusal.Invalid, "保存或删除必须提供版本号。");
        if (expectedRevision < 0) throw Error(GmMailRefusal.Invalid, "版本号无效。");
        var input = rules.Normalize(new(request.Title, request.Body, request.RecipientIds, request.Items, request.Gold), false);
        GmMailDocument result;
        if (kind == GmMailEdit.Draft)
        {
            var old = await persistence.DraftAsync(actor.Id, token); Version(old, expectedRevision);
            result = new(Guid.Empty, "草稿", checked((old?.Revision ?? 0) + 1), input);
            await persistence.SaveDraftAsync(actor.Id, result, token);
        }
        else
        {
            var templates = await persistence.TemplatesAsync(actor.Id, token);
            var old = templates.FirstOrDefault(x => x.Id == id);
            if (kind != GmMailEdit.CreateTemplate && old is null) throw Error(GmMailRefusal.NotFound, "模板不存在。");
            Version(kind == GmMailEdit.CreateTemplate ? null : old, expectedRevision);
            if (kind == GmMailEdit.DeleteTemplate)
            {
                await persistence.DeleteTemplateAsync(actor.Id, RequireId(id), token); result = old!;
            }
            else
            {
                var name = request.Name?.Trim();
                if (name is not { Length: >= 1 and <= 64 }) throw Error(GmMailRefusal.Invalid, "模板名称须为 1..64 个字符。");
                if (templates.Any(x => x.Name == name && x.Id != id) || kind == GmMailEdit.CreateTemplate && templates.Count >= 20)
                    throw Error(GmMailRefusal.Conflict, "模板名称重复或已达到 20 份上限。");
                result = new(kind == GmMailEdit.CreateTemplate ? Guid.NewGuid() : id, name, checked((old?.Revision ?? 0) + 1), input with { RecipientIds = [] });
                await persistence.SaveTemplateAsync(actor.Id, result, token);
            }
        }
        await audit.AppendAsync(new(Guid.NewGuid().ToString("N"), clock.GetUtcNow(), kind == GmMailEdit.Draft ? "SaveMailDraft" : "EditMailTemplate",
            actor.Id, actor.LoginName, result.Id.ToString("N"), "Succeeded", kind.ToString()), token);
        return result;
    }
    public async ValueTask<GmMailSendResult> SendAsync(string secret, GmMailPreparation preparation, CancellationToken token)
    {
        RequireTransaction(); await persistence.AcquireWriteAsync(token);
        var actor = await AuthorizeAsync(secret, token);
        var input = rules.Normalize(preparation.Input, true); var id = RequireId(preparation.RequestId);
        var old = await persistence.OperationAsync(actor.Id, id, token);
        if (old is not null) { Match(old, input); return new(old.Receipt, true); }
        var targets = await TargetsAsync(input, token);
        if (!targets.Select(x => new AccountId(x.AccountId)).Distinct().OrderBy(x => x.Value).SequenceEqual(preparation.Accounts.Distinct().OrderBy(x => x.Value)))
            throw Error(GmMailRefusal.Changed, "收件人的账号归属已改变，请重新预览。");
        var at = clock.GetUtcNow(); var expires = at.AddDays(30);
        var grants = await rules.PrepareItemsAsync(input, at, token);
        var sent = new List<GmMailSentRecipient>();
        var instanceValues = new HashSet<int>();
        foreach (var target in targets)
        {
            var builder = MailBuilder.FromSystem(new CharacterId(target.CharacterId)).WithText(input.Title, input.Body).ExpiresAt(expires);
            var uids = new List<int>();
            foreach (var grant in grants)
            {
                if (grant.Preview.Kind == "equipment")
                    for (var count = 0; count < grant.Requested.Count; count++)
                    {
                        int instanceValue;
                        do { instanceValue = Random.Shared.Next(1, int.MaxValue); } while (!instanceValues.Add(instanceValue));
                        var core = grant.Core with { Value = instanceValue };
                        var uid = await worldItems.InsertAsync(core, null, null, token);
                        uids.Add(uid); builder.AddEquipment(new ItemIdentity(uid));
                    }
                else builder.AddStackable(grant.Requested.ItemId, grant.Requested.Count);
            }
            if (input.Gold > 0) builder.AddGold(input.Gold);
            var mailed = await mailbox.SendAsync(builder.Build(), token);
            await persistence.AssertMailWireRangeAsync(mailed.MailId, token);
            sent.Add(new(Recipient(target), target.AccountId, mailed.MailId, uids.ToArray()));
        }
        var receipt = new GmMailReceipt(id, actor.Id, actor.LoginName, at, expires, input, sent);
        await persistence.SaveOperationAsync(actor.Id, new(rules.PayloadHash(input), receipt), token);
        await audit.AppendAsync(new(Guid.NewGuid().ToString("N"), at, "SendOfficialMail", actor.Id, actor.LoginName,
            id.ToString("N"), "Succeeded", $"已向 {sent.Count.ToString(CultureInfo.InvariantCulture)} 个角色发送"), token);
        return new(receipt, false);
    }
    public async ValueTask<bool> RecordFailureAsync(string secret, Guid requestId, string outcome, string reason, CancellationToken token)
    {
        RequireTransaction(); await persistence.AcquireWriteAsync(token);
        var actor = await AuthorizeAsync(secret, token);
        if (outcome is not ("Rejected" or "RolledBack" or "Unknown")) throw new ArgumentException("Invalid outcome.");
        await audit.AppendAsync(new(Guid.NewGuid().ToString("N"), clock.GetUtcNow(), "SendOfficialMail", actor.Id, actor.LoginName,
            requestId.ToString("N"), outcome, reason), token); return true;
    }
    private async ValueTask<GmIdentity> AuthorizeAsync(string secret, CancellationToken token)
    {
        var actor = await access.IdentifyAsync(secret, token) ?? throw Error(GmMailRefusal.Unauthorized, "会话已失效，请重新登录。");
        if (!actor.Permissions.HasFlag(GmPermission.SendMail)) throw Error(GmMailRefusal.Forbidden, "没有发送官方邮件的权限。");
        return actor;
    }
    private async ValueTask<IReadOnlyList<GmMailRecipientRecord>> TargetsAsync(GmMailInput input, CancellationToken token)
    {
        var result = new List<GmMailRecipientRecord>();
        foreach (var id in input.RecipientIds)
        {
            var target = (await persistence.RecipientsAsync(new(CharacterId: id), token)).Items.SingleOrDefault()
                ?? throw Error(GmMailRefusal.Invalid, $"收件角色 {id} 不存在或已删除。");
            var owner = await characters.FindOwnerAsync(new CharacterId(id), token);
            if (owner?.Value != target.AccountId) throw Error(GmMailRefusal.Changed, "收件人归属已改变。");
            result.Add(target);
        }
        return result;
    }
    private void Match(GmMailOperation old, GmMailInput input)
    { if (old.PayloadHash != rules.PayloadHash(input)) throw Error(GmMailRefusal.Conflict, "该操作编号已经发送其它内容；请保留原结果，新发送使用新编号。"); }
    private static GmMailRecipient Recipient(GmMailRecipientRecord value)
    {
        string name; string status; string? hex = null;
        if (value.NameBytes is null) { name = "姓名字节缺失"; status = "missing"; }
        else try { name = Utf8.GetString(value.NameBytes); status = "valid"; }
            catch (DecoderFallbackException) { hex = Convert.ToHexString(value.NameBytes); name = $"姓名编码异常（hex:{hex}）"; status = "invalid"; }
        return new(value.CharacterId, name, status, hex, value.Level, value.Job, value.GrowType);
    }
    private static void Version(GmMailDocument? value, long expected)
    { if ((value?.Revision ?? 0) != expected) throw Error(GmMailRefusal.Conflict, "内容已在另一页面改变，请重新加载后保存。"); }
    private static Guid RequireId(Guid id) => id != Guid.Empty ? id : throw Error(GmMailRefusal.Invalid, "操作编号无效。");
    private void RequireTransaction() { if (transactions.Current is null) throw new InvalidOperationException("GM mail mutation requires the command transaction."); }
    private static void CheckFilter(GmPlayerFilter filter)
    { if (filter.Page is < 1 or > 1000000 || filter.PageSize is < 1 or > 100 || filter.CharacterId <= 0 || filter.Q?.Length > 128) throw Error(GmMailRefusal.Invalid, "查询参数无效。"); }
    private static GmMailException Error(GmMailRefusal reason, string text) => new(reason, text);
}
