using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;
namespace Dfo.Server.Application.Tests.Characters;

public sealed class SkillSlotTests
{
    [Theory]
    [InlineData(0,150)] // remove from hotbar into the tree
    [InlineData(0,3)] // move to an empty hotbar position
    [InlineData(150,0)] // drag from tree into hotbar
    [InlineData(198,203)] // extended hotbar
    public async Task Empty_slot_move_survives_reload_without_losing_skill_or_command(int from,int to)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture(); await f.SeedAsync();
            f.Sql($"UPDATE character_skills SET slot={from} WHERE character_id=71 AND page_index=0");
            await using(var tx=f.Scope.Begin())
            {
                Assert.True(await f.Services.GetRequiredService<ICharacterSkillLearningDomainService>().SwapSlotsAsync(f.Session,0,from,to,default));
                await tx.CommitAsync(default);
            }
            await using var reload=new LearningFixture(f.Path);
            var rows=await reload.Services.GetRequiredService<ICharacterSkillTable>().ReadEntryAsync(new(71),default);
            var moved=Assert.Single(rows,r=>r.PageIndex==0);
            Assert.Equal(to,moved.Slot); Assert.Equal(1,moved.SkillId); Assert.Equal(2,moved.Level);
            Assert.Equal(15,Assert.Single(rows,r=>r.PageIndex==1).Slot);
            Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=71 AND page_index=0 AND extra_values=X'0908'"));
            Assert.Equal(7,f.Scalar("SELECT slot FROM character_skills WHERE character_id=72"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Occupied_swap_is_atomic(bool fail)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f=new LearningFixture(); await f.SeedAsync();
            f.Sql("INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) VALUES(71,'pve',0,0,2,3),(71,'pvp',0,7,1,9)");
            if(fail) f.Sql("CREATE TRIGGER fail_slot BEFORE UPDATE OF slot ON character_skills WHEN NEW.slot=0 AND OLD.slot=-1 BEGIN SELECT RAISE(ABORT,'failure'); END");
            async Task Swap()
            {
                await using var tx=f.Scope.Begin();
                Assert.True(await f.Services.GetRequiredService<ICharacterSkillLearningDomainService>().SwapSlotsAsync(f.Session,0,7,0,default));
                await tx.CommitAsync(default);
            }
            if(fail) await Assert.ThrowsAnyAsync<Exception>(Swap); else await Swap();
            Assert.Equal(fail?7:0,f.Scalar("SELECT slot FROM character_skills WHERE character_id=71 AND mode='pve' AND page_index=0 AND skill_id=1"));
            Assert.Equal(fail?0:7,f.Scalar("SELECT slot FROM character_skills WHERE character_id=71 AND mode='pve' AND page_index=0 AND skill_id=2"));
            Assert.Equal(7,f.Scalar("SELECT slot FROM character_skills WHERE character_id=71 AND mode='pvp'"));
            Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM character_skills WHERE slot=-1"));
        });
    }

    [Theory]
    [InlineData(1,7,0)]
    [InlineData(0,7,204)]
    [InlineData(0,7,-1)]
    [InlineData(0,0,1)]
    public async Task Invalid_or_inactive_request_does_not_change_learning(int page,int a,int b)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f=new LearningFixture(); await f.SeedAsync();
            await using(var tx=f.Scope.Begin())
            {
                Assert.False(await f.Services.GetRequiredService<ICharacterSkillLearningDomainService>().SwapSlotsAsync(f.Session,page,a,b,default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(7,f.Scalar("SELECT slot FROM character_skills WHERE character_id=71 AND page_index=0"));
        });
    }
}
