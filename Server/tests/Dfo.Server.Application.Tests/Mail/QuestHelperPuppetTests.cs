using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
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
        Assert.Empty(await f.Mail.InboxAsync(Recipient));
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
        Assert.Equal([3257, 3258, 3259], mails.Single(m => m.Title.Length > 0).Attachments
            .Select(a => Assert.IsType<MailAsset.Stackable>(a.Asset).ItemId));
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
    public async Task Puppets_BusinessDayChangesAtSixAndReplacesUnclaimedMail()
    {
        using var f = PuppetFixture(); // 08:00 China time.
        Assert.True(await CheckPuppets(f));
        var old = Assert.Single(await f.Mail.InboxAsync(Recipient));
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero), old.ExpiresAt);
        f.Clock.Advance(TimeSpan.FromHours(21).Add(TimeSpan.FromMinutes(59)));
        Assert.False(await CheckPuppets(f)); // Next day 05:59 is still the same business day.
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await CheckPuppets(f));
        var replacement = Assert.Single(await f.Mail.InboxAsync(Recipient));
        Assert.NotEqual(old.Id, replacement.Id);
        await AssertMailFailureAsync(MailFailure.NotFound, () => f.Mail.ClaimAsync(Recipient, old.Attachments[0].Id).AsTask());
        Assert.False(await CheckPuppets(f));
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

    [Fact]
    public async Task Puppets_LateClaimPreservesDeadlineAndSixStartsTheNextPeriod()
    {
        using var f = PuppetFixture();
        Assert.True(await CheckPuppets(f));
        var old = Assert.Single(await f.Mail.InboxAsync(Recipient));
        var deadline = new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero);
        Assert.Equal(deadline, old.ExpiresAt);
        Assert.StartsWith("quest-helper:", old.DeliveryKey);
        f.Clock.Advance(TimeSpan.FromHours(22).Subtract(TimeSpan.FromSeconds(1)));
        await f.Mail.ClaimAsync(Recipient, old.Attachments[0].Id);
        var held = await f.Services.GetRequiredService<ICharacterItemTable>().ReadByCharacterAsync(Recipient, default);
        Assert.Equal(deadline.ToUnixTimeSeconds(), Assert.Single(held).Core.ExpireTime);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        await AssertMailFailureAsync(MailFailure.Expired, () => f.Mail.ClaimAsync(Recipient, old.Attachments[1].Id).AsTask());
        Assert.True(await CheckPuppets(f));
        Assert.Equal(0, await f.StackAsync(Recipient, 3257));
        var current = Assert.Single(await f.Mail.InboxAsync(Recipient));
        Assert.Equal(deadline.AddDays(1), current.ExpiresAt);
        Assert.Equal(4, current.Attachments.Length);
        using var reopened = f.Reopen();
        Assert.False(await CheckPuppets(reopened));
        Assert.Equal(current.Id, Assert.Single(await reopened.Mail.InboxAsync(Recipient)).Id);
    }

    [Fact]
    public async Task Puppets_RolloverFailureRestoresOldInventoryMailAndDailyMarker()
    {
        using var f = PuppetFixture();
        Assert.True(await CheckPuppets(f));
        var old = Assert.Single(await f.Mail.InboxAsync(Recipient));
        await f.Mail.ClaimAsync(Recipient, old.Attachments[0].Id);
        f.Clock.Advance(TimeSpan.FromDays(1));
        f.Sql("CREATE TRIGGER fail_rollover_mail BEFORE INSERT ON mailbox_messages BEGIN SELECT RAISE(ABORT,'rollover failure'); END");
        await Assert.ThrowsAsync<SqliteException>(() => CheckPuppets(f));
        Assert.Equal(1, await f.StackAsync(Recipient, 3257));
        Assert.Equal(0, f.Scalar($"SELECT deleted_flag FROM mailbox_recipients WHERE message_id={old.Id}"));
        f.Sql("DROP TRIGGER fail_rollover_mail");
        Assert.True(await CheckPuppets(f));
        Assert.Equal(0, await f.StackAsync(Recipient, 3257));
        Assert.Equal(4, Assert.Single(await f.Mail.InboxAsync(Recipient)).Attachments.Length);
    }

    [Fact]
    public async Task Puppets_FirstLoginExactlyAtSixUsesTomorrowDeadline()
    {
        using var f = PuppetFixture();
        f.Clock.Advance(TimeSpan.FromHours(22));
        Assert.True(await CheckPuppets(f));
        Assert.Equal(new DateTimeOffset(2026, 9, 25, 22, 0, 0, TimeSpan.Zero),
            Assert.Single(await f.Mail.InboxAsync(Recipient)).ExpiresAt);
    }

    [Fact]
    public async Task Puppets_OldRollingTwentyFourHourStockDoesNotBlockTheNewPeriod()
    {
        using var f = PuppetFixture();
        var oldCreation = f.Clock.GetUtcNow().AddHours(-9); // Yesterday 23:00 China time.
        await f.SeedStackAsync(Recipient, 121, 3257, 1, 3,
            expireTime: checked((int)oldCreation.AddDays(1).ToUnixTimeSeconds()));
        await f.SeedStackAsync(Recipient, 122, 1000, 2, 3);
        Assert.True(await CheckPuppets(f));
        Assert.Equal(0, await f.StackAsync(Recipient, 3257));
        Assert.Equal(2, await f.StackAsync(Recipient, 1000));
        Assert.Equal(4, Assert.Single(await f.Mail.InboxAsync(Recipient)).Attachments.Length);
    }

    [Fact]
    public async Task Puppets_ClaimRacingRolloverCannotReceiveTheOldBatch()
    {
        using var f = PuppetFixture();
        Assert.True(await CheckPuppets(f));
        var old = Assert.Single(await f.Mail.InboxAsync(Recipient));
        f.Clock.Advance(TimeSpan.FromHours(22));
        using var other = f.Reopen();
        var claim = Task.Run(() => AttemptAsync(() => other.Mail.ClaimAsync(Recipient, old.Attachments[0].Id).AsTask()));
        var rollover = Task.Run(() => CheckPuppets(f));
        await Task.WhenAll(claim, rollover);
        Assert.True(await rollover);
        var error = Assert.IsType<MailException>(await claim);
        Assert.Contains(error.Reason, new[] { MailFailure.Expired, MailFailure.NotFound });
        Assert.Equal(0, await f.StackAsync(Recipient, 3257));
        Assert.Equal(4, Assert.Single(await f.Mail.InboxAsync(Recipient)).Attachments.Length);
    }

    [Fact]
    public async Task Puppets_LegacyMailIsConvertedWithoutRegrantingClaimedOrConsumedAttachments()
    {
        using var f = PuppetFixture();
        await f.Mail.SendAsync(MailBuilder.FromSystem(Recipient).WithText("魔王之契约：任务助手", "old")
            .AddStackable(3257, 1).AddStackable(3258, 1).Build());
        var old = Assert.Single(await f.Mail.InboxAsync(Recipient));
        await f.Mail.ClaimAsync(Recipient, old.Attachments[0].Id);
        Assert.True(await CheckPuppets(f));
        var held = await f.Services.GetRequiredService<ICharacterItemTable>().ReadByCharacterAsync(Recipient, default);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds(), Assert.Single(held).Core.ExpireTime);
        Assert.DoesNotContain(await f.Mail.InboxAsync(Recipient), m => m.Id == old.Id);
        Assert.DoesNotContain((await f.Mail.InboxAsync(Recipient)).SelectMany(m => m.Attachments),
            a => a.Asset is MailAsset.Stackable { ItemId: 3257 });
        await f.Mail.ClaimAsync(Recipient, (await f.Mail.InboxAsync(Recipient)).SelectMany(m => m.Attachments)
            .Single(a => a.Asset is MailAsset.Stackable { ItemId: 3258 }).Id);
        f.Sql("DELETE FROM item_slots WHERE owner_scope='character' AND owner_id=2");
        Assert.False(await CheckPuppets(f));
        Assert.DoesNotContain((await f.Mail.InboxAsync(Recipient)).SelectMany(m => m.Attachments.Where(a => !a.Claimed)),
            a => a.Asset is MailAsset.Stackable { ItemId: 3257 or 3258 });
    }
}
