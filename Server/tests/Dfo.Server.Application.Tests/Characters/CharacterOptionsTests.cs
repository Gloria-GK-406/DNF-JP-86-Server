using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;
namespace Dfo.Server.Application.Tests.Characters;

public sealed class CharacterOptionsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Opaque_settings_preserve_all_bytes_and_flags_with_transactional_isolation(bool commit)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f=new LearningFixture();await f.SeedAsync();
            f.Sql("INSERT INTO character_settings(character_id,user_state_bits) VALUES(71,7),(72,9)");
            var blob=Enumerable.Range(0,516).Select(i=>(byte)(i%256)).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(blob,512);
            await using(var tx=f.Scope.Begin())
            {
                Assert.True(await f.Services.GetRequiredService<ICharacterOptionsDomainService>().SaveAsync(f.Session,blob,default));
                if(commit) await tx.CommitAsync(default);
            }
            await using var reload=new LearningFixture(f.Path);
            var table=reload.Services.GetRequiredService<ICharacterSettingsTable>();
            var row=await table.FindAsync(new(71),default);
            if(commit) Assert.Equal(blob,row!.OptionBlob); else Assert.Null(row!.OptionBlob);
            Assert.Equal(7,row.UserStateBits);
            Assert.Null((await table.FindAsync(new(72),default))!.OptionBlob);
        });
    }
    [Fact]
    public async Task Missing_row_is_created_and_malformed_or_foreign_session_cannot_replace_it()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f=new LearningFixture();await f.SeedAsync();
            var blob=new byte[516];BinaryPrimitives.WriteUInt32LittleEndian(blob,512);blob[100]=123;
            await using(var tx=f.Scope.Begin())
            {
                var service=f.Services.GetRequiredService<ICharacterOptionsDomainService>();
                Assert.True(await service.SaveAsync(f.Session,blob,default));
                Assert.False(await service.SaveAsync(f.Session,new byte[516],default));
                Assert.False(await service.SaveAsync(f.Session,new byte[4],default));
                var stale=Dfo.Server.Data.World.SelectedSessionIdentity.Create(f.Session.AccountId,new(72),f.Session.ConnectionId,99,10011);
                Assert.False(await service.SaveAsync(stale,blob,default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(blob,(await f.Services.GetRequiredService<ICharacterSettingsTable>().FindAsync(new(71),default))!.OptionBlob);
        });
    }
}
