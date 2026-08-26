using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Characters;

/// <summary>Persistence for character creation, name lookup and soft deletion.</summary>
public sealed class SqliteCharacterLifecycleTests
{
    private static readonly AccountId Account = new(1);

    private static async Task WithRepositoryAsync(Func<ICharacterRepository, Task> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await body(new CharacterSide(Path.Combine(directory, "characters.db")).PerCall);
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
            var created = await new AccountSide(databasePath).GetOrCreateAsync("10038", "hash");
            Assert.Equal(Account, created.AccountId);

            var repository = new CharacterSide(databasePath).PerCall;
            var id = await repository.CreateAsync(
                new NewCharacterSpec(Account, "Nagisa"u8.ToArray(), Job: 3, SlotIndex: 0, CharacterBaseStats.Zero),
                CancellationToken.None);

            var entryState = new CharacterSide(databasePath).EntryState(new GoldLimitRuleService());
            var record = await entryState.LoadAsync(Account, id, CancellationToken.None);

            Assert.Equal(id, record.CharacterId);
            Assert.Equal(3, record.Profile.Job);
            Assert.Equal(1, record.Profile.Level);

            // Entering also loads account-scoped state, and that side reads this character's
            // rows too. Asserting only the character loader is what let a null pc_room_state
            // through: the character profile never reads that column, while the account loader
            // left-joins it for every live character and refuses a null.
            var account = await new AccountSide(databasePath)
                .Entry.LoadAsync(Account, CancellationToken.None);

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
            await new AccountSide(databasePath).GetOrCreateAsync("10038", "hash");

            var stats = new CharacterBaseStats(
                HpMax: 1800, MpMax: 1400,
                PhysicalAttack: 75, PhysicalDefense: 75,
                MagicalAttack: 45, MagicalDefense: 45,
                DarkResistance: 200, LightResistance: -200,
                InventoryLimit: 480000, MpRegenSpeed: 500,
                MoveSpeed: 8500, AttackSpeed: 8500, CastSpeed: 7000,
                HitRecovery: 6000, JumpPower: 4300, Weight: 500000);
            var repository = new CharacterSide(databasePath).PerCall;
            var id = await repository.CreateAsync(
                new NewCharacterSpec(Account, "Nagisa"u8.ToArray(), Job: 0, SlotIndex: 0, stats),
                CancellationToken.None);

            var stored = (await new CharacterSide(databasePath).EntryState(new GoldLimitRuleService())
                .LoadAsync(Account, id, CancellationToken.None)).Profile.DerivedStats;

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
            var repository = new CharacterSide(databasePath).PerCall;
            await using (var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString()))
            {
                await connection.OpenAsync(CancellationToken.None);
                await using var drop = connection.CreateCommand();
                drop.CommandText = "DROP TABLE character_settings;";
                await drop.ExecuteNonQueryAsync(CancellationToken.None);
            }

            await Assert.ThrowsAnyAsync<Exception>(async () => await repository.CreateAsync(
                new NewCharacterSpec(Account, "Doomed"u8.ToArray(), Job: 1, SlotIndex: 0, CharacterBaseStats.Zero),
                CancellationToken.None));

            Assert.Empty(await repository.ListByAccountAsync(Account, CancellationToken.None));
        });
    }

    /// <summary>
    /// The roster carries the stored appearance parts, and a character without any carries none.
    /// </summary>
    /// <remarks>
    /// What the roster draws is what the character is wearing, so the parts are the equipment the
    /// creation was given and a character given none carries none. Both halves are reached through
    /// the repository because both are now shapes it produces.
    ///
    /// The stored-blob half is still exercised by hand afterwards. A blob written by something
    /// other than this creation -- an older row, another tool -- must still decode, and nothing
    /// the repository does can produce one to test that with.
    /// </remarks>
    [Fact]
    public Task TheRosterCarriesStoredAppearancePartsAndCopesWithNone()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-character-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "characters.db");
        return RunAndCleanUpAsync(directory, async () =>
        {
            await new AccountSide(databasePath).GetOrCreateAsync("account", "hash");
            var repository = new CharacterSide(databasePath).PerCall;

            var bare = await repository.CreateAsync(
                new NewCharacterSpec(Account, "Bare"u8.ToArray(), Job: 1, SlotIndex: 0, CharacterBaseStats.Zero),
                CancellationToken.None);
            var clothed = await repository.CreateAsync(
                new NewCharacterSpec(
                    Account, "Clothed"u8.ToArray(), Job: 0, SlotIndex: 2, CharacterBaseStats.Zero,
                    InitialEquipment:
                    [
                        new NewCharacterEquipment(12, 27600, 45),
                        new NewCharacterEquipment(14, 10400, 33),
                        new NewCharacterEquipment(16, 12400, 27),
                    ]),
                CancellationToken.None);
            var created = await repository.ListByAccountAsync(Account, CancellationToken.None);

            // The parts are the granted equipment the roster draws, in its slots. A creation given
            // nothing to wear writes nothing rather than a stand-in set: the roster would
            // otherwise draw every character holding whichever job's weapon the stand-in was
            // transcribed from. The coat and the pants are worn but not drawn -- the roster's
            // appearance stops at slot thirteen.
            Assert.Empty(created.Single(value => value.CharacterId == bare).Appearances);
            var worn = created.Single(value => value.CharacterId == clothed).Appearances;
            Assert.Equal([12], worn.Select(value => (int)value.Slot));
            Assert.Equal([27600], worn.Select(value => value.DisplayItemId));
            Assert.Equal([4], worn.Select(value => value.ExpansionLength));

            var dressed = await repository.CreateAsync(
                new NewCharacterSpec(Account, "Dressed"u8.ToArray(), Job: 2, SlotIndex: 1, CharacterBaseStats.Zero),
                CancellationToken.None);

            // One count byte followed by one twenty-three byte entry, which is how the column is
            // laid out wherever it does get written.
            byte[] blob =
            [
                0x01,
                0x0C, 0x80, 0x80, 0xCB, 0x06, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            ];
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync(CancellationToken.None);
                await using var update = connection.CreateCommand();
                update.CommandText =
                    "UPDATE characters SET appearance_blob = @blob WHERE character_id = @id;";
                update.Parameters.AddWithValue("@blob", blob);
                update.Parameters.AddWithValue("@id", dressed.Value);
                Assert.Equal(1, await update.ExecuteNonQueryAsync(CancellationToken.None));
            }

            // NULL rather than an empty blob. Creation writes a one-byte count of zero for a
            // character with nothing to wear, and a column holding nothing at all is a different
            // input the decoder must also survive.
            await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                await connection.OpenAsync(CancellationToken.None);
                await using var clear = connection.CreateCommand();
                clear.CommandText =
                    "UPDATE characters SET appearance_blob = NULL WHERE character_id = @id;";
                clear.Parameters.AddWithValue("@id", bare.Value);
                Assert.Equal(1, await clear.ExecuteNonQueryAsync(CancellationToken.None));
            }

            var roster = await repository.ListByAccountAsync(Account, CancellationToken.None);

            Assert.Empty(roster.Single(value => value.CharacterId == bare).Appearances);
            var part = Assert.Single(roster.Single(value => value.CharacterId == dressed).Appearances);
            Assert.Equal(0x0C, part.Slot);
            Assert.Equal(0x06CB8080, part.DisplayItemId);
            Assert.Equal(4, part.ExpansionLength);
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
