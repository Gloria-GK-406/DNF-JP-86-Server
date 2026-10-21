using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Mail;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class QuestHelperPuppetPvfTests
{
    [Dfo.Server.Testing.PvfFact]
    public async Task Real_puppet_templates_share_the_mail_deadline_and_claim_keeps_the_original_expiration()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var shop = new CeraShopFixture(useArchive: true);
            var f = shop.Host;
            f.Clock.Advance(new DateTimeOffset(2026, 10, 20, 23, 0, 0, TimeSpan.FromHours(8)) - f.Clock.GetUtcNow());
            f.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,582,4000000000)");
            var puppets = f.Services.GetRequiredService<IQuestHelperPuppetDomainService>();
            var scope = (DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>();
            await using (var tx = scope.Begin())
            {
                Assert.True(await puppets.EnsureDailyAsync(f.Session.AccountId, f.Session.CharacterId, default));
                await tx.CommitAsync(default);
            }
            var mailbox = f.Services.GetRequiredService<IMailboxDomainService>();
            var mail = Assert.Single(await mailbox.InboxAsync(f.Session.CharacterId));
            var expected = new DateTimeOffset(2026, 10, 21, 6, 0, 0, TimeSpan.FromHours(8));
            Assert.Equal(expected, mail.ExpiresAt);
            Assert.Equal([2681922, 2681923, 2681924, 2681925], mail.Attachments.Select(a => Assert.IsType<MailAsset.Stackable>(a.Asset).ItemId));
            f.Clock.Advance(TimeSpan.FromHours(1));
            await mailbox.ClaimAsync(f.Session.CharacterId, mail.Attachments[0].Id);
            var held = await f.Services.GetRequiredService<ICharacterItemTable>().ReadByCharacterAsync(f.Session.CharacterId, default);
            Assert.Equal(expected.ToUnixTimeSeconds(), Assert.Single(held, x => x.Core.ItemId == 2681922).Core.ExpireTime);
            await using (var tx = scope.Begin())
            {
                Assert.False(await puppets.EnsureDailyAsync(f.Session.AccountId, f.Session.CharacterId, default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(mail.Id, Assert.Single(await mailbox.InboxAsync(f.Session.CharacterId)).Id);
        });
    }
}
