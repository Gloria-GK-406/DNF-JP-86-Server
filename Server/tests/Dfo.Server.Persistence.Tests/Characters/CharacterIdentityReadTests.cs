using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Characters;

/// <summary>
/// The statement that answers what a character is, read back column by column.
/// </summary>
/// <remarks>
/// Which column a reader pulls a value out of is a number written next to the SELECT, and getting
/// it wrong is silent: the read succeeds and hands back the neighbouring column. The name on this
/// table sits at a different position in every statement that carries it -- second in the roster
/// reads, first here -- and the shared reader that pulls it had that position written into it as a
/// constant until this read arrived.
///
/// So the row below is written with values that cannot stand in for one another: the slot, the job
/// and the grow type are three different numbers, none of them the level or the character id, and
/// the name is bytes no column of numbers could be mistaken for. Any one column landing in another
/// field moves at least one assertion, and pulling the name from a numeric column does not even
/// return.
/// </remarks>
public sealed class CharacterIdentityReadTests
{
    private static readonly AccountId Account = new(1);

    /// <summary>Bytes, so that reading a number into this field cannot come back looking right.</summary>
    private static readonly byte[] Name = "Nagisa"u8.ToArray();

    [Fact]
    public async Task EveryColumnOfTheIdentityReadLandsInItsOwnField()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "characters.db");
        try
        {
            var side = new CharacterSide(databasePath);
            var id = await side.PerCall.CreateAsync(
                new NewCharacterSpec(Account, Name, Job: 4, SlotIndex: 3, CharacterBaseStats.Zero),
                CancellationToken.None);

            // Creation stamps a grow type of zero, which is also what a field nothing was read
            // into would hold. Moved to a third number so that all four assertions discriminate.
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync(CancellationToken.None);
                await using var update = connection.CreateCommand();
                update.CommandText =
                    "UPDATE characters SET grow_type = 7 WHERE character_id = @id;";
                update.Parameters.AddWithValue("@id", id.Value);
                Assert.Equal(1, await update.ExecuteNonQueryAsync(CancellationToken.None));
            }

            var identity = await side.Characters.FindIdentityAsync(id, CancellationToken.None);

            Assert.NotNull(identity);
            Assert.Equal(3, identity.SlotIndex);
            Assert.Equal(Name, identity.Name);
            Assert.Equal(4, identity.Job);
            Assert.Equal(7, identity.GrowType);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
