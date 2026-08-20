using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

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
        var access = new AmbientStoreAccess(new ClosedTransactionAccessor());
        var run = new MemoryItemStore();
        var dungeon = new ContainerId("dungeon");

        await access.ChangeAsync(
            run,
            held =>
            {
                held.Write(dungeon, new StoredItem(0, Potion()));
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);

        // Allowed is not the same as effective. A boundary opened here has to be settled here
        // too, or the write is staged and then dropped with nothing to show for it.
        var kept = await access.ReadAsync(
            run, held => held.ReadAsync(dungeon, 0, CancellationToken.None));
        Assert.Equal(PotionId, kept!.Core.ItemId);
    }

    private const int PotionId = 1000;

    private static InventoryItemCoreState Potion() =>
        new(6, PotionId, 1, 0, 0, 0, 0, 0, 0, 0, -1,
            new ChronicleOptionState(0, 0, 0, 0, 0), new ChronicleOptionState(0, 0, 0, 0, 0),
            0, 0, 0, 0, 0,
            new RandomOptionState(0, 0, 0), new RandomOptionState(0, 0, 0),
            new RandomOptionState(0, 0, 0), 0, byte.MaxValue, 0, new RandomOptionState(0, 0, 0),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private sealed class ClosedTransactionAccessor : IDomainTransactionAccessor
    {
        public IDomainTransaction? Current => null;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string path)
        {
            this.Path = path;
            this.Characters = new SqliteCharacterStore(path);
        }

        public string Path { get; }

        public SqliteCharacterStore Characters { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-outside-{Guid.NewGuid():N}.db");
            _ = new SqliteCharacterRepository(path);
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
