using Dfo.Server.Testing;
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
        var runs=f.Services.DungeonRuns();
        var cell=new Dfo.Server.Abstractions.Content.MazeCell(0,0);
        var layout=new Dfo.Server.Abstractions.World.DungeonLayout(3541,0,1,1,cell,cell,1,1,
            new Dictionary<Dfo.Server.Abstractions.Content.MazeCell,Dfo.Server.Abstractions.Content.DungeonCellDefinition> { [cell]=Dfo.Server.Abstractions.Content.DungeonCellDefinition.Fixed(1) });
        var run=f.InTransaction(()=>runs.PreparePartyEntry([new(f.Session.AccountId,f.Session.CharacterId,0,true)],layout,0,
            new(cell,1,1,[],[new(7,10002915,1,0,false,Dfo.Server.Abstractions.World.DungeonGroundItemSource.FromActor(1))]),
            Dfo.Server.Abstractions.World.DungeonRunId.New(),DateTimeOffset.UtcNow)).Run;
        async ValueTask<Dfo.Server.Protocol.Game.GroundPickup.GroundPickupResult> PickUp(ushort drop) => await Dfo.Server.Testing.TestGameplay.RunAsync(f.Services,async()=>
        {
            await using var scope=((Dfo.Server.Application.Transactions.DomainTransactionScope)f.Services.GetRequiredService<Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>()).Begin();
            var result=await ActivatorUtilities.CreateInstance<Dfo.Server.Application.Tests.Support.GroundPickupAlgorithmDriver>(f.Services).PickUpAsync(new(f.Session.AccountId,f.Session.CharacterId,drop,run.RunId,run.RoomVersion),default);
            await scope.CommitAsync(default);return result;
        });
        var number=Assert.Single(run.CurrentCellState.GroundItems).Number;
        var result=await PickUp(number);
        Assert.Equal(Dfo.Server.Abstractions.World.GroundPickupStatus.PickedUp,result.Status);
        Assert.Equal(1,f.Count(10002915));Assert.Null(runs.Find(f.Session.CharacterId)!.CurrentCellState.FindGroundItem(number));
        Assert.NotEqual(Dfo.Server.Abstractions.World.GroundPickupStatus.PickedUp,(await PickUp(number)).Status);
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
