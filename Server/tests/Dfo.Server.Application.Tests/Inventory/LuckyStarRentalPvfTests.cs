using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class LuckyStarRentalPvfTests
{
    [SkippableFact]
    public async Task Current_archive_catalog_jobs_prices_item_points_and_real_equipment_rental_are_consistent()
    {
        Skip.IfNot(World.ContentArchive.Available,World.ContentArchive.SkipReason);
        using var f=new ShopFixture(s=>s.AddSingleton<IItemPlacementDefinitionSource,PvfItemPlacementDefinitionSource>());
        f.SetLevel(85);f.Sql("UPDATE characters SET job=0; UPDATE accounts SET lucky_star=400;");
        var content=f.Services.GetRequiredService<ILuckyStarRentalContentSource>();
        var catalog=content.Load();
        Assert.Equal((400,100000,1),(catalog.Limit,catalog.GoldPerStar,catalog.ClearReward));
        Assert.Equal(14,catalog.Offers.Select(x=>x.CharacterJob).Distinct().Count());
        foreach(var job in catalog.Offers.GroupBy(x=>x.CharacterJob))
            Assert.Equal(new[]{3,5,10,15,20,25,30,40},job.Select(x=>x.Price).Distinct().Order().ToArray());
        foreach(var (id,points) in new[]{(10002998,1),(10003204,200),(10004663,50),(10004918,30),(490001384,50)})
            Assert.Equal(points,await content.RentPointsAsync(id,default));
        var quote=f.Services.GetRequiredService<IEquipmentRentalRuleService>();
        foreach(var offer in catalog.Offers)
        {
            var priced=await quote.QuoteAsync(offer.ItemId,offer.CharacterJob!.Value,85,f.Clock.GetUtcNow(),default);
            var usable=(await content.UsableJobsAsync(offer.ItemId,default))!.Contains(offer.CharacterJob.Value);
            Assert.True(priced.Rejection==(usable?EquipmentRentalRejection.None:EquipmentRentalRejection.WrongJob),$"Item {offer.ItemId},job {offer.CharacterJob}: {priced.Rejection}");
        }
        var scope=(DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>();
        await using var tx=scope.Begin();
        var result=await f.Services.GetRequiredService<IEquipmentRentalDomainService>().RentAsync(new(1),new(1),401000037,default);
        Assert.True(result.Succeeded);
        Assert.Equal(397,result.Panel.LuckyStar);
        Assert.Equal(401000037,Assert.Single(result.Panel.Items).ItemId);
        Assert.Equal(f.Clock.GetUtcNow().AddHours(24).ToUnixTimeSeconds(),Assert.Single(result.Panel.Items).ExpireTime);
        await tx.CommitAsync(default);
        Assert.Equal(1,f.Count(401000037));
    }
}
