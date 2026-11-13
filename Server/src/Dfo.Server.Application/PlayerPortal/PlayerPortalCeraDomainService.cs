using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.PlayerPortal;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.PlayerPortal;

namespace Dfo.Server.Application.PlayerPortal;

/// <remarks>
/// SD-1: every check and the credit run in the one command transaction, in a fixed order: write intent, master switch,
/// session, replay, feature switch, limits, then the write. The wallet itself follows the GM grant's rules (SD-8).
/// </remarks>
internal sealed class PlayerPortalCeraDomainService(IPlayerPortalPersistence portal, IPlayerPortalAccessDomainService access,
    PlayerPortalRuleService rules, IGmCeraRuleService ceraRules, IAccountWalletDomainService wallets, IGmAuditDomainService audit,
    IDailyResetRuleService days, IDomainTransactionAccessor transactions, TimeProvider clock) : IPlayerPortalCeraDomainService
{
    internal const string Action = "PlayerGrantCera";
    private const string GrantReason = "玩家自助";

    public async ValueTask<PlayerCeraGrantResult> GrantAsync(string? secret, PlayerCeraRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireTransaction();
        await portal.AcquireWriteAsync(token);
        var identity = await access.AuthorizeAsync(secret, token);
        var (currency, amount) = rules.NormalizeCera(request);
        var hash = rules.CeraPayloadHash(currency, amount);

        // A request id that already succeeded answers as it did, whatever the switches and limits say now (RQ-012).
        if (await portal.FindOperationAsync(identity.AccountId, request.RequestId, token) is { } previous)
            return previous.Kind == PlayerPortalOperationKind.Cera && previous.PayloadHash == hash
                ? new(PlayerPortalReceipts.Parse(previous.ReceiptJson), true)
                : throw new PlayerPortalException(PlayerPortalRefusal.RequestConflict, "该编号已用于其它内容；新操作请使用新编号。");

        var settings = await portal.ReadSettingsAsync(token);
        if (!settings.CeraEnabled) throw new PlayerPortalException(PlayerPortalRefusal.FeatureDisabled, "加点券已关闭。");
        var now = clock.GetUtcNow();
        var day = days.PeriodDay(now);
        rules.CheckCeraLimits(settings.Limits, currency, amount,
            await PlayerPortalDailyUsage.CeraAsync(portal, identity.AccountId, currency, day, token));

        var before = await Wallet(identity.AccountId, token);
        AccountCeraBalances after;
        try { after = ceraRules.Decide(before, new GmCeraInput([identity.AccountId], currency, amount, GrantReason)).After; }
        catch (GmCeraException refusal) { throw new PlayerPortalException(PlayerPortalRefusal.Invalid, refusal.Message); }
        if (!await wallets.TryApplyAsync(new(identity.AccountId), before, after, token))
            throw new PlayerPortalException(PlayerPortalRefusal.Invalid, "账号余额已改变，请刷新后重试。");

        var name = PlayerPortalRuleService.CurrencyName(currency);
        var receipt = PlayerPortalReceipts.Serialize(new PlayerCeraReceipt(request.RequestId, PlayerPortalReceipts.Cera, now,
            $"{name} +{amount}", PlayerPortalReceipts.Succeeded, currency, amount, View(before), View(after)));
        await portal.SaveOperationAsync(new(identity.AccountId, request.RequestId, PlayerPortalOperationKind.Cera, currency, amount,
            hash, receipt, day, now), token);
        var (was, becomes) = currency == PlayerPortalRuleService.Cera ? (before.Cera, after.Cera) : (before.TokenCera, after.TokenCera);
        await audit.AppendAsync(PlayerPortalAudit.Player(Action, identity.AccountId, identity.LoginName,
            PlayerPortalAudit.AccountTarget(identity.AccountId), "Succeeded",
            $"{name} +{amount}，余额 {was} → {becomes}，编号 {request.RequestId:N}", now), token);
        return new(PlayerPortalReceipts.Parse(receipt), false);
    }

    public async ValueTask RecordFailureAsync(PlayerIdentity identity, Guid requestId, string outcome, string reason, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (outcome is not ("Rejected" or "RolledBack" or "Unknown")) throw new ArgumentException("Invalid outcome.", nameof(outcome));
        RequireTransaction();
        await portal.AcquireWriteAsync(token);
        await audit.AppendAsync(PlayerPortalAudit.Player(Action, identity.AccountId, identity.LoginName,
            PlayerPortalAudit.AccountTarget(identity.AccountId), outcome, $"{reason}，编号 {requestId:N}", clock.GetUtcNow()), token);
    }

    private async ValueTask<AccountCeraBalances> Wallet(int accountId, CancellationToken token)
    {
        try
        {
            return await wallets.ReadAsync(new(accountId), token)
                ?? throw new PlayerPortalException(PlayerPortalRefusal.Invalid, "账号钱包数据异常。");
        }
        catch (Exception error) when (error is InvalidDataException or InvalidCastException or OverflowException or FormatException)
        { throw new PlayerPortalException(PlayerPortalRefusal.Invalid, "账号钱包数据异常。"); }
    }

    private static PlayerBalancesView View(AccountCeraBalances balances) => new(balances.Cera, balances.TokenCera);

    private void RequireTransaction()
    {
        if (transactions.Current is null) throw new InvalidOperationException("Player cera grants require a command transaction.");
    }
}
