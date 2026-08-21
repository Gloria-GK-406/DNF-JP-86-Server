using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Characters;

/// <summary>
/// Entering a character on a database that was only ever brought into shape, and nothing else.
/// </summary>
/// <remarks>
/// Every character selection asks what a dungeon left behind. On a server that has just been
/// started for the first time there is nothing to find -- but there does have to be somewhere to
/// look, and until now nothing said so: the outbox was the one table no component declared, and
/// the recovery got away with it by asking the database whether the table was there and giving up
/// quietly when it was not.
///
/// That question was removed when the tables were split, on the understanding that the one
/// component which declares tables declares all of them. This is that understanding, written down
/// where it fails loudly rather than on the first character selection of a fresh server.
/// </remarks>
public sealed class DungeonRecoveryOnAFreshDatabaseTests
{
    [Fact]
    public async Task ACharacterEntersOnADatabaseThatHasOnlyEverBeenBroughtIntoShape()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-fresh-{Guid.NewGuid():N}.db");
        try
        {
            var side = new CharacterSide(path);
            await ExecuteAsync(path, """
                INSERT INTO characters(character_id,account_id,name,job,level)
                VALUES(70,17,'fresh',1,60);
                """);

            var outcome = await side.InUseCaseAsync(token => side
                .DungeonRecovery(new NoProgression(), new DungeonEffectRuleService())
                .RecoverAsync(new AccountId(17), new CharacterId(70), token));

            Assert.Equal(0, outcome.CommittedCount);
            Assert.Equal(0, outcome.DeadLetterCount);
            Assert.Equal(0, outcome.FailedCount);
            Assert.Equal(0, outcome.RemainingCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }
        }
    }

    /// <summary>
    /// Bringing a character into being is a change, so it is refused outside a use case.
    /// </summary>
    /// <remarks>
    /// It is the one statement here that both changes the database and answers with something --
    /// the id it gave the character. Phrased that way it could have been read rather than
    /// written, and a read is allowed to happen on a connection of its own; that would have made
    /// this the single change able to land with nothing to take it back.
    /// </remarks>
    [Fact]
    public async Task BringingACharacterIntoBeingOutsideAUseCaseIsRefused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-nolease-{Guid.NewGuid():N}.db");
        try
        {
            var side = new CharacterSide(path);

            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await side.Roster.CreateAsync(
                    new NewCharacterSpec(
                        new AccountId(17), "Alone"u8.ToArray(), Job: 3, SlotIndex: 0,
                        CharacterBaseStats.Zero),
                    CancellationToken.None));

            Assert.Contains("use case", refusal.Message, StringComparison.Ordinal);
            Assert.Equal(0, await ScalarAsync(path, "SELECT COUNT(*) FROM characters"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }
        }
    }

    private static async Task<long> ScalarAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private sealed class NoProgression : IDungeonExperienceProgressionPolicySource
    {
        public DungeonExperienceProgressionPolicy Load() => new([], 0, 0, 0);
    }

    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
