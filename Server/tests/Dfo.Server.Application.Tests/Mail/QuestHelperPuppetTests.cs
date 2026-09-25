using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Mail;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Mail;

public sealed partial class MailboxCoreIntegrationTests
{
    private sealed class PuppetRules : IQuestHelperPuppetRuleService
    {
        public IReadOnlyList<int> PuppetItemIds { get; } = [3257, 3258, 3259, 3260];
    }

    private static MailFixture PuppetFixture(bool subscribed = true)
    {
        var f = new MailFixture(s => s.AddSingleton<IQuestHelperPuppetRuleService>(new PuppetRules()));
        if (subscribed) Subscribe(f);
        return f;
    }

    private static void Subscribe(MailFixture f) => f.Sql(
        "INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,582,4000000000)");

    private static async Task<bool> CheckPuppets(MailFixture f, CharacterId? character = null, AccountId? owner = null)
    {
        var scope = f.Services.GetRequiredService<DomainTransactionScope>();
        await using var tx = scope.Begin();
        var result = await f.Services.GetRequiredService<IQuestHelperPuppetDomainService>()
            .EnsureDailyAsync(owner ?? new AccountId(1), character ?? Recipient, default);
        await tx.CommitAsync(default);
        return result;
    }

    [Fact]
    public async Task Puppets_LoginSendsFourSystemAttachmentsAndRestartDoesNotDuplicate()
    {
        using var f = PuppetFixture();
        Assert.True(await CheckPuppets(f));
        var mail = Assert.Single(await f.Mail.InboxAsync(Recipient));
        Assert.Equal(0, mail.SenderCharacterId);
        Assert.Empty(mail.SenderName);
        Assert.Equal([3257, 3258, 3259, 3260], mail.Attachments.Select(a => Assert.IsType<MailAsset.Stackable>(a.Asset).ItemId));
        Assert.All(mail.Attachments, a => Assert.Equal(1, Assert.IsType<MailAsset.Stackable>(a.Asset).Count));
        using var reopened = f.Reopen();
        Assert.False(await CheckPuppets(reopened));
        Assert.Single(await reopened.Mail.InboxAsync(Recipient));
        // One account's other characters receive their own stock.
        Assert.True(await CheckPuppets(reopened, Stranger));
        Assert.Single(await reopened.Mail.InboxAsync(Stranger));
    }

    [Fact]
    public async Task Puppets_NoSubscriptionDoesNotClaimDayAndExpiredSubscriptionDoesNotSend()
    {
        using var f = PuppetFixture(false);
        Assert.False(await CheckPuppets(f));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
        Subscribe(f);
        Assert.True(await CheckPuppets(f));
        f.Clock.Advance(TimeSpan.FromDays(1));
        f.Sql("UPDATE account_premiums SET end_time=1");
        Assert.False(await CheckPuppets(f));
        Assert.Single(await f.Mail.InboxAsync(Recipient));
    }

    [Fact]
    public async Task Puppets_CombinesBagQuickSlotsCargoAndUnclaimedMailAndMarksFullyStockedDay()
    {
        using var f = PuppetFixture();
        await f.SeedStackAsync(Recipient, 121, 3257, 1, 3);
        await f.SeedStackAsync(Recipient, 3, 3258, 1, 3);
        await f.SeedStackAsync(Recipient, 0, 3259, 1, 3, InventoryContainerType.PersonalCargo);
        await f.Mail.SendAsync(MailBuilder.FromSystem(Recipient).AddStackable(3260, 1).Build());
        Assert.True(await CheckPuppets(f));
        Assert.Single(await f.Mail.InboxAsync(Recipient));
        // Consumption and deletion do not grant another check on the same business day.
        f.Sql("DELETE FROM item_slots WHERE owner_id=2 AND owner_scope='character' AND list_type=0 AND slot_index=121");
        Assert.False(await CheckPuppets(f));
        f.Clock.Advance(TimeSpan.FromDays(1));
        Assert.True(await CheckPuppets(f));
        var mails = await f.Mail.InboxAsync(Recipient);
        Assert.Equal(2, mails.Count);
        var added = Assert.Single(mails.Single(m => m.Title.Length > 0).Attachments);
        Assert.Equal(new MailAsset.Stackable(3257, 1), added.Asset);
    }

    [Fact]
    public async Task Puppets_ClaimedAndExpiredAttachmentsDoNotBlockReplenishment()
    {
        using var f = PuppetFixture();
        await f.Mail.SendAsync(MailBuilder.FromSystem(Recipient).AddStackable(3257, 1).Build());
        var mail = Assert.Single(await f.Mail.InboxAsync(Recipient));
        await f.Mail.ClaimAsync(Recipient, Assert.Single(mail.Attachments).Id);
        f.Sql("DELETE FROM item_slots WHERE owner_id=2 AND owner_scope='character'");
        await f.Mail.SendAsync(MailBuilder.FromSystem(Recipient).AddStackable(3258, 1)
            .ExpiresAt(f.Clock.GetUtcNow().AddSeconds(1)).Build());
        f.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(await CheckPuppets(f));
        Assert.Equal(4, (await f.Mail.InboxAsync(Recipient)).Single(m => m.Title.Length > 0).Attachments.Length);
    }

    [Fact]
    public async Task Puppets_BusinessDayChangesAtSixNotMidnightAndUnclaimedMailPreventsDuplicates()
    {
        using var f = PuppetFixture(); // 08:00 China time.
        Assert.True(await CheckPuppets(f));
        f.Clock.Advance(TimeSpan.FromHours(21).Add(TimeSpan.FromMinutes(59)));
        Assert.False(await CheckPuppets(f)); // Next day 05:59 is still the same business day.
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await CheckPuppets(f));
        Assert.Single(await f.Mail.InboxAsync(Recipient));
    }

    [Fact]
    public async Task Puppets_MarkerFailureRollsBackMailAndAllowsRetry()
    {
        using var f = PuppetFixture();
        f.Sql("CREATE TRIGGER fail_puppet_marker BEFORE INSERT ON character_daily_counters BEGIN SELECT RAISE(ABORT,'marker failure'); END");
        await Assert.ThrowsAsync<SqliteException>(() => CheckPuppets(f));
        Assert.Empty(await f.Mail.InboxAsync(Recipient));
        f.Sql("DROP TRIGGER fail_puppet_marker");
        Assert.True(await CheckPuppets(f));
        Assert.Single(await f.Mail.InboxAsync(Recipient));
    }

    [Fact]
    public async Task Puppets_MailFailureDoesNotClaimDayAndForeignAccountIsRejected()
    {
        using var f = PuppetFixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CheckPuppets(f, owner: new AccountId(99)));
        f.Sql("CREATE TRIGGER fail_puppet_mail BEFORE INSERT ON mailbox_messages BEGIN SELECT RAISE(ABORT,'mail failure'); END");
        await Assert.ThrowsAsync<SqliteException>(() => CheckPuppets(f));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_daily_counters"));
        f.Sql("DROP TRIGGER fail_puppet_mail");
        Assert.True(await CheckPuppets(f));
    }

    [Fact]
    public async Task Puppets_ConcurrentChecksOnlySendOnce()
    {
        using var f = PuppetFixture();
        using var other = f.Reopen();
        var results = await Task.WhenAll(Task.Run(() => CheckPuppets(f)), Task.Run(() => CheckPuppets(other)));
        Assert.Single(results, x => x);
        Assert.Single(await f.Mail.InboxAsync(Recipient));
    }
}
