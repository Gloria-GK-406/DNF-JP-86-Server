using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.PlayerPortal;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.PlayerPortal;

namespace Dfo.Server.Application.PlayerPortal;

/// <remarks>
/// The account is always the session's (RQ-003): nothing in the request names it. Account, characters and their online
/// state come from the GM player directory, projected down to what a player may see.
/// </remarks>
internal sealed class PlayerPortalAccountDomainService(IPlayerPortalAccessDomainService access, IPlayerPortalPersistence portal,
    IGmPlayerDirectoryDomainService directory, IDailyResetRuleService days, TimeProvider clock) : IPlayerPortalAccountDomainService
{
    private const int CharacterPage = 100;

    public async ValueTask<PlayerAccountView> MeAsync(string secret, CancellationToken token)
    {
        var identity = await access.AuthorizeAsync(secret, token);
        var account = (await directory.AccountAsync(identity.AccountId, token)).Value
            ?? throw new PlayerPortalException(PlayerPortalRefusal.Unauthenticated, "请先登录。");
        var characters = new List<PlayerCharacterView>();
        for (var page = 1; ; page++)
        {
            var batch = await directory.CharactersAsync(new GmPlayerFilter(page, CharacterPage, AccountId: identity.AccountId), token);
            characters.AddRange(batch.Items.Where(character => !character.IsDeleted && character.AccountId == identity.AccountId)
                .Select(character => new PlayerCharacterView(character.CharacterId, character.Name, character.Job, character.Level, character.IsOnline)));
            if ((long)page * CharacterPage >= batch.Total || batch.Items.Count == 0) break;
        }
        var settings = await portal.ReadSettingsAsync(token);
        var day = days.PeriodDay(clock.GetUtcNow());
        var today = new PlayerUsageView(
            await PlayerPortalDailyUsage.CeraAsync(portal, identity.AccountId, PlayerPortalRuleService.Cera, day, token),
            await PlayerPortalDailyUsage.CeraAsync(portal, identity.AccountId, PlayerPortalRuleService.TokenCera, day, token),
            await PlayerPortalDailyUsage.MailsAsync(portal, identity.AccountId, day, token));
        return new(identity.AccountId, identity.LoginName, new(account.Balances.Cera, account.Balances.TokenCera), characters,
            new(settings.CeraEnabled, settings.MailEnabled, settings.Limits, today));
    }

    /// <remarks>Only the switch and the session gate the records: a closed feature still lets the player look (RQ-021).</remarks>
    public async ValueTask<PlayerOperationsView> OperationsAsync(string secret, PlayerPortalOperationKind? kind, int page, int pageSize,
        CancellationToken token)
    {
        var identity = await access.AuthorizeAsync(secret, token);
        if (page is < 1 or > 1000000 || pageSize is < 1 or > 100)
            throw new PlayerPortalException(PlayerPortalRefusal.Invalid, "查询参数无效。");
        var stored = await portal.OperationsAsync(identity.AccountId, kind, page, pageSize, token);
        return new(stored.Items.Select(operation => PlayerPortalReceipts.Parse(operation.ReceiptJson)).ToArray(), stored.Total, page, pageSize);
    }
}
