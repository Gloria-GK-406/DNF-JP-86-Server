using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Characters;

public sealed class AccountMaterialConsumptionTests
{
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    public async Task Skill_material_consumption_is_transactional_and_account_isolated(bool commit, ushort operation)
    {
        await using var f=new LearningFixture();await f.SeedAsync();
        f.Sql("UPDATE accounts SET cube_clear=3,cube_red=7 WHERE account_id=1; UPDATE accounts SET cube_clear=99 WHERE account_id=2");
        await using(var tx=f.Scope.Begin())
        {
            var service=f.Services.GetRequiredService<IItemDiscardDomainService>();
            var result=await service.DiscardMaterialAsync(f.Session,AccountMaterialKind.ClearCube,3037,2,1,operation,default);
            Assert.NotNull(result);Assert.Equal(1,result.FinalCount);Assert.Equal(-2,result.AddedCount);
            // Repeated consumption observes the pending debit, not an obsolete balance.
            Assert.Null(await service.DiscardMaterialAsync(f.Session,AccountMaterialKind.ClearCube,3037,2,1,operation,default));
            if(commit) await tx.CommitAsync(default);
        }
        Assert.Equal(commit?1:3,f.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
        Assert.Equal(7,f.Scalar("SELECT cube_red FROM accounts WHERE account_id=1"));
        Assert.Equal(99,f.Scalar("SELECT cube_clear FROM accounts WHERE account_id=2"));
    }
    [Theory]
    [InlineData(3037,4,1,1)]
    [InlineData(3037,0,1,1)]
    [InlineData(3037,-1,1,1)]
    [InlineData(3036,1,1,1)]
    [InlineData(3037,1,2,1)]
    [InlineData(3037,1,1,0)]
    [InlineData(3037,1,1,3)]
    [InlineData(3036,1,1,2)]
    public async Task Invalid_or_insufficient_request_does_not_debit(int item,int count,byte entries,ushort operation)
    {
        await using var f=new LearningFixture();await f.SeedAsync();
        f.Sql("UPDATE accounts SET cube_clear=3 WHERE account_id=1");
        await using(var tx=f.Scope.Begin())
        {
            Assert.Null(await f.Services.GetRequiredService<IItemDiscardDomainService>()
                .DiscardMaterialAsync(f.Session,AccountMaterialKind.ClearCube,item,count,entries,operation,default));
            await tx.CommitAsync(default);
        }
        Assert.Equal(3,f.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
    }
    [Fact]
    public async Task Last_cube_reaches_zero_and_next_consumption_is_refused()
    {
        await using var f=new LearningFixture();await f.SeedAsync();
        f.Sql("UPDATE accounts SET cube_clear=1 WHERE account_id=1");
        await using(var tx=f.Scope.Begin())
        {
            var service=f.Services.GetRequiredService<IItemDiscardDomainService>();
            Assert.Equal(0,(await service.DiscardMaterialAsync(f.Session,AccountMaterialKind.ClearCube,3037,1,1,1,default))!.FinalCount);
            Assert.Null(await service.DiscardMaterialAsync(f.Session,AccountMaterialKind.ClearCube,3037,1,1,1,default));
            await tx.CommitAsync(default);
        }
        Assert.Equal(0,f.Scalar("SELECT cube_clear FROM accounts WHERE account_id=1"));
    }
}
