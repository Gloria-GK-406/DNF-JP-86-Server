using Dfo.Server.Application.Transactions;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Persistence.Tests.Support;
using Microsoft.Data.Sqlite;
using Dfo.Server.Testing;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// Whether two avatars can end up standing in one world under one identity.
/// </summary>
/// <remarks>
/// An avatar's identity is the four bytes its core carries at offset five, and it is the only thing
/// that tells two avatars apart once they are rows. Every reader of an avatar addresses it by that
/// value, so two rows sharing one is not a duplicate record but two things the server can no longer
/// name separately -- and the write that made the second of them is the last moment anything could
/// have noticed.
///
/// <see cref="SqliteItemDomainStore"/> has a guard against exactly that, in the INSERT its fill
/// issues: the row is written only when nothing already standing carries the same four bytes. What
/// these three cases hold is not only that the guard refuses a collision, but that it is reached at
/// all -- because until a write could carry an avatar's six facts, it was not.
///
/// The guard's condition begins <c>@expire IS NULL OR ...</c>, and its subquery matches only rows
/// whose <c>avatar_expire_date</c> is not null. Both halves used to be null for anything born in
/// this world: the six columns of a newly inserted row could only ever come from the retrieval that
/// reads them off a standing row, and an avatar entering the world for the first time has no
/// standing row to be read off. So a first-time write bound a null <c>@expire</c>, short-circuited
/// the whole condition, and landed a row whose six columns were null -- which the next write's
/// subquery could not see either. The blindness fed itself. A write that carries its own six facts
/// is what breaks the circle at both ends, and the third case below is that contrast, run twice
/// over the same code with nothing changed but whether the writes carry facts.
/// </remarks>
public sealed class TwoAvatarsDoNotShareOneIdentityTests
{
    private const int Account = 84;

    private const int Character = 93;

    private const byte AvatarKind = 8;

    private const int FirstAvatarId = 2600;

    private const int SecondAvatarId = 2601;

    private static readonly CharacterId Owner = new(Character);

    private static readonly ContainerId Avatars =
        ContainerId.ForCharacter(Owner, InventoryContainerType.Avatar);

    /// <summary>
    /// Two avatars put down by two separate settlements stand under two identities of their own.
    /// </summary>
    /// <remarks>
    /// Two settlements rather than one, because that is what two deliveries are: the second write
    /// meets a world that already holds an avatar, which is the only arrangement in which the guard
    /// has anything to say. One settlement writing both would prove that two rows can be inserted
    /// and nothing about whether the second was judged against the first.
    ///
    /// Both are read back through the entry load rather than out of the columns, because "the world
    /// took it" means the server can pick it up again: the load refuses an avatar whose identity is
    /// not above zero, whose six facts are missing, or whose jewel socket is not thirty bytes, so an
    /// avatar it serves is one that got through all three.
    ///
    /// The six facts are compared field by field and against the values each write carried, so that
    /// "each kept its own" cannot be satisfied by both rows answering with the same thing, nor by
    /// both answering with nothing.
    /// </remarks>
    [Fact]
    public async Task TwoAvatarsPutDownInSeparateSettlementsStandUnderIdentitiesOfTheirOwn()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await PutDownAsync(path, slot: 4, itemId: FirstAvatarId, avatarUid: 9101, Facts(1));
            await PutDownAsync(path, slot: 5, itemId: SecondAvatarId, avatarUid: 9102, Facts(2));

            var one = await OneAvatarAsync(side, slot: 4);
            var other = await OneAvatarAsync(side, slot: 5);

            // Each carries the identity its own write named, and the two are not each other's.
            Assert.Equal(9101, one.Snapshot.InstanceValue);
            Assert.Equal(9102, other.Snapshot.InstanceValue);
            Assert.NotEqual(one.Snapshot.InstanceValue, other.Snapshot.InstanceValue);

            // The world took both: each has a row of its own, and the rows are not the same row.
            Assert.NotNull(one.ItemUid);
            Assert.NotNull(other.ItemUid);
            Assert.NotEqual(one.ItemUid, other.ItemUid);
            Assert.Equal(1, await RowsUnderAsync(path, 9101));
            Assert.Equal(1, await RowsUnderAsync(path, 9102));

            // And each is served the six facts its own write carried, not the other's.
            AssertFacts(Facts(1), one.Snapshot.AvatarDetail);
            AssertFacts(Facts(2), other.Snapshot.AvatarDetail);
        });
    }

    /// <summary>
    /// A write naming an identity the world already stands under is refused, not written beside it.
    /// </summary>
    /// <remarks>
    /// The refusal is an <see cref="InvalidOperationException"/> raised where the insert returned no
    /// row, which is what a <c>RETURNING</c> whose <c>WHERE</c> failed does: the statement succeeds
    /// and writes nothing, so nothing but the missing identity says the row was refused. It is
    /// raised by creation inside the transaction the participant holds open, so the settlement
    /// takes nothing with it -- which is asserted below rather than assumed, by reading the world
    /// back after the rollback.
    ///
    /// The colliding write names a different template and a different slot, so what it collides on
    /// is the identity and nothing else.
    /// </remarks>
    [Fact]
    public async Task AWriteNamingAnIdentityTheWorldAlreadyStandsUnderIsRefusedAndRolledBack()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await PutDownAsync(path, slot: 4, itemId: FirstAvatarId, avatarUid: 9101, Facts(1));

            var refusal = await RefusedAsync(
                path, slot: 5, itemId: SecondAvatarId, avatarUid: 9101, Facts(2));
            Assert.Contains("identity", refusal.Message, StringComparison.OrdinalIgnoreCase);

            // Nothing was written beside it, and nothing was taken from it.
            Assert.Equal(1, await RowsUnderAsync(path, 9101));
            Assert.Equal([(4, FirstAvatarId, 9101)], await AvatarSlotsAsync(path));
            AssertFacts(Facts(1), (await OneAvatarAsync(side, slot: 4)).Snapshot.AvatarDetail);
        });
    }

    /// <summary>Missing details are rejected before any avatar row can be created.</summary>
    [Fact]
    public async Task MissingFactsCannotCreateAnAvatarAndCompleteFactsDoNotPermitDuplicateIdentity()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                PutDownAsync(path, slot: 4, itemId: FirstAvatarId, avatarUid: 9201, facts: null));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                PutDownAsync(path, slot: 5, itemId: SecondAvatarId, avatarUid: 9201, facts: null));
            Assert.Equal(0, await RowsUnderAsync(path, 9201));
            Assert.Empty(await AvatarSlotsAsync(path));

            await PutDownAsync(path, slot: 6, itemId: FirstAvatarId, avatarUid: 9202, Facts(1));
            Assert.NotNull(await ExpireDateUnderAsync(path, 9202));
            await RefusedAsync(path, slot: 7, itemId: SecondAvatarId, avatarUid: 9202, Facts(2));
            Assert.Equal(1, await RowsUnderAsync(path, 9202));
            AssertFacts(Facts(1), (await OneAvatarAsync(side, 6)).Snapshot.AvatarDetail);
        });
    }

    private static Task PutDownAsync(
        string path, int slot, int itemId, int avatarUid, InventoryAvatarDetailState? facts) =>
        SettleAsync(path, async session =>
        {
            var item = await session.CreateAsync(
                ItemSnapshot.FromCore(Core(itemId, avatarUid), false, avatar: facts), default);
            await session.PutAsync(Avatars, slot, item, default);
        });

    private static Task<InvalidOperationException> RefusedAsync(
        string path, int slot, int itemId, int avatarUid, InventoryAvatarDetailState facts) =>
        Assert.ThrowsAsync<InvalidOperationException>(() => PutDownAsync(path, slot, itemId, avatarUid, facts));

    private static async Task SettleAsync(string path, Func<IItemDomainStoreSession, ValueTask> work)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var scope = new DomainTransactionScope();
            await using var transaction = scope.Begin();
            await work(new SqliteItemDomainStore(path).SessionIn(scope.Current!));
            await transaction.CommitAsync(CancellationToken.None);
        });
    }

    /// <summary>The six facts of the nth avatar, every one of them distinct and none defaulted.</summary>
    private static InventoryAvatarDetailState Facts(int which) => new(
        ExpireDate: 1_700_000_000 + which,
        ClearAvatarId: 6200 + which,
        JewelSocket: [.. Enumerable.Range(0, 30).Select(index => (byte)(index + which))],
        Color1: (ushort)(1100 + which),
        Color2: (ushort)(2100 + which),
        DeleteDate: 1_800_000_000 + which);

    /// <summary>Every one of the six by name, so a single dropped field fails on that field.</summary>
    private static void AssertFacts(
        InventoryAvatarDetailState expected, InventoryAvatarDetailState? found)
    {
        Assert.NotNull(found);
        Assert.Equal(expected.ExpireDate, found.ExpireDate);
        Assert.Equal(expected.ClearAvatarId, found.ClearAvatarId);
        Assert.Equal(expected.JewelSocket, found.JewelSocket);
        Assert.Equal(expected.Color1, found.Color1);
        Assert.Equal(expected.Color2, found.Color2);
        Assert.Equal(expected.DeleteDate, found.DeleteDate);
    }

    /// <summary>The avatar the entry load serves at that position of the avatar container.</summary>
    private static async Task<ItemReference> OneAvatarAsync(InventorySide side, int slot)
    {
        var loaded = await side.InUseCaseAsync(token => side.CharacterEntries
            .LoadCharacterAsync(new AccountId(Account), new CharacterId(Character), token));
        var item = Assert.Single(
            loaded.CharacterItems,
            value => value.ContainerId == Avatars
                && value.SlotIndex == slot);
        Assert.Equal(AvatarKind, item.Snapshot.LegacyKind);
        return item;
    }

    /// <summary>How many rows of the world carry that identity in their core.</summary>
    private static async Task<long> RowsUnderAsync(string path, int avatarUid)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT COUNT(*) FROM items WHERE substr(item_core,6,4)=@uid";
        command.Parameters.AddWithValue("@uid", AvatarKeyOf(avatarUid));
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// The expiry on the row standing under that identity, which is what the guard selects on.
    /// </summary>
    /// <remarks>
    /// Read as the plain column rather than through the load, because null is the answer this is
    /// asked for and the load refuses a row that answers null rather than reporting it.
    /// </remarks>
    private static async Task<int?> ExpireDateUnderAsync(string path, int avatarUid)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT avatar_expire_date FROM items WHERE substr(item_core,6,4)=@uid "
            + "ORDER BY item_uid LIMIT 1";
        command.Parameters.AddWithValue("@uid", AvatarKeyOf(avatarUid));
        var found = await command.ExecuteScalarAsync();
        return found is null or DBNull ? null : Convert.ToInt32(found, null);
    }

    /// <summary>What stands in the avatar container, by position, template and identity.</summary>
    private static async Task<List<(int Slot, int ItemId, int AvatarUid)>> AvatarSlotsAsync(
        string path)
    {
        await using var connection = Open(path);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT place.slot_index,held.item_core "
            + "FROM item_slots place JOIN items held ON held.item_uid=place.core_uid "
            + "WHERE place.owner_scope='character' AND place.owner_id=@owner "
            + "AND place.list_type=@list ORDER BY place.slot_index";
        command.Parameters.AddWithValue("@owner", Character);
        command.Parameters.AddWithValue("@list", (int)InventoryContainerType.Avatar);
        var rows = new List<(int, int, int)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var core = (byte[])reader.GetValue(1);
            rows.Add((
                reader.GetInt32(0),
                BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(1, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(core.AsSpan(5, 4))));
        }

        return rows;
    }

    /// <summary>An avatar UID as the four bytes a core carries it as.</summary>
    private static byte[] AvatarKeyOf(int avatarUid)
    {
        var key = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(key, avatarUid);
        return key;
    }

    private static InventoryItemCoreState Core(int itemId, int avatarUid)
    {
        var core = new byte[82];
        core[0] = AvatarKind;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5), avatarUid);
        return InventoryItemCore.Decode(core);
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        return connection;
    }

    private static async Task WithASeededCharacterAsync(Func<string, InventorySide, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var path = Path.Combine(Path.GetTempPath(), $"dfo-avatarpair-{Guid.NewGuid():N}.db");
        try
        {
            var side = new InventorySide(path, new AvatarTemplates());
            await using (var connection = Open(path))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    INSERT INTO accounts(account_id,m_id) VALUES({Account},'pair');
                    INSERT INTO characters(character_id,account_id,name,job,level)
                    VALUES({Character},{Account},'pair',3,60);
                    """;
                await command.ExecuteNonQueryAsync();
            }

            await body(path, side);
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

    /// <summary>The two templates these tests use are avatars; everything else is a pile.</summary>
    private sealed class AvatarTemplates : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(itemId is FirstAvatarId or SecondAvatarId
                ? new ItemPlacement(AvatarKind, 1, new(BusinessInventorySpace.Avatar, 0, 255))
                    { WorldClass = ItemWorldClass.Avatar }
                : new ItemPlacement(2, 100, new(BusinessInventorySpace.Consumable, 0, 55)));
    }

}
