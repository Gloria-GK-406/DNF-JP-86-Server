using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class MarriageCapsuleItemUseTests
{
    [SkippableFact]
    public async Task Corrected_wedding_guardian_ticket_can_be_picked_up_once_through_the_real_grant_pipeline()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH")),"Real PVF required.");
        using var fixture=new CeraShopFixture(useArchive:true);var f=fixture.Host;
        var runs=f.Services.GetRequiredService<Dfo.Server.Abstractions.World.IDungeonRunDomainService>();
        var cell=new Dfo.Server.Abstractions.Content.MazeCell(0,0);
        var layout=new Dfo.Server.Abstractions.World.DungeonLayout(3541,0,1,1,cell,cell,1,1,
            new Dictionary<Dfo.Server.Abstractions.Content.MazeCell,Dfo.Server.Abstractions.Content.DungeonCellDefinition> { [cell]=Dfo.Server.Abstractions.Content.DungeonCellDefinition.Fixed(1) });
        var run=f.InTransaction(()=>runs.PreparePartyEntry([new(f.Session.AccountId,f.Session.CharacterId,0,true)],layout,0,
            new(cell,1,1,[],[new(7,10002915,1,0,false,Dfo.Server.Abstractions.World.DungeonGroundItemSource.FromActor(1))]),
            Dfo.Server.Abstractions.World.DungeonRunId.New(),DateTimeOffset.UtcNow)).Run;
        var commands=f.Services.GetRequiredService<ICommandBus>();
        var number=Assert.Single(run.CurrentCellState.GroundItems).Number;
        var request=new Dfo.Server.Protocol.Game.GroundPickup.PickUpGroundDropCommand(f.Session.AccountId,f.Session.CharacterId,number,run.RunId,run.RoomVersion);
        var result=await commands.SendAsync(request);
        Assert.Equal(Dfo.Server.Protocol.Game.GroundPickup.GroundPickupStatus.PickedUp,result.Status);
        Assert.Equal(1,f.Count(10002915));Assert.Null(runs.Find(f.Session.CharacterId)!.CurrentCellState.FindGroundItem(number));
        Assert.NotEqual(Dfo.Server.Protocol.Game.GroundPickup.GroundPickupStatus.PickedUp,(await commands.SendAsync(request)).Status);
        Assert.Equal(1,f.Count(10002915));
    }
    [SkippableFact]
    public async Task Real_marriage_capsule_uses_the_common_experience_effect_and_consumes_once()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH")),"Real PVF required.");
        using var fixture=new CeraShopFixture(useArchive:true); fixture.Host.SetLevel(30);
        await fixture.Host.Seed(69,10002910,1);
        var before=fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1");
        var result=await fixture.Host.Services.GetRequiredService<ICommandBus>().SendAsync(WholeUse.Command(fixture.Host.Services, fixture.Host.Session,new(BusinessInventorySpace.Consumable,4),10002910));
        Assert.Equal(ItemUseStatus.Used,result.Status);Assert.Equal(before+30000,fixture.Host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(0,fixture.Host.Count(10002910));
    }
}
