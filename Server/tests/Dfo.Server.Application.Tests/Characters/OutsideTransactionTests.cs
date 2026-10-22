using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// What happens to a character's stored state when no use case boundary is open.
/// </summary>
/// <remarks>
/// Reading is allowed: it is how state is looked at without anything being changed. Changing is
/// refused, and refused loudly. A change made here would be one nothing could take back -- no
/// transaction to unwind it, no failure for anyone to see -- which is the silent non-atomic write
/// the boundary exists to make impossible.
/// </remarks>
public sealed class OutsideTransactionTests
{
    private static readonly OwnedCharacterId Owned = new(new AccountId(6), new CharacterId(60));

    [Fact]
    public async Task ChangingACharacterOutsideAUseCaseIsRefused()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            var access = new AmbientStoreAccess(new ClosedTransactionAccessor());

            var refusal = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await access.ChangeAsync(
                    fixture.Characters,
                    rows =>
                    {
                        rows.WritePosition(Owned, new CharacterPositionState(1, 2, 3, 4, 5));
                        return ValueTask.CompletedTask;
                    },
                    CancellationToken.None));

            Assert.Contains("outside a use case boundary", refusal.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ReadingACharacterOutsideAUseCaseIsAllowed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var access = new AmbientStoreAccess(new ClosedTransactionAccessor());

        var position = await access.ReadAsync(
            fixture.Characters, rows => rows.ReadPositionAsync(Owned, CancellationToken.None));

        Assert.NotNull(position);
    }

    /// <remarks>
    /// A store that keeps nothing past the process has nothing that could be left half done, so it
    /// is not refused. That is the same split the containers make: the character's own inventory
    /// refuses, what a dungeon handed them does not.
    /// </remarks>
    [Fact]
    public async Task ChangingSomethingThatOutlivesNothingIsAllowedOutsideAUseCase()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var access = new AmbientStoreAccess(new ClosedTransactionAccessor());
            var run = new VolatileItemDomainStore();
            var dungeon = new ContainerId("dungeon");

            await access.ChangeAsync(
                run,
                async held =>
                {
                    await held.PutAsync(dungeon, 0, await held.CreateAsync(ItemSnapshot.FromCore(Potion(), true), default), default);
                },
                CancellationToken.None);

            // Allowed is not the same as effective. A boundary opened here has to be settled here
            // too, or the write is staged and then dropped with nothing to show for it.
            var kept = await access.ReadAsync(
                run, held => held.GetAsync(dungeon, 0, CancellationToken.None));
            Assert.Equal(PotionId, kept!.ItemId);
        });
    }

    private const int PotionId = 1000;

    private static InventoryItemCoreState Potion() =>
        new(6, PotionId, 1, 0, 0, 0, 0, 0, 0, 0, -1,
            new ChronicleOptionState(0, 0, 0, 0, 0), new ChronicleOptionState(0, 0, 0, 0, 0),
            0, 0, 0, 0, 0,
            new RandomOptionState(0, 0, 0), new RandomOptionState(0, 0, 0),
            new RandomOptionState(0, 0, 0), 0, byte.MaxValue, 0, new RandomOptionState(0, 0, 0),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Where a character was standing, saved from inside a use case and from outside one.
    /// </summary>
    /// <remarks>
    /// This seam is reached both ways and has to behave differently in each. Inside a use case it
    /// must write through that one, or the place a character is standing would settle on a second
    /// connection independently of everything else the use case decided. Outside one -- connection
    /// teardown, which is not a command -- it must open a boundary rather than skip having one.
    /// </remarks>
    [Fact]
    public async Task WhereACharacterWasStandingIsSavedThroughAnOpenUseCase()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            var scope = new DomainTransactionScope();
            var persistence = new SelectedSessionPositionPersistence(fixture.Characters, scope, new StubEntryPlacementRuleService());
            var moved = new CharacterPositionState(11, 22, 33, 44, 1);

            await using (var lease = scope.Begin())
            {
                await persistence.PersistAsync(
                    Owned.Account, Owned.Character, moved, CancellationToken.None);

                // Still inside the use case, so nothing outside it can see the change yet. The row
                // exists either way, so what is asserted is that it still says where it said before.
                Assert.NotEqual(moved, await ReadPositionAsync(fixture));
                await lease.CommitAsync(CancellationToken.None);
            }

            Assert.Equal(moved, await ReadPositionAsync(fixture));
        });
    }

    [Fact]
    public async Task WhereACharacterWasStandingIsSavedWhenNoUseCaseIsOpen()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            var persistence = new SelectedSessionPositionPersistence(
                fixture.Characters, new ClosedTransactionAccessor(), new StubEntryPlacementRuleService());
            var moved = new CharacterPositionState(11, 22, 33, 44, 1);

            await persistence.PersistAsync(
                Owned.Account, Owned.Character, moved, CancellationToken.None);

            Assert.Equal(moved, await ReadPositionAsync(fixture));
        });
    }

    /// <summary>A session ending inside another use case settles with that use case.</summary>
    /// <remarks>
    /// Selecting a character displaces whatever the account had selected before, and that session's
    /// teardown happens inside the new selection. It writes through that selection rather than
    /// beside it, so a selection that fails takes the departing character's last position with it.
    ///
    /// That is a deliberate trade. Settling beside it would put a second connection on the same
    /// database while the selection is holding a view of it, and once the repositories on this path
    /// reach the database through the use case, the selection can no longer write at all -- what it
    /// is looking at is no longer what is there. A rare failure that leaves a slightly older stored
    /// position is a smaller cost than a selection that cannot happen.
    /// </remarks>
    [Fact]
    public async Task ASessionEndingInsideAnotherUseCaseSettlesWithIt()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            var scope = new DomainTransactionScope();
            var persistence = new SelectedSessionPositionPersistence(fixture.Characters, scope, new StubEntryPlacementRuleService());
            var departed = new CharacterPositionState(11, 22, 33, 44, 1);

            await using (var failing = scope.Begin())
            {
                await persistence.PersistAsync(
                    Owned.Account, Owned.Character, departed, CancellationToken.None);
                _ = failing;
            }

            Assert.NotEqual(departed, await ReadPositionAsync(fixture));

            await using (var succeeding = scope.Begin())
            {
                await persistence.PersistAsync(
                    Owned.Account, Owned.Character, departed, CancellationToken.None);
                await succeeding.CommitAsync(CancellationToken.None);
            }

            Assert.Equal(departed, await ReadPositionAsync(fixture));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Inn_and_current_position_settle_together_even_after_multiple_crossings(bool commit)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            var scope = new DomainTransactionScope();
            var persistence = new SelectedSessionPositionPersistence(fixture.Characters, scope,
                new StubEntryPlacementRuleService(innPredicate: town => town is 1 or 11));
            var timeGate = new CharacterPositionState(12, 0, 562, 183, 5);
            await using (var lease = scope.Begin())
            {
                await persistence.PersistAsync(Owned.Account, Owned.Character, new(11, 0, 10, 20, 5), default);
                await persistence.PersistAsync(Owned.Account, Owned.Character, timeGate, default);
                if (commit) await lease.CommitAsync(default);
            }
            Assert.Equal(commit ? 11 : 0, await ReadInnAsync(fixture));
            if (commit) Assert.Equal(timeGate, await ReadPositionAsync(fixture));
            else Assert.NotEqual(timeGate, await ReadPositionAsync(fixture));
        });
    }

    [Fact]
    public async Task Teardown_in_a_town_without_an_inn_preserves_the_previous_inn()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var fixture = await Fixture.CreateAsync();
            var persistence = new SelectedSessionPositionPersistence(fixture.Characters, new ClosedTransactionAccessor(),
                new StubEntryPlacementRuleService(innPredicate: town => town is 1 or 11));
            await persistence.PersistAsync(Owned.Account, Owned.Character, new(11, 0, 10, 20, 5), default);
            var destination = new CharacterPositionState(12, 0, 562, 183, 5);
            await persistence.PersistAsync(Owned.Account, Owned.Character, destination, default);
            await persistence.PersistAsync(Owned.Account, Owned.Character, destination, default);
            Assert.Equal(11, await ReadInnAsync(fixture));
            Assert.Equal(destination, await ReadPositionAsync(fixture));
        });
    }

    [Fact]
    public async Task Legacy_schema_gains_an_unset_inn_without_changing_the_saved_position()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fixture.Path }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE characters SET town_id=11; ALTER TABLE characters DROP COLUMN last_inn_town_id;";
            await command.ExecuteNonQueryAsync();
        }
        _ = new SqliteDatabase(fixture.Path, new DomainTransactionScope());
        _ = new SqliteDatabase(fixture.Path, new DomainTransactionScope());
        Assert.Equal(0, await ReadInnAsync(fixture));
        Assert.Equal(11, (await ReadPositionAsync(fixture))!.TownId);
    }

    private static async Task<int> ReadInnAsync(Fixture fixture)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fixture.Path }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_inn_town_id FROM characters WHERE character_id=60";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async ValueTask<CharacterPositionState?> ReadPositionAsync(Fixture fixture)
    {
        var reading = new DomainTransaction();
        var position = await fixture.Characters.SessionIn(reading)
            .ReadPositionAsync(Owned, CancellationToken.None);
        await reading.CommitAsync(CancellationToken.None);
        return position;
    }

    private sealed class ClosedTransactionAccessor : IDomainTransactionAccessor
    {
        public IDomainTransaction? Current => null;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string path)
        {
            this.Path = path;
            this.Characters = new SqliteCharacterDomainStore(path);
        }

        public string Path { get; }

        public SqliteCharacterDomainStore Characters { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-outside-{Guid.NewGuid():N}.db");
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            var fixture = new Fixture(path);
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();
            await using var character = connection.CreateCommand();
            character.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'read')";
            await character.ExecuteNonQueryAsync();
            return fixture;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(this.Path);
            }
            catch (IOException)
            {
                // A file the runtime still holds is left for the temp directory to reap.
            }

            return ValueTask.CompletedTask;
        }
    }
}
