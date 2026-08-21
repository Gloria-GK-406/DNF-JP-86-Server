using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Characters;

/// <summary>
/// What a character is born with, on the six tables that hold one row each for as long as it lives.
/// </summary>
/// <remarks>
/// Cutting the character's own tables by concept turned three rows into six, and the values a
/// creation write does not name into defaults on tables it had never written before. Neither is
/// visible to the tests that read a character back, because those seed what they read. The gap is
/// real: a reader that answered with an empty array where the old one answered with nothing at all
/// went the whole way through this change unnoticed, and would have taken four bytes off every
/// character's option frame.
/// </remarks>
public sealed class CharacterBodyCreationTests
{
    private static readonly AccountId Account = new(1);

    /// <summary>The values a creation write never names, which therefore come from the schema.</summary>
    /// <remarks>
    /// Zero is what almost every column defaults to, so the ones that do not are the ones worth
    /// pinning: each of these is a value the server would send differently if the default were
    /// carried across as zero.
    /// </remarks>
    public static TheoryData<string, string, long> Defaults() => new()
    {
        { "characters", "skill_tree_index", -1 },
        { "characters", "direction", 5 },
        { "characters", "area_state", 3 },
        { "characters", "level", 1 },
        { "character_settings", "user_state_bits", 3 },
        { "character_condition", "return_user_flag", 1 },
        { "character_condition", "pc_room_id", 65537 },
        { "character_condition", "crystal_cube_grade", 255 },
    };

    [Theory]
    [MemberData(nameof(Defaults))]
    public async Task AColumnTheCreationWriteDoesNotNameKeepsItsDeclaredValue(
        string table, string column, long expected)
    {
        await WithACreatedCharacterAsync(async (path, id) =>
        {
            Assert.Equal(expected, await ScalarAsync(
                path, $"SELECT {column} FROM {table} WHERE character_id={id.Value}"));
        });
    }

    [Theory]
    [InlineData("characters")]
    [InlineData("character_stats")]
    [InlineData("character_condition")]
    [InlineData("character_pvp")]
    [InlineData("character_settings")]
    [InlineData("character_expert_job")]
    public async Task ACreatedCharacterHasItsRowInEveryTableThatHoldsOne(string table)
    {
        await WithACreatedCharacterAsync(async (path, id) =>
        {
            Assert.Equal(1L, await ScalarAsync(
                path, $"SELECT COUNT(*) FROM {table} WHERE character_id={id.Value}"));
        });
    }

    /// <remarks>
    /// The option block is the one column here where absent and empty are different things: absent
    /// sends four zero bytes and empty sends none. A reader that turns a NULL into an empty array
    /// loses that distinction, and nothing downstream notices -- the frame is simply four bytes
    /// shorter for every character ever created.
    /// </remarks>
    [Fact]
    public async Task ACreatedCharacterHasNoOptionBlockRatherThanAnEmptyOne()
    {
        await WithACreatedCharacterAsync(async (path, id) =>
        {
            var side = new CharacterSide(path);
            var record = await side.EntryState(new GoldLimitRuleService())
                .LoadAsync(Account, id, CancellationToken.None);

            Assert.Null(record.Profile.CharacterOptionBlob);
        });
    }

    private static async Task WithACreatedCharacterAsync(Func<string, CharacterId, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-born-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "characters.db");
        try
        {
            var created = await new AccountSide(path).GetOrCreateAsync("10038", "hash");
            Assert.Equal(Account, created.AccountId);

            var id = await new CharacterSide(path).PerCall.CreateAsync(
                new NewCharacterSpec(
                    Account, "Nagisa"u8.ToArray(), Job: 3, SlotIndex: 0, CharacterBaseStats.Zero),
                CancellationToken.None);

            await body(path, id);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }
        }
    }

    private static async Task<long> ScalarAsync(string path, string statement)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = statement;

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
