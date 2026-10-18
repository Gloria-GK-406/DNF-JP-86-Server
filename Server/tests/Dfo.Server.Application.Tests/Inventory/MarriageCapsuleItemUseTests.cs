using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class MarriageCapsuleItemUseTests
{
    [SkippableFact]
    public async Task Real_marriage_capsule_uses_the_common_experience_effect_and_consumes_once()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH")),"Real PVF required.");
        using var fixture=new CeraShopFixture(useArchive:true); fixture.Host.SetLevel(30);
        await fixture.Host.Seed(69,10002910,1);
        var before=fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1");
        var result=await fixture.Host.Services.GetRequiredService<ICommandBus>().SendAsync(new UseStackableCommand(fixture.Host.Session,new(BusinessInventorySpace.Consumable,4),10002910));
        Assert.Equal(ItemUseStatus.Used,result.Status);Assert.Equal(before+30000,fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(0,fixture.Host.Count(10002910));
    }
}
