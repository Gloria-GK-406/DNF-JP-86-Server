using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Characters;

/// <summary>
/// What a character's skills are stored as, and what the storage refuses.
/// </summary>
/// <remarks>
/// PvE skills and PvP skills were two tables with the same columns under different names, and only
/// one of them declared anything: the PvP table carried <c>NOT NULL</c> on its five non-BLOB
/// columns, a page range, a cascading foreign key and a per-page uniqueness, while the PvE table
/// carried none of the four. Merging them means every one of those has to arrive on the one table
/// that is left, and none of them has ever had a test.
///
/// The four are written down here rather than only in the declaration, because a constraint that
/// vanishes refuses nothing and reports nothing.
/// </remarks>
public sealed class SkillCarrierTests
{
    private static readonly AccountId Account = new(1);

    /// <summary>
    /// A character created holding skills is stored holding them, one row each.
    /// </summary>
    /// <remarks>
    /// This is the only path that writes a PvE skill row, and it is written down because the merge
    /// puts a uniqueness on page zero that this path has never had to satisfy. What makes it safe
    /// is a fact about the archive rather than about this code -- no job declares the same active
    /// twice -- and that fact is pinned in
    /// <c>PvfInitialSkillDefinitionSourceTests.No_job_declares_the_same_active_skill_twice</c>.
    /// </remarks>
    [Fact]
    public async Task ACharacterCreatedHoldingSkillsKeepsOneRowEach()
    {
        await WithAFreshDatabaseAsync(async path =>
        {
            var created = await new AccountSide(path).GetOrCreateAsync("10038", "hash");
            Assert.Equal(Account, created.AccountId);

            var characterId = await new CharacterSide(path).PerCall.CreateAsync(
                new NewCharacterSpec(
                    Account,
                    "Nagisa"u8.ToArray(),
                    Job: 3,
                    SlotIndex: 0,
                    CharacterBaseStats.Zero,
                    [
                        new NewCharacterSkill(0, 5, 1),
                        new NewCharacterSkill(1, 46, 1),
                        new NewCharacterSkill(2, 169, 1),
                    ]),
                CancellationToken.None);

            Assert.Equal(
                [(0, 5, 1), (1, 46, 1), (2, 169, 1)],
                await SkillsAsync(path, characterId, "pve"));
            Assert.Empty(await SkillsAsync(path, characterId, "pvp"));
        });
    }

    /// <summary>
    /// Five columns that may not be empty. The PvP table has always said so; the PvE one never did.
    /// </summary>
    [Theory]
    [InlineData("character_id")]
    [InlineData("mode")]
    [InlineData("page_index")]
    [InlineData("slot")]
    [InlineData("skill_id")]
    [InlineData("level")]
    public async Task ASkillRowMissingOneOfItsRequiredValuesIsRefused(string column)
    {
        await WithACharacterAsync(async path =>
        {
            var columns = new[]
            {
                "character_id", "mode", "page_index", "slot", "skill_id", "level",
            };
            var values = new[] { "1", "'pve'", "0", "0", "5", "1" };
            values[Array.IndexOf(columns, column)] = "NULL";

            await Assert.ThrowsAsync<SqliteException>(async () => await ExecuteAsync(path,
                $"INSERT INTO character_skills({string.Join(',', columns)}) "
                + $"VALUES({string.Join(',', values)})"));
        });
    }

    /// <summary>
    /// A duel page is one of two. An ordinary page is not, and the check has to say which it means.
    /// </summary>
    /// <remarks>
    /// The range came from the PvP table, where every row was a duel row. On the merged table it
    /// has to be qualified: a PvE page index of two is ordinary and must still be accepted, and
    /// carrying the check across unqualified would refuse it.
    /// </remarks>
    [Fact]
    public async Task ADuelPageBeyondTheSecondIsRefusedAndAnOrdinaryOneIsNot()
    {
        await WithACharacterAsync(async path =>
        {
            await Assert.ThrowsAsync<SqliteException>(async () => await ExecuteAsync(path,
                "INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) "
                + "VALUES(1,'pvp',2,0,5,1)"));

            await ExecuteAsync(path,
                "INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) "
                + "VALUES(1,'pve',2,0,5,1)");

            Assert.Equal(1L, await ScalarAsync(
                path, "SELECT COUNT(*) FROM character_skills WHERE page_index=2"));
        });
    }

    /// <summary>Only two words are a mode, and neither of them reinterprets anything.</summary>
    [Fact]
    public async Task AThirdModeIsRefused()
    {
        await WithACharacterAsync(async path =>
            await Assert.ThrowsAsync<SqliteException>(async () => await ExecuteAsync(path,
                "INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) "
                + "VALUES(1,'duel',0,0,5,1)")));
    }

    /// <summary>
    /// One skill appears once on a page -- of its own mode.
    /// </summary>
    /// <remarks>
    /// The uniqueness came from the PvP table and is qualified by mode here, which is what makes
    /// the merge lossless in both directions: the same skill on the same page of the other mode is
    /// a different row and stays permitted.
    /// </remarks>
    [Fact]
    public async Task TheSameSkillTwiceOnOnePageIsRefusedAndTheOtherModesPageIsNot()
    {
        await WithACharacterAsync(async path =>
        {
            await ExecuteAsync(path,
                "INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) "
                + "VALUES(1,'pve',0,0,5,1)");

            await Assert.ThrowsAsync<SqliteException>(async () => await ExecuteAsync(path,
                "INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) "
                + "VALUES(1,'pve',0,1,5,1)"));

            await ExecuteAsync(path,
                "INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) "
                + "VALUES(1,'pvp',0,0,5,1)");

            Assert.Equal(2L, await ScalarAsync(path, "SELECT COUNT(*) FROM character_skills"));
        });
    }

    /// <summary>
    /// Two slots of one page are two rows; the same slot twice is one.
    /// </summary>
    [Fact]
    public async Task TheSameSlotTwiceOnOnePageIsRefused()
    {
        await WithACharacterAsync(async path =>
        {
            await ExecuteAsync(path,
                "INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) "
                + "VALUES(1,'pve',0,0,5,1)");

            await Assert.ThrowsAsync<SqliteException>(async () => await ExecuteAsync(path,
                "INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) "
                + "VALUES(1,'pve',0,0,46,1)"));
        });
    }

    /// <summary>
    /// A character taken away takes both sets of its skills with it.
    /// </summary>
    /// <remarks>
    /// The cascade is declared rather than switched on: SQLite enforces a foreign key only when the
    /// connection asks it to, and nothing in the server asks. So this says what the declaration
    /// means rather than what happens today, and it asks the pragma itself -- which is the only way
    /// to tell a declared cascade from a missing one. The declaration is the thing the merge could
    /// silently drop.
    /// </remarks>
    [Fact]
    public async Task ACharacterTakenAwayTakesBothSetsOfItsSkillsWithIt()
    {
        await WithACharacterAsync(async path =>
        {
            await ExecuteAsync(path,
                "INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) "
                + "VALUES(1,'pve',0,0,5,1),(1,'pvp',0,0,5,1)");

            await ExecuteAsync(path,
                "PRAGMA foreign_keys=ON; DELETE FROM characters WHERE character_id=1;");

            Assert.Equal(0L, await ScalarAsync(path, "SELECT COUNT(*) FROM character_skills"));
        });
    }

    /// <summary>The two modes are two sets of rows, and neither read sees the other.</summary>
    [Fact]
    public async Task TheTwoModesDoNotSeeEachOther()
    {
        await WithACharacterAsync(async path =>
        {
            await ExecuteAsync(path,
                "INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) "
                + "VALUES(1,'pve',0,0,5,1),(1,'pve',0,1,46,2),(1,'pvp',0,0,169,3)");

            Assert.Equal(2L, await ScalarAsync(
                path, "SELECT COUNT(*) FROM character_skills WHERE mode='pve'"));
            Assert.Equal(1L, await ScalarAsync(
                path, "SELECT COUNT(*) FROM character_skills WHERE mode='pvp'"));
            Assert.Equal(169L, await ScalarAsync(
                path, "SELECT skill_id FROM character_skills WHERE mode='pvp'"));
        });
    }

    /// <summary>
    /// Seeding the duel pages puts them on the duel side, once, and says so.
    /// </summary>
    /// <remarks>
    /// Three things meet here and none of them had a test. The rows a seeding writes must land as
    /// duel rows -- the mode is a literal in one statement now, and a literal is not checked by
    /// anything. The marker must be set, or every entry would seed again. And a second seeding must
    /// do nothing, which is what the marker is for: the profile handed to it the second time is a
    /// different one, and if it were written the character would enter the arena with skills it
    /// never had.
    ///
    /// The profile is deliberately not empty. An empty one writes no row, which is why the tests
    /// that used one could not have caught any of this.
    /// </remarks>
    [Fact]
    public async Task SeedingTheDuelPagesWritesThemOnceOnTheDuelSide()
    {
        await WithACharacterAsync(async path =>
        {
            var side = new CharacterSide(path);

            var seeded = await side.InUseCaseAsync(token => side.PvpProfile.LoadOrInitializeAsync(
                Account, new CharacterId(1), Profile(700, 800), token));

            Assert.Equal([700], seeded.Pages[0].Skills.Select(skill => (int)skill.SkillId));
            Assert.Equal([800], seeded.Pages[1].Skills.Select(skill => (int)skill.SkillId));
            Assert.Equal(2L, await ScalarAsync(
                path, "SELECT COUNT(*) FROM character_skills WHERE mode='pvp'"));
            Assert.Equal(0L, await ScalarAsync(
                path, "SELECT COUNT(*) FROM character_skills WHERE mode='pve'"));
            Assert.Equal(1L, await ScalarAsync(
                path, "SELECT skills_initialized FROM character_pvp WHERE character_id=1"));

            // A second seeding is refused by the marker rather than written and deduplicated.
            var again = await side.InUseCaseAsync(token => side.PvpProfile.LoadOrInitializeAsync(
                Account, new CharacterId(1), Profile(701, 801), token));

            Assert.Equal([700], again.Pages[0].Skills.Select(skill => (int)skill.SkillId));
            Assert.Equal([800], again.Pages[1].Skills.Select(skill => (int)skill.SkillId));
            Assert.Equal(2L, await ScalarAsync(
                path, "SELECT COUNT(*) FROM character_skills WHERE mode='pvp'"));
        });
    }

    /// <summary>One skill on each duel page, so that a write has something to get wrong.</summary>
    private static CharacterSkillsEntryState Profile(uint first, uint second) => new(
        [
            new CharacterSkillPageState(0, [new CharacterSkillState(3, first, 4, [])]),
            new CharacterSkillPageState(1, [new CharacterSkillState(5, second, 6, [])]),
        ],
        [],
        new CharacterSkillPointBasisState(0, 0),
        [],
        null);

    /// <summary>
    /// Each read answers with its own mode's rows and none of the other's.
    /// </summary>
    /// <remarks>
    /// The point of the merge and the thing that could quietly undo it. Both reads run against one
    /// table now, and a read that lost its <c>mode</c> condition would answer with both sets: the
    /// entry load would hand the client duel skills on its ordinary pages, and the duel profile
    /// would hand the arena the character's ordinary ones. Neither would fail, and no other test on
    /// this path holds a character with both.
    /// </remarks>
    [Fact]
    public async Task EachReadAnswersWithItsOwnModeAndNoneOfTheOther()
    {
        await WithACharacterAsync(async path =>
        {
            await ExecuteAsync(path,
                "INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) "
                + "VALUES(1,'pve',0,3,100,4),(1,'pvp',0,7,200,5),(1,'pvp',1,9,300,6)");
            var side = new CharacterSide(path);

            var entry = await side.EntryState(new GoldLimitRuleService())
                .LoadAsync(Account, new CharacterId(1), CancellationToken.None);
            var duel = await side.InUseCaseAsync(token => side.PvpProfile.LoadOrInitializeAsync(
                Account,
                new CharacterId(1),
                new CharacterSkillsEntryState(
                    [new CharacterSkillPageState(0, []), new CharacterSkillPageState(1, [])],
                    [],
                    new CharacterSkillPointBasisState(0, 0),
                    [],
                    null),
                token));

            Assert.Equal([100], entry.Skill.Skills.Select(row => row.SkillId));
            Assert.Equal([200], duel.Pages[0].Skills.Select(skill => (int)skill.SkillId));
            Assert.Equal([300], duel.Pages[1].Skills.Select(skill => (int)skill.SkillId));
        });
    }

    /// <summary>
    /// Reading, levelling and removing an ordinary skill reach the ordinary rows only.
    /// </summary>
    /// <remarks>
    /// The three statements that are neither of the two reads already pinned. Each names its mode
    /// in one string, and a duel row at the same page and slot is now a row of the same table --
    /// so a statement that lost its mode would read it, raise it, or delete it, and the arena would
    /// find its pages changed by something that was never about them. The seed puts the two rows on
    /// the same page and slot deliberately, because that is the only arrangement where losing the
    /// condition shows.
    /// </remarks>
    [Fact]
    public async Task LevellingAnOrdinarySkillLeavesTheDuelRowAtTheSamePlaceAlone()
    {
        await WithACharacterAsync(async path =>
        {
            await ExecuteAsync(path,
                "INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) "
                + "VALUES(1,'pve',0,1,100,4),(1,'pvp',0,1,200,7)");
            var side = new CharacterSide(path);

            var levels = await side.InUseCaseAsync(
                token => side.Skills.ReadLevelsAsync(new CharacterId(1), token));
            Assert.Equal([100], levels.Select(row => row.SkillId));

            Assert.Equal(1, await side.InUseCaseAsync(
                token => side.Skills.SetLevelAsync(new CharacterId(1), 0, 1, 9, token)));
            Assert.Equal(7L, await ScalarAsync(
                path, "SELECT level FROM character_skills WHERE mode='pvp'"));

            Assert.Equal(1, await side.InUseCaseAsync(
                token => side.Skills.RemoveAsync(new CharacterId(1), 0, 1, token)));
            Assert.Equal(1L, await ScalarAsync(
                path, "SELECT COUNT(*) FROM character_skills WHERE mode='pvp'"));
            Assert.Equal(0L, await ScalarAsync(
                path, "SELECT COUNT(*) FROM character_skills WHERE mode='pve'"));
        });
    }

    /// <summary>
    /// A job-specific page held by another job is refused, not ignored.
    /// </summary>
    /// <remarks>
    /// The two job-specific pages stay two tables, because merging them needs a column whose value
    /// decides what the payload column means -- a page body against a shield's item id -- and that
    /// is the shape the red line forbids. Which means the thing worth pinning about them is not
    /// their shape but their refusal, and until now nothing pinned it: a character of the wrong job
    /// holding either one is a database that cannot be entered, and a load that quietly dropped the
    /// rows instead would look identical from every test on this path.
    /// </remarks>
    [Theory]
    [InlineData(9, "INSERT INTO character_dark_knight_combo_skill_pages VALUES(1,0,X'0A0B');")]
    [InlineData(12, "INSERT INTO character_knight_shield_deck VALUES(1,0,666);")]
    public async Task AJobSpecificPageHeldByAnotherJobIsRefused(byte owningJob, string seed)
    {
        await WithAFreshDatabaseAsync(async path =>
        {
            var created = await new AccountSide(path).GetOrCreateAsync("10038", "hash");
            Assert.Equal(Account, created.AccountId);
            var side = new CharacterSide(path);

            // Any job but the one the page belongs to.
            var characterId = await side.PerCall.CreateAsync(
                new NewCharacterSpec(
                    Account, "Nagisa"u8.ToArray(), Job: (byte)(owningJob == 9 ? 12 : 9),
                    SlotIndex: 0, CharacterBaseStats.Zero),
                CancellationToken.None);
            Assert.Equal(1, characterId.Value);
            await ExecuteAsync(path, seed);

            var refusal = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await side.EntryState(new GoldLimitRuleService())
                    .LoadAsync(Account, characterId, CancellationToken.None));
            Assert.Contains("inapplicable", refusal.Message, StringComparison.Ordinal);
        });
    }

    private static async Task WithACharacterAsync(Func<string, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        await WithAFreshDatabaseAsync(async path =>
        {
            var created = await new AccountSide(path).GetOrCreateAsync("10038", "hash");
            Assert.Equal(Account, created.AccountId);
            var characterId = await new CharacterSide(path).PerCall.CreateAsync(
                new NewCharacterSpec(
                    Account, "Nagisa"u8.ToArray(), Job: 3, SlotIndex: 0, CharacterBaseStats.Zero),
                CancellationToken.None);
            Assert.Equal(1, characterId.Value);

            await body(path);
        });
    }

    private static async Task ExecuteAsync(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(string path, string statement)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = statement;

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    /// <remarks>
    /// The mode is named because that is the whole of what a write chooses. A read-back that left it
    /// out would answer the same whichever mode the row went into, and the literal in the insert is
    /// the only thing that decides.
    /// </remarks>
    private static async Task<IReadOnlyList<(int Slot, int SkillId, int Level)>> SkillsAsync(
        string path, CharacterId characterId, string mode)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT slot, skill_id, level FROM character_skills "
            + $"WHERE character_id={characterId.Value} AND mode='{mode}' AND page_index=0 "
            + "ORDER BY slot";
        var rows = new List<(int, int, int)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)));
        }

        return rows;
    }

    private static async Task WithAFreshDatabaseAsync(Func<string, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-skills-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await body(Path.Combine(directory, "characters.db"));
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
}
