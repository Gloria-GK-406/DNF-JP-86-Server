using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.PackageOpening;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class DarkRepulsorPackageTests
{
    private const int Box = 10007316;
    private const int Weapon = 101600023;

    [SkippableFact]
    public async Task Actual_box_opens_through_00a0_and_delivers_one_permanent_untradeable_weapon_avatar()
    {
        using var shop = Fixture(0);
        await shop.Host.Seed(5, Box, 1);
        await EffectRequestTransport.Send(shop.Host, new PackageOpeningRequest(
            new(1, 0xA0, 0, EffectRequestTransport.FreshSequence(shop.Host)), new(5, [])));
        var response = Assert.IsType<PackageOpeningResponse>(shop.Host.Sender.Messages[0]);
        Assert.Equal(OpeningRejection.None, response.Result.Rejection);
        var avatar = Assert.Single(response.Result.Receipt!.Updates).Core;
        Assert.Equal(Weapon, avatar.ItemId);
        Assert.Equal(8, avatar.LegacyKind);
        Assert.Equal(1, avatar.Count);
        Assert.Equal(1, avatar.TradeRestriction);
        Assert.Equal(0, avatar.AvatarDetail!.ExpireDate);
        Assert.Equal(0, shop.Host.Count(Box));
        Assert.Contains(shop.Host.Sender.Messages.Select(shop.Encode), f => BitConverter.ToUInt16(f, 1) == 0x000E);
        var mail = shop.Host.Services.GetRequiredService<IMailItemRuleService>();
        Assert.Equal(MailItemSendRefusal.InstanceTradeRestricted, (await mail.CanSendAsync(avatar, true, default)).Refusal);
        // A second attempt cannot manufacture a second weapon after the source has gone.
        await EffectRequestTransport.Send(shop.Host, new PackageOpeningRequest(
            new(1, 0xA0, 0, EffectRequestTransport.FreshSequence(shop.Host)), new(5, [])));
        Assert.Equal(1, shop.Host.Count(Weapon));
    }

    [SkippableFact]
    public async Task Wrong_job_preserves_the_box_and_grants_nothing()
    {
        using var shop = Fixture(12);
        await shop.Host.Seed(5, Box, 1);
        var error = await Assert.ThrowsAsync<OpeningRejectedException>(() => Open(shop));
        Assert.Equal(OpeningRejection.ItemUnusable, error.Reason);
        Assert.Equal(1, shop.Host.Count(Box));
        Assert.Equal(0, shop.Host.Count(Weapon));
    }

    [SkippableFact]
    public async Task Reward_write_failure_rolls_back_source_and_allows_retry()
    {
        using var shop = Fixture(0);
        await shop.Host.Seed(5, Box, 1);
        shop.Host.Sql("CREATE TRIGGER fail_weapon BEFORE INSERT ON item_slots BEGIN SELECT RAISE(ABORT,'forced'); END;");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => Open(shop));
        Assert.Equal(1, shop.Host.Count(Box));
        Assert.Equal(0, shop.Host.Count(Weapon));
        shop.Host.Sql("DROP TRIGGER fail_weapon");
        await Open(shop);
        Assert.Equal(0, shop.Host.Count(Box));
        Assert.Equal(1, shop.Host.Count(Weapon));
    }

    private static Task<PackageOpeningResult> Open(CeraShopFixture shop) =>
        OpeningDomainTestDriver.Package(shop.Host, shop.Host.Session, new(5, []));

    private static CeraShopFixture Fixture(int job)
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH")), "PVF_ARCHIVE_PATH is not available.");
        var shop = new CeraShopFixture(useArchive: true);
        shop.Host.Sql($"UPDATE characters SET job={job} WHERE character_id=1;");
        return shop;
    }
}
