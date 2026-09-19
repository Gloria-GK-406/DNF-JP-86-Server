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
            Assert.Equal(9101, one.Core.Value);
            Assert.Equal(9102, other.Core.Value);
            Assert.NotEqual(one.Core.Value, other.Core.Value);

            // The world took both: each has a row of its own, and the rows are not the same row.
            Assert.NotNull(one.Identity);
            Assert.NotNull(other.Identity);
            Assert.NotEqual(one.Identity, other.Identity);
            Assert.Equal(1, await RowsUnderAsync(path, 9101));
            Assert.Equal(1, await RowsUnderAsync(path, 9102));

            // And each is served the six facts its own write carried, not the other's.
            AssertFacts(Facts(1), one.AvatarDetail);
            AssertFacts(Facts(2), other.AvatarDetail);
        });
    }

    /// <summary>
    /// A write naming an identity the world already stands under is refused, not written beside it.
    /// </summary>
    /// <remarks>
    /// The refusal is an <see cref="InvalidOperationException"/> raised where the insert returned no
    /// row, which is what a <c>RETURNING</c> whose <c>WHERE</c> failed does: the statement succeeds
    /// and writes nothing, so nothing but the missing identity says the row was refused. It is
    /// raised during prepare, inside the transaction the participant holds open, so the settlement
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
            Assert.Contains("named no identity", refusal.Message, StringComparison.Ordinal);

            // Nothing was written beside it, and nothing was taken from it.
            Assert.Equal(1, await RowsUnderAsync(path, 9101));
            Assert.Equal([(4, FirstAvatarId, 9101)], await AvatarSlotsAsync(path));
            AssertFacts(Facts(1), (await OneAvatarAsync(side, slot: 4)).AvatarDetail);
        });
    }

    /// <summary>
    /// The guard is blind to rows carrying no facts, and engages once the writes carry them.
    /// </summary>
    /// <remarks>
    /// Two halves, the same two writes each time, differing in one thing only: whether the write
    /// carries the six facts an avatar arrives with.
    ///
    /// The first half is the world as it stood before a write could carry them, reproduced through
    /// today's code by simply leaving them unanswered -- which is still permitted, because a write
    /// that merely moves a standing avatar has nothing to add. Both writes land, under one identity,
    /// and no error is raised anywhere: <c>@expire</c> is null on the second write as well, so the
    /// guard's condition is satisfied before its subquery is ever consulted. The first half is a
    /// record of that blind spot, not an endorsement of it; a change that closes it should rewrite
    /// this half rather than delete it, because the second half means nothing without it.
    ///
    /// The second half is the same pair carrying facts. The first write puts a row down whose
    /// <c>avatar_expire_date</c> is not null -- the very property the subquery selects on, and the
    /// one no first-time row used to have -- and the second write binds a non-null <c>@expire</c>,
    /// so the condition is no longer satisfied by its left side and the subquery finds the standing
    /// row. It is refused.
    ///
    /// Neither half alone says anything. A guard that never engaged would pass the first and fail
    /// the second; a guard that always engaged would fail the first and pass the second. Only a
    /// guard whose reach is decided by what the write carries passes both.
    /// </remarks>
    [Fact]
    public async Task TheGuardIsBlindToFactlessRowsAndEngagesOnceTheWritesCarryFacts()
    {
        await WithASeededCharacterAsync(async (path, _) =>
        {
            // Nothing carries facts: two writes under one identity, both taken, no complaint.
            await PutDownAsync(path, slot: 4, itemId: FirstAvatarId, avatarUid: 9201, facts: null);
            await PutDownAsync(path, slot: 5, itemId: SecondAvatarId, avatarUid: 9201, facts: null);
            Assert.Equal(2, await RowsUnderAsync(path, 9201));
            Assert.Null(await ExpireDateUnderAsync(path, 9201));

            // The same pair carrying facts: the first is taken, the second is refused.
            await PutDownAsync(path, slot: 6, itemId: FirstAvatarId, avatarUid: 9202, Facts(1));
            Assert.NotNull(await ExpireDateUnderAsync(path, 9202));

            var refusal = await RefusedAsync(
                path, slot: 7, itemId: SecondAvatarId, avatarUid: 9202, Facts(2));
            Assert.Contains("named no identity", refusal.Message, StringComparison.Ordinal);
            Assert.Equal(1, await RowsUnderAsync(path, 9202));
        });
    }

    /// <summary>One settlement putting one avatar down where nothing stands.</summary>
    private static Task PutDownAsync(
        string path, int slot, int itemId, int avatarUid, InventoryAvatarDetailState? facts) =>
        SettleAsync(path, session =>
            session.Write(Avatars, Placed(slot, itemId, avatarUid, facts)));

    /// <summary>The same settlement, run to the refusal its prepare raises and then taken back.</summary>
    /// <remarks>
    /// Prepare and rollback are separate here rather than settled together, because what is under
    /// test is that the failure happens before anything is committed and that taking the
    /// transaction back leaves the world as it was.
    /// </remarks>
    private static async Task<InvalidOperationException> RefusedAsync(
        string path, int slot, int itemId, int avatarUid, InventoryAvatarDetailState facts)
    {
        var transaction = new StubTransaction();
        var session = new SqliteItemDomainStore(path).SessionIn(transaction);
        session.Write(Avatars, Placed(slot, itemId, avatarUid, facts));
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await transaction.PrepareAsync());
        await transaction.RollbackAsync();
        return refusal;
    }

    private static StoredItem Placed(
        int slotIndex, int itemId, int avatarUid, InventoryAvatarDetailState? facts) =>
        new(slotIndex, Core(itemId, avatarUid))
        {
            WorldClass = ItemWorldClass.Avatar,
            AvatarDetail = facts,
        };

    private static async Task SettleAsync(string path, Action<IItemDomainStoreSession> work)
    {
        var transaction = new StubTransaction();
        work(new SqliteItemDomainStore(path).SessionIn(transaction));
        await transaction.SettleAsync();
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
    private static async Task<InventoryStorageItem> OneAvatarAsync(InventorySide side, int slot)
    {
        var loaded = await side.InUseCaseAsync(token => side.CharacterEntries
            .LoadCharacterAsync(new AccountId(Account), new CharacterId(Character), token));
        var item = Assert.Single(
            loaded.CharacterItems,
            value => value.ContainerType == InventoryContainerType.Avatar
                && value.SlotIndex == slot);
        Assert.Equal(AvatarKind, item.Core.ItemKind);
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

    /// <summary>
    /// Stands in for the coordinator, as the store's own tests do.
    /// </summary>
    /// <remarks>
    /// Prepare, rollback and commit are separate here because one of these cases needs a prepare
    /// that throws and a rollback after it, which a single settle cannot express.
    /// </remarks>
    private sealed class StubTransaction : IDomainTransaction
    {
        private readonly Dictionary<object, ITransactionParticipant> enlisted = [];

        public TParticipant Enlist<TParticipant>(object identity, Func<TParticipant> create)
            where TParticipant : ITransactionParticipant
        {
            if (this.enlisted.TryGetValue(identity, out var existing))
            {
                return (TParticipant)existing;
            }

            var participant = create();
            this.enlisted.Add(identity, participant);
            return participant;
        }

        public async Task PrepareAsync()
        {
            foreach (var participant in this.enlisted.Values)
            {
                await participant.PrepareAsync(CancellationToken.None);
            }
        }

        public async Task RollbackAsync()
        {
            foreach (var participant in this.enlisted.Values)
            {
                await participant.RollbackAsync(CancellationToken.None);
            }
        }

        public async Task SettleAsync()
        {
            await this.PrepareAsync();
            foreach (var participant in this.enlisted.Values)
            {
                await participant.CommitAsync(CancellationToken.None);
            }
        }
    }
}
