using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Data.Mail;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed partial class ItemDeliveryTests
{
    [Fact]
    public async Task AntonRaidRewardsCommitOnceAcrossOnlineInventoryOfflineMailAndRollback()
    {
        using var fixture = Fixture();
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            var service = fixture.Services.GetRequiredService<IAntonRaidRewardGrantDomainService>();
            var instance = Guid.NewGuid();
            var player = new AntonRaidRewardParticipant(new(1), new(1), 1, 0);
            var onlineCard = GrantOf(0, AntonRaidRewardKind.PartyCard, 9001, 3);
            var offlineCard = GrantOf(0, AntonRaidRewardKind.SquadItem, 9001, 4);
            var retry = GrantOf(0, AntonRaidRewardKind.RetryCertificate, 9001, 1);
            var offlineGold = GrantOf(0, AntonRaidRewardKind.Gold, 0, 160000);
            var onlineGold = GrantOf(1, AntonRaidRewardKind.Gold, 0, 300);

            Assert.Equal(3, Assert.Single((await Settle(onlineCard, online: true)).Delivery!.Inventory).Count);
            Assert.Equal(3, fixture.Count(9001));
            Assert.Empty(await Mail(fixture).InboxAsync(player.Character));

            var rolledBack = await Settle(offlineCard, online: false, commit: false);
            Assert.True(rolledBack.Claimed);
            Assert.Equal(4, rolledBack.Delivery!.MailedCount);
            Assert.Empty(await Mail(fixture).InboxAsync(player.Character));

            Assert.Equal(4, (await Settle(offlineCard, online: false)).Delivery!.MailedCount);
            Assert.False((await Settle(offlineCard, online: true)).Claimed);
            Assert.False((await Settle(onlineCard, online: false)).Claimed);
            Assert.True((await Settle(retry, online: true)).Claimed);
            Assert.False((await Settle(retry with { Phase = 1 }, online: false)).Claimed);
            Assert.True((await Settle(offlineGold, online: false)).Claimed);
            Assert.False((await Settle(offlineGold, online: true)).Claimed);
            var before = fixture.Gold;
            Assert.Equal(before + 300, (await Settle(onlineGold, online: true)).Gold);
            Assert.False((await Settle(onlineGold, online: true)).Claimed);
            Assert.Equal(before + 300, fixture.Gold);
            Assert.Equal(3, fixture.Count(9001));

            var mails = await Mail(fixture).InboxAsync(player.Character);
            Assert.Contains(mails, mail => mail.DeliveryKey == $"raid:{instance:N}:retry:{player.Character.Value}");
            var assets = mails.SelectMany(mail => mail.Attachments.Select(attachment => attachment.Asset)).ToArray();
            Assert.Equal(3, assets.Length);
            Assert.Contains(new MailAsset.Stackable(9001, 4), assets);
            Assert.Contains(new MailAsset.Stackable(9001, 1), assets);
            Assert.Contains(new MailAsset.Gold(160000), assets);

            AntonRaidRewardGrant GrantOf(byte phase, AntonRaidRewardKind kind, int item, int qty) =>
                new(instance, phase, player, kind, new(item, qty), DateTimeOffset.UnixEpoch);

            async Task<AntonRaidRewardReceipt> Settle(AntonRaidRewardGrant grant, bool online, bool commit = true)
            {
                await using var transaction = Scope(fixture).Begin();
                var result = await service.GrantAsync(grant, online, default);
                if (commit) await transaction.CommitAsync(default);
                return result;
            }
        });
    }
}
