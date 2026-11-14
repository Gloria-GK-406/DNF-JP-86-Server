using System.Globalization;
using System.Text;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.PlayerPortal;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Administration;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.PlayerPortal;

namespace Dfo.Server.Application.PlayerPortal;

/// <remarks>
/// SD-1: every check and the delivery run in the one command transaction, in a fixed order: write intent, master switch,
/// session, the content, replay, mail switch, recipient, limits, items, then the delivery. The content, the items and
/// the delivery are the GM official mail's own rules and code (SD-8); only the player's checks are added around them.
/// </remarks>
internal sealed class PlayerPortalMailDomainService(IPlayerPortalPersistence portal, IPlayerPortalAccessDomainService access,
    PlayerPortalRuleService rules, IGmMailRuleService mailRules, IGmMailPersistence mailPersistence, ICharacterTable characters,
    IItemCatalogDomainService catalog, OfficialMailDelivery delivery, IGmAuditDomainService audit, IDailyResetRuleService days,
    IDomainTransactionAccessor transactions, TimeProvider clock) : IPlayerPortalMailDomainService
{
    internal const string Action = "PlayerSendMail";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public async ValueTask<PlayerIdentity> AuthorizeCatalogAsync(string? secret, CancellationToken token)
    {
        var identity = await access.AuthorizeAsync(secret, token);
        if (!(await portal.ReadSettingsAsync(token)).MailEnabled) throw MailDisabled();
        return identity;
    }

    // The GM mail's selection key: what the player finds is exactly what the mail rules will send (plan-review R-3).
    public async ValueTask<ItemCatalogPage> SearchItemsAsync(string? secret, ItemCatalogFilter filter, CancellationToken token)
    {
        await AuthorizeCatalogAsync(secret, token);
        return await catalog.SearchAsync(filter, mailRules.SelectionKey, token);
    }

    public async ValueTask<ItemCatalogDetails> ItemDetailsAsync(string? secret, int itemId, CancellationToken token)
    {
        await AuthorizeCatalogAsync(secret, token);
        return await catalog.ItemAsync(itemId, mailRules.SelectionKey, token);
    }

    public async ValueTask<PlayerMailSendResult> SendAsync(string? secret, PlayerMailRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTransaction();
        await portal.AcquireWriteAsync(token);
        var identity = await access.AuthorizeAsync(secret, token);
        // Before the GM rules: they turn a blank body into their default text (RQ-011, plan-review R-3).
        rules.CheckMailContent(request);
        GmMailInput input;
        try { input = mailRules.Normalize(rules.ToMailInput(request), true); }
        catch (GmMailException refusal) { throw new PlayerPortalException(PlayerPortalRefusal.Invalid, refusal.Message); }
        var hash = rules.MailPayloadHash(input);

        // A request id that already succeeded answers as it did, whatever the switch, the recipient and the limits say now (RQ-012).
        if (await portal.FindOperationAsync(identity.AccountId, request.RequestId, token) is { } previous)
            return previous.Kind == PlayerPortalOperationKind.Mail && previous.PayloadHash == hash
                ? new(PlayerPortalReceipts.Parse(previous.ReceiptJson), true)
                : throw new PlayerPortalException(PlayerPortalRefusal.RequestConflict, "该编号已用于其它内容；新操作请使用新编号。");

        var settings = await portal.ReadSettingsAsync(token);
        if (!settings.MailEnabled) throw MailDisabled();
        var recipient = await RecipientAsync(identity.AccountId, request.CharacterId, token);
        var now = clock.GetUtcNow();
        var day = days.PeriodDay(now);
        rules.CheckMailLimits(settings.Limits, input.Gold, await PlayerPortalDailyUsage.MailsAsync(portal, identity.AccountId, day, token));

        IReadOnlyList<GmMailPreparedItem> grants;
        try { grants = await mailRules.PrepareItemsAsync(input, now, token); }
        catch (GmMailException refusal) { throw new PlayerPortalException(PlayerPortalRefusal.Invalid, refusal.Message); }
        var expires = now + OfficialMailDelivery.Lifetime;
        var delivered = await delivery.DeliverAsync(new CharacterId(recipient.CharacterId), input.Title, input.Body, grants, input.Gold, expires, token);

        var name = Name(recipient);
        var summary = Summary(name, grants.Count, input.Gold);
        var receipt = PlayerPortalReceipts.Serialize(new PlayerMailReceipt(request.RequestId, PlayerPortalReceipts.Mail, now, summary,
            PlayerPortalReceipts.Succeeded, recipient.CharacterId, name, input.Title, input.Body,
            grants.Select(grant => new PlayerMailReceiptItem(grant.Preview.ItemId, grant.Preview.Count, grant.Preview.Name, grant.Preview.Kind)).ToArray(),
            input.Gold, expires));
        await portal.SaveOperationAsync(new(identity.AccountId, request.RequestId, PlayerPortalOperationKind.Mail, null, input.Gold,
            hash, receipt, day, now), token);
        await audit.AppendAsync(PlayerPortalAudit.Player(Action, identity.AccountId, identity.LoginName, Target(recipient.CharacterId),
            "Succeeded", $"{summary}，邮件 {delivered.MailId.ToString(CultureInfo.InvariantCulture)}，编号 {request.RequestId:N}", now), token);
        return new(PlayerPortalReceipts.Parse(receipt), false);
    }

    public async ValueTask RecordFailureAsync(PlayerIdentity identity, Guid requestId, int characterId, string outcome, string reason,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (outcome is not ("Rejected" or "RolledBack" or "Unknown")) throw new ArgumentException("Invalid outcome.", nameof(outcome));
        RequireTransaction();
        await portal.AcquireWriteAsync(token);
        await audit.AppendAsync(PlayerPortalAudit.Player(Action, identity.AccountId, identity.LoginName, Target(characterId), outcome,
            $"{reason}，编号 {requestId:N}", clock.GetUtcNow()), token);
    }

    /// <summary>
    /// The recipient must be a live character of the session's account, checked twice as the GM mail does. Missing,
    /// deleted and someone else's all answer the one same refusal, so none can be told apart (RQ-004).
    /// </summary>
    private async ValueTask<GmMailRecipientRecord> RecipientAsync(int accountId, int characterId, CancellationToken token)
    {
        var target = (await mailPersistence.RecipientsAsync(new GmPlayerFilter(1, 1, CharacterId: characterId), token)).Items.SingleOrDefault();
        if (target is null || target.AccountId != accountId
            || (await characters.FindOwnerAsync(new CharacterId(characterId), token))?.Value != accountId)
            throw new PlayerPortalException(PlayerPortalRefusal.NotFound, "收件角色不存在。");
        return target;
    }

    private static string Name(GmMailRecipientRecord recipient)
    {
        if (recipient.NameBytes is { } bytes)
            try { return Utf8.GetString(bytes); }
            catch (DecoderFallbackException) { }
        return $"角色 {recipient.CharacterId.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string Summary(string name, int kinds, int gold)
    {
        var parts = new List<string>();
        if (kinds > 0) parts.Add($"物品 {kinds.ToString(CultureInfo.InvariantCulture)} 种");
        if (gold > 0) parts.Add($"金币 {gold.ToString(CultureInfo.InvariantCulture)}");
        return $"发给 {name}：{(parts.Count == 0 ? "正文" : string.Join("、", parts))}";
    }

    private static string Target(int characterId) => $"角色 {characterId.ToString(CultureInfo.InvariantCulture)}";

    private static PlayerPortalException MailDisabled() => new(PlayerPortalRefusal.FeatureDisabled, "发邮件已关闭。");

    private void RequireTransaction()
    {
        if (transactions.Current is null) throw new InvalidOperationException("Player mail requires a command transaction.");
    }
}
