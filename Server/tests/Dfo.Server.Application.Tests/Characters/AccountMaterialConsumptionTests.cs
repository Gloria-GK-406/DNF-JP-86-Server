using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Abstractions.Transactions;
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
        using var f=new ShopFixture();
        f.Sql("INSERT INTO accounts(account_id,m_id) VALUES(2,'other')");
        await f.SeedShared(3037, 3); await f.SeedShared(3035, 7); await f.SeedShared(3037, 99, 2);
        await using(var tx=((DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
        {
            var service=f.Services.GetRequiredService<IItemDiscardDomainService>();
            var result=await service.DiscardAsync(f.Session,new(BusinessInventorySpace.SharedMaterial,4),1,operation,3037,2,f.Clock.GetUtcNow(),default);
            Assert.NotNull(result.Receipt);Assert.Equal(1,result.Receipt.RemainingCore!.Value);Assert.Equal(2,result.Receipt.AppliedCount);
            // Repeated consumption observes the pending debit, not an obsolete balance.
            Assert.Null((await service.DiscardAsync(f.Session,new(BusinessInventorySpace.SharedMaterial,4),1,operation,3037,2,f.Clock.GetUtcNow(),default)).Receipt);
            if(commit) await tx.CommitAsync(default);
        }
        Assert.Equal(commit?1:3,f.SharedCount(3037));
        Assert.Equal(7,f.SharedCount(3035));
        Assert.Equal(99,f.SharedCount(3037,2));
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
        using var f=new ShopFixture();
        await f.SeedShared(3037, 3);
        await using(var tx=((DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
        {
            Assert.Null((await f.Services.GetRequiredService<IItemDiscardDomainService>()
                .DiscardAsync(f.Session,new(BusinessInventorySpace.SharedMaterial,4),entries,operation,item,count,f.Clock.GetUtcNow(),default)).Receipt);
            await tx.CommitAsync(default);
        }
        Assert.Equal(3,f.SharedCount(3037));
    }
    [Fact]
    public async Task Last_cube_reaches_zero_and_next_consumption_is_refused()
    {
        using var f=new ShopFixture();
        await f.SeedShared(3037, 1);
        await using(var tx=((DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin())
        {
            var service=f.Services.GetRequiredService<IItemDiscardDomainService>();
            Assert.Null((await service.DiscardAsync(f.Session,new(BusinessInventorySpace.SharedMaterial,4),1,1,3037,1,f.Clock.GetUtcNow(),default)).Receipt!.RemainingCore);
            Assert.Null((await service.DiscardAsync(f.Session,new(BusinessInventorySpace.SharedMaterial,4),1,1,3037,1,f.Clock.GetUtcNow(),default)).Receipt);
            await tx.CommitAsync(default);
        }
        Assert.Equal(0,f.SharedCount(3037));
    }
}
