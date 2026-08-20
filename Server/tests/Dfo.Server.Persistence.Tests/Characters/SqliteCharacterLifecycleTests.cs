using System.Text;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Characters;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Characters;

/// <summary>Persistence for character creation, name lookup and soft deletion.</summary>
public sealed class SqliteCharacterLifecycleTests
{
    private static readonly AccountId Account = new(1);

    private static async Task WithRepositoryAsync(Func<SqliteCharacterRepository, Task> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await body(new SqliteCharacterRepository(Path.Combine(directory, "characters.db")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public Task ACreatedCharacterCanBeEntered()
    {
        // The entry profile query inner-joins three tables. Creation used to write only the
        // characters row, so every character it produced was rejected at select-character with
        // "incomplete required profile state" -- a corruption assertion firing on a character the
        // server had just built itself.
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "characters.db");
        return RunAndCleanUpAsync(directory, async () =>
        {
            // The account loader below reads a real account row, so create one first. Its id is
            // the autoincrement seed, which is the Account this test uses.
            var accounts = new SqliteAccountRepository(databasePath);
            var created = await accounts.GetOrCreateAsync("10038", "hash", CancellationToken.None);
            Assert.Equal(Account, created.AccountId);

            var repository = new SqliteCharacterRepository(databasePath);
            var id = await repository.CreateAsync(
                new NewCharacterSpec(Account, "Nagisa"u8.ToArray(), Job: 3, SlotIndex: 0, CharacterBaseStats.Zero),
                CancellationToken.None);

            var entryState = new SqliteCharacterEntryStateRepository(databasePath, new GoldLimitRuleService());
            var record = await entryState.LoadAsync(Account, id, CancellationToken.None);

            Assert.Equal(id, record.CharacterId);
            Assert.Equal(3, record.Profile.Job);
            Assert.Equal(1, record.Profile.Level);

            // Entering also loads account-scoped state, and that side reads this character's
            // rows too. Asserting only the character loader is what let a null pc_room_state
            // through: the character profile never reads that column, while the account loader
            // left-joins it for every live character and refuses a null.
            var accountState = new SqliteAccountEntryStateRepository(databasePath);
            var account = await accountState.LoadAsync(Account, CancellationToken.None);

            Assert.Equal(Account, account.AccountId);
        });
    }

    [Fact]
    public Task ACreatedCharacterKeepsTheStatsItWasBornWith()
    {
        // Creation used to leave every stat at its schema default, so a character entered the
        // world with zero maximum HP. The values below are the swordman's, scaled the way the
        // archive's numbers are, and they travel back out through the same loader select-character
        // uses rather than through a hand-written query.
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "characters.db");
        return RunAndCleanUpAsync(directory, async () =>
        {
            var accounts = new SqliteAccountRepository(databasePath);
            await accounts.GetOrCreateAsync("10038", "hash", CancellationToken.None);

            var stats = new CharacterBaseStats(
                HpMax: 1800, MpMax: 1400,
                PhysicalAttack: 75, PhysicalDefense: 75,
                MagicalAttack: 45, MagicalDefense: 45,
                DarkResistance: 200, LightResistance: -200,
                InventoryLimit: 480000, MpRegenSpeed: 500,
                MoveSpeed: 8500, AttackSpeed: 8500, CastSpeed: 7000,
                HitRecovery: 6000, JumpPower: 4300, Weight: 500000);
            var repository = new SqliteCharacterRepository(databasePath);
            var id = await repository.CreateAsync(
                new NewCharacterSpec(Account, "Nagisa"u8.ToArray(), Job: 0, SlotIndex: 0, stats),
                CancellationToken.None);

            var stored = (await new SqliteCharacterEntryStateRepository(databasePath, new GoldLimitRuleService())
                .LoadAsync(Account, id, CancellationToken.None)).Profile.Subtype1;

            Assert.Equal(stats.HpMax, stored.HpMaximum);
            Assert.Equal(stats.MpMax, stored.MpMaximum);
            Assert.Equal(stats.PhysicalAttack, stored.PhysicalAttack);
            Assert.Equal(stats.MagicalDefense, stored.MagicalDefense);
            Assert.Equal(stats.InventoryLimit, stored.InventoryLimit);
            Assert.Equal(stats.MoveSpeed, stored.MoveSpeed);
            Assert.Equal(stats.Weight, stored.Weight);

            // Signed and absent stats are the two that a column-order slip would silently pass:
            // the swordman's light resistance is negative and he declares no fire resistance.
            Assert.Equal(-200, stored.LightResistance);
            Assert.Equal(200, stored.DarkResistance);
            Assert.Equal(0, stored.FireResistance);

            // Born at full health. The column's schema default is zero, and zero here is worse
            // than a missing stat: the client draws an empty bar under an intact maximum, which
            // is how a character with correct maxima still showed no HP at all.
            Assert.Equal(100, stored.CurrentHpMpPercent);
        });
    }

    [Fact]
    public Task CreationLeavesNoCharacterBehindWhenItCannotComplete()
    {
        // Atomicity is the whole reason the entry-state rows are written here rather than by a
        // second call from the service layer: a character row that outlived a failed follow-up
        // would be exactly the half-built state the assertion exists to catch.
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "characters.db");
        return RunAndCleanUpAsync(directory, async () =>
        {
            var repository = new SqliteCharacterRepository(databasePath);
            await using (var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
            {
                await connection.OpenAsync(CancellationToken.None);
                await using var drop = connection.CreateCommand();
                drop.CommandText = "DROP TABLE character_init_flags;";
                await drop.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await Assert.ThrowsAnyAsync<Exception>(async () => await repository.CreateAsync(
                new NewCharacterSpec(Account, "Doomed"u8.ToArray(), Job: 1, SlotIndex: 0, CharacterBaseStats.Zero),
                CancellationToken.None));

            Assert.Empty(await repository.ListByAccountAsync(Account, CancellationToken.None));
        });
    }

    private static async Task RunAndCleanUpAsync(string directory, Func<Task> body)
    {
        try
        {
            await body();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public Task ACreatedCharacterComesBackOnTheRoster() => WithRepositoryAsync(async repository =>
    {
        var id = await repository.CreateAsync(
            new NewCharacterSpec(Account, "Nagisa"u8.ToArray(), Job: 3, SlotIndex: 0, CharacterBaseStats.Zero),
            CancellationToken.None);

        var roster = await repository.ListByAccountAsync(Account, CancellationToken.None);

        var only = Assert.Single(roster);
        Assert.Equal(id, only.CharacterId);
        Assert.Equal("Nagisa"u8.ToArray(), only.Name);
        Assert.Equal(3, only.Job);
        Assert.Equal(1, only.Level);
        Assert.Equal(0, only.SlotIndex);
    });

    [Fact]
    public Task NameLookupMatchesOnBytesRatherThanDecodedText() =>
        WithRepositoryAsync(async repository =>
        {
            var name = Encoding.UTF8.GetBytes("さくら");
            await repository.CreateAsync(
                new NewCharacterSpec(Account, name, 0, 0, CharacterBaseStats.Zero),
                CancellationToken.None);

            Assert.True(await repository.NameExistsAsync(name, CancellationToken.None));
            Assert.False(await repository.NameExistsAsync(
                Encoding.UTF8.GetBytes("さくらこ"),
                CancellationToken.None));
        });

    [Fact]
    public Task ANameThatIsNotValidUtf8SurvivesTheRoundTripIntact() =>
        WithRepositoryAsync(async repository =>
        {
            // The display column cannot hold these bytes, so the blob column is the authority.
            // If the read path ever fell back to the text column this would come back mangled.
            byte[] undecodable = [0x41, 0xC3, 0x28, 0x42];
            await repository.CreateAsync(
                new NewCharacterSpec(Account, undecodable, 0, 0, CharacterBaseStats.Zero),
                CancellationToken.None);

            var roster = await repository.ListByAccountAsync(Account, CancellationToken.None);

            Assert.Equal(undecodable, Assert.Single(roster).Name);
            Assert.True(await repository.NameExistsAsync(undecodable, CancellationToken.None));
        });

    [Fact]
    public Task SoftDeleteHidesTheCharacterAndFreesItsName() =>
        WithRepositoryAsync(async repository =>
        {
            var id = await repository.CreateAsync(
                new NewCharacterSpec(Account, "Doomed"u8.ToArray(), 0, 0, CharacterBaseStats.Zero),
                CancellationToken.None);

            Assert.True(await repository.SoftDeleteAsync(id, CancellationToken.None));

            Assert.Empty(await repository.ListByAccountAsync(Account, CancellationToken.None));
            Assert.False(await repository.NameExistsAsync("Doomed"u8.ToArray(), CancellationToken.None));
        });

    [Fact]
    public Task DeletingTwiceReportsFailureTheSecondTime() =>
        WithRepositoryAsync(async repository =>
        {
            var id = await repository.CreateAsync(
                new NewCharacterSpec(Account, "Once"u8.ToArray(), 0, 0, CharacterBaseStats.Zero),
                CancellationToken.None);

            Assert.True(await repository.SoftDeleteAsync(id, CancellationToken.None));
            Assert.False(await repository.SoftDeleteAsync(id, CancellationToken.None));
        });

    [Fact]
    public Task TheRosterIsOrderedBySlot() => WithRepositoryAsync(async repository =>
    {
        await repository.CreateAsync(
            new NewCharacterSpec(Account, "Third"u8.ToArray(), 0, 2, CharacterBaseStats.Zero), CancellationToken.None);
        await repository.CreateAsync(
            new NewCharacterSpec(Account, "First"u8.ToArray(), 0, 0, CharacterBaseStats.Zero), CancellationToken.None);
        await repository.CreateAsync(
            new NewCharacterSpec(Account, "Second"u8.ToArray(), 0, 1, CharacterBaseStats.Zero), CancellationToken.None);

        var roster = await repository.ListByAccountAsync(Account, CancellationToken.None);

        Assert.Equal(
            ["First", "Second", "Third"],
            roster.Select(character => Encoding.UTF8.GetString(character.Name)));
    });

    [Fact]
    public Task AnotherAccountsCharacterIsNotOnThisRosterButStillOwnsItsName() =>
        WithRepositoryAsync(async repository =>
        {
            await repository.CreateAsync(
                new NewCharacterSpec(new AccountId(2), "Theirs"u8.ToArray(), 0, 0, CharacterBaseStats.Zero),
                CancellationToken.None);

            Assert.Empty(await repository.ListByAccountAsync(Account, CancellationToken.None));

            // Names are unique server-wide, not per account.
            Assert.True(await repository.NameExistsAsync("Theirs"u8.ToArray(), CancellationToken.None));
        });
}
