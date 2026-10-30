using Dfo.Server.Testing;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Schema;
using Dfo.Server.Persistence.Characters;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Characters;

public sealed class CharacterExperienceStoragePolicyTests
{
    [Fact]
    public async Task LegacyRowsNormalizeAndAnySqlWriteIsConstrainedToDerivedLevelsAndCappedExperience()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-experience-policy-{Guid.NewGuid():N}.db");
        try
        {
            var database = new SqliteDatabase(path, TestRequired.TransactionScope());
            using var link = new SqliteConnection(database.ConnectionString);
            await link.OpenAsync();
            void Sql(string text)
            {
                using var command = link.CreateCommand();
                command.CommandText = text;
                command.ExecuteNonQuery();
            }
            Sql("INSERT INTO accounts(account_id,m_id) VALUES(1,'cap'); INSERT INTO characters(character_id,account_id,name,level,exp,slot_index) VALUES(1,1,'cap',86,3347,0),(2,1,'low',50,-1,1)");
            var policy = new CharacterExperienceStoragePolicy(database);
            var scale = new CharacterExperienceScale([100,250,450,700]);
            await policy.InstallAsync(scale, default);
            var table = new SqliteCharacterTable(database);
            var capped = await table.FindProgressAsync(new(1), default);
            Assert.Equal(5, capped!.Level);
            Assert.Equal(700, capped.Experience);
            var low = await table.FindProgressAsync(new(2), default);
            Assert.Equal(1, low!.Level);
            Assert.Equal(0, low.Experience);
            Assert.Throws<SqliteException>(() => Sql("UPDATE characters SET exp=701 WHERE character_id=1"));
            Assert.Throws<SqliteException>(() => Sql("UPDATE characters SET exp=-1 WHERE character_id=1"));
            Assert.Throws<SqliteException>(() => Sql("INSERT INTO characters(account_id,name,level,exp,slot_index) VALUES(1,'bad',5,701,2)"));
            Assert.Throws<SqliteException>(() => Sql("UPDATE characters SET exp=0.5 WHERE character_id=1"));
            Sql("UPDATE characters SET level=86,exp=120 WHERE character_id=1");
            Sql("UPDATE characters SET level=86 WHERE character_id=1");
            var changed = await table.FindProgressAsync(new(1), default);
            Assert.Equal(2, changed!.Level);
            Assert.Equal(120, changed.Experience);
            Assert.Equal(2, (await table.ReadByAccountAsync(new(1), default))[0].Level);
            Assert.Equal(2, (await table.FindStandingAsync(new(1), default))!.Level);
            await policy.InstallAsync(scale, default);
            Assert.Equal(changed, await table.FindProgressAsync(new(1), default));
        }
        finally
        {
            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={path}"));
            File.Delete(path);
        }
    }
}
