using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Accounts;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// What the two write paths that do not go through a container do with the archive's answer.
/// </summary>
/// <remarks>
/// Settlement is not the only thing that writes items. Creation lands a character's starting gear
/// straight into the table, and a dirty entry lease writes back everything the lease held. Both ask
/// the same classification the container asks, and both refuse a row the answer contradicts --
/// which is the only reason asking there is worth anything, because neither of them settles through
/// the store and so neither of them is held to it by anything else.
///
/// Each case is paired with the write that is not contradicted, so what these hold is the refusal
/// rather than the path being broken. The archive is stood in for, because what is under test is
/// what the path does with an answer; that the answer comes from the archive is
/// <c>ItemClassificationTests</c>'s.
/// </remarks>
public sealed class ClassificationRefusalTests
{
    private static readonly AccountId Account = new(6);

    /// <summary>
    /// A starting piece the archive reads as a pile is refused rather than welded to a worn slot.
    /// </summary>
    [Fact]
    public async Task CreationRefusesAStartingPieceTheArchiveReadsAsAPile()
    {
        await using var database = NewDatabase();
        var refusal = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await database.InUseCaseAsync(token => Roster(database, ItemWorldClass.Interchangeable)
                .CreateAsync(SpecWearing(WeaponId), token)));

        Assert.Contains(WeaponId.ToString(null, null), refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM items"));
    }

    /// <summary>The same creation, with the archive reading the piece as a worn piece.</summary>
    [Fact]
    public async Task CreationWearsAStartingPieceTheArchiveReadsAsAWornPiece()
    {
        await using var database = NewDatabase();
        _ = await database.InUseCaseAsync(token => Roster(database, ItemWorldClass.Equipment)
            .CreateAsync(SpecWearing(WeaponId), token));

        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM items"));
    }

    /// <summary>
    /// A lease row written as something else than the avatar the archive declares is refused.
    /// </summary>
    /// <remarks>
    /// The kind byte and the archive disagreeing is the whole reason the classification exists, so
    /// the disagreement is what is written here: kind 2, an ordinary consumable, on a template the
    /// archive calls an avatar. Nothing downstream would have noticed -- a projection assembles the
    /// bytes the client expects out of either -- which is why the refusal has to be here.
    /// </remarks>
    [Fact]
    public async Task AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot()
    {
        await using var database = NewDatabase();
        await database.SeedCharacterAsync();
        var refusal = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await database.InUseCaseAsync(async token =>
            {
                await Assembly(database, ItemWorldClass.Avatar)
                    .SaveAsync(Lease(itemKind: 2), token);
                return true;
            }));

        Assert.Contains(AvatarItemId.ToString(null, null), refusal.Message, StringComparison.Ordinal);
        Assert.Equal(0, await database.CountAsync("SELECT COUNT(*) FROM items"));
    }

    /// <summary>The same save, with the row saying what the archive says.</summary>
    [Fact]
    public async Task AnEntrySaveKeepsARowThatSaysWhatTheArchiveSays()
    {
        await using var database = NewDatabase();
        await database.SeedCharacterAsync();
        _ = await database.InUseCaseAsync(async token =>
        {
            await Assembly(database, ItemWorldClass.Avatar).SaveAsync(Lease(itemKind: 8), token);
            return true;
        });

        Assert.Equal(1, await database.CountAsync("SELECT COUNT(*) FROM items"));
    }

    private const int WeaponId = 4001;

    private const int AvatarItemId = 3001;

    private static NewCharacterSpec SpecWearing(int itemId) => new(
        Account, "Nagisa"u8.ToArray(), Job: 3, SlotIndex: 0, CharacterBaseStats.Zero,
        InitialEquipment: [new NewCharacterEquipment(0, itemId, 100, ItemKind: 1)]);

    private static EntryInventoryLeaseSnapshot Lease(byte itemKind) => new(
        SelectedSessionIdentity.Create(
            Account, new CharacterId(60), new NetworkConnectionId("Game:classify"), 1, 10011),
        new CharacterItemLeaseState(
            new CharacterId(60),
            [
                new InventoryContainerState(
                    InventoryContainerType.Main,
                    0,
                    [
                        new InventoryItemState(
                            null,
                            65,
                            StackableItemCore.Create(itemKind, AvatarItemId, 1),
                            itemKind == 8
                                ? new InventoryAvatarDetailState(1, 2, new byte[30], 3, 4, 5)
                                : null),
                    ]),
            ]),
        1,
        true);

    private static ICharacterRepository Roster(TestDatabase database, ItemWorldClass declared) =>
        new CharacterRosterService(
            new SqliteCharacterTable(database.Value),
            new SqliteCharacterSkillTable(database.Value),
            new SqliteCharacterConditionTable(database.Value),
            new SqliteCharacterStatsTable(database.Value),
            new SqliteCharacterPvpTable(database.Value),
            new SqliteCharacterSettingsTable(database.Value),
            new SqliteCharacterExpertJobTable(database.Value),
            new SqliteCharacterContainerStateTable(database.Value),
            new SqliteCharacterItemTable(database.Value),
            Rules(declared));

    private static IEntryInventoryLeasePersistence Assembly(
        TestDatabase database, ItemWorldClass declared) =>
        new InventoryEntryAssembly(
            new SqliteCharacterTable(database.Value),
            new SqliteAccountTable(database.Value),
            new SqliteCharacterItemTable(database.Value),
            new SqliteAccountCargoItemTable(database.Value),
            new SqliteCharacterContainerStateTable(database.Value),
            new SqliteAccountCargoStateTable(database.Value),
            new SqliteCharacterGoldLimitTable(database.Value),
            new SqliteCharacterCarriedGoldTable(database.Value),
            new SqliteCharacterItemCooldownTable(database.Value),
            new SqliteCharacterItemEffectTable(database.Value),
            new SqliteCharacterItemLockTable(database.Value),
            new SqliteCharacterRentalTable(database.Value),
            Rules(declared));

    private static ItemPlacementRuleService Rules(ItemWorldClass declared) =>
        new(new Declaring(declared), new NoWornSlots());

    /// <summary>Stands in for the archive, declaring every template the same thing.</summary>
    private sealed class Declaring(ItemWorldClass declared) : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(
                new ItemPlacement(1, 1, 0, 120) { WorldClass = declared });
    }

    private sealed class NoWornSlots : IEquipmentSlotDefinitionSource
    {
        public ValueTask<byte?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<byte?>(null);
    }

    private static TestDatabase NewDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-classify-{Guid.NewGuid():N}.db");
        var scope = new DomainTransactionScope();
        return new TestDatabase(path, scope, new SqliteDatabase(path, scope));
    }

    private sealed record TestDatabase(
        string Path, DomainTransactionScope Scope, SqliteDatabase Value) : IAsyncDisposable
    {
        /// <summary>Runs one call inside a use case and settles it, the way one command would.</summary>
        public async Task<TResult> InUseCaseAsync<TResult>(
            Func<CancellationToken, ValueTask<TResult>> work)
        {
            await using var lease = this.Scope.Begin();
            var result = await work(CancellationToken.None).ConfigureAwait(false);
            await lease.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return result;
        }

        /// <summary>The character a lease save writes back for, which must already exist.</summary>
        public async Task SeedCharacterAsync()
        {
            await using var connection = this.Open();
            await using var account = connection.CreateCommand();
            account.CommandText =
                "INSERT INTO accounts(account_id,m_id,password_hash) VALUES(6,'account-6','')";
            await account.ExecuteNonQueryAsync();
            await using var character = connection.CreateCommand();
            character.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'classify')";
            await character.ExecuteNonQueryAsync();
        }

        public async Task<long> CountAsync(string sql)
        {
            await using var connection = this.Open();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync(), null);
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

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = this.Path }.ToString());
            connection.Open();
            return connection;
        }
    }
}
