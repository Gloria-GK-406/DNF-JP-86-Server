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
/// Whether an avatar's six facts belong to the avatar or to the position it is standing in.
/// </summary>
/// <remarks>
/// The six used to be a row of <c>character_avatar_detail</c> found under a number the item core
/// carried, and they are columns of the world row now. Which of the two they follow is not visible
/// from the column list: a move that rewrote them at the destination would look exactly the same
/// from the outside as a move that left them alone, and both would answer correctly for a single
/// avatar moved on its own.
///
/// So the discriminating case is two avatars trading places. If the facts belonged to the position,
/// the position each of them arrives at would answer with the facts the other one left there. The
/// second test below is that case; the first is the plain one, field by field, so that a change to
/// any single one of the six is caught by name.
///
/// Both read the avatar back through the entry load rather than out of the columns, because the
/// question is what the character is served, and the load is the only thing that turns the columns
/// into that.
/// </remarks>
public sealed class AvatarDetailFollowsTheIdentityTests
{
    private const int Account = 82;

    private const int Character = 91;

    private const byte AvatarKind = 8;

    private const int FirstAvatarId = 2500;

    private const int SecondAvatarId = 2501;

    private static readonly CharacterId Owner = new(Character);

    private static readonly ContainerId Avatars =
        ContainerId.ForCharacter(Owner, InventoryContainerType.Avatar);

    /// <summary>
    /// An avatar moved to another position is served with the same six facts it had before.
    /// </summary>
    /// <remarks>
    /// Every one of the six carries a value of its own, none of them the default, so a move that
    /// dropped or defaulted any single field fails on that field's name rather than on a whole
    /// record being unequal.
    /// </remarks>
    [Fact]
    public async Task AnAvatarKeepsAllSixOfItsFactsFieldByFieldWhenItMoves()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await InsertAvatarAsync(path, slot: 4, itemId: FirstAvatarId, avatarUid: 9001, Facts(1));

            var before = await OneAvatarAsync(side, slot: 4);
            var identity = before.ItemUid;
            Assert.NotNull(identity);

            await MoveAsync(path, from: 4, to: 11, itemId: FirstAvatarId, avatarUid: 9001);

            var after = await OneAvatarAsync(side, slot: 11);
            Assert.Equal(identity, after.ItemUid);

            var expected = before.Snapshot.AvatarDetail;
            var found = after.Snapshot.AvatarDetail;
            Assert.NotNull(expected);
            Assert.NotNull(found);
            Assert.Equal(expected.ExpireDate, found.ExpireDate);
            Assert.Equal(expected.ClearAvatarId, found.ClearAvatarId);
            Assert.Equal(expected.JewelSocket, found.JewelSocket);
            Assert.Equal(expected.Color1, found.Color1);
            Assert.Equal(expected.Color2, found.Color2);
            Assert.Equal(expected.DeleteDate, found.DeleteDate);

            // And the seeded values themselves, so that "unchanged" cannot be satisfied by both
            // reads answering with nothing.
            var seeded = Facts(1);
            Assert.Equal(seeded.ExpireDate, found.ExpireDate);
            Assert.Equal(seeded.ClearAvatarId, found.ClearAvatarId);
            Assert.Equal(seeded.JewelSocket, found.JewelSocket);
            Assert.Equal(seeded.Color1, found.Color1);
            Assert.Equal(seeded.Color2, found.Color2);
            Assert.Equal(seeded.DeleteDate, found.DeleteDate);
        });
    }

    /// <summary>
    /// Two avatars trading positions each arrive carrying their own facts, not the position's.
    /// </summary>
    /// <remarks>
    /// This is the case that tells the two answers apart. Facts belonging to the position would
    /// leave the first position answering with the first avatar's colours after the swap; facts
    /// belonging to the thing arrive with the thing, so the first position answers with the
    /// second avatar's.
    /// </remarks>
    [Fact]
    public async Task TwoAvatarsSwappingPositionsEachCarryTheirOwnFacts()
    {
        await WithASeededCharacterAsync(async (path, side) =>
        {
            await InsertAvatarAsync(path, slot: 4, itemId: FirstAvatarId, avatarUid: 9001, Facts(1));
            await InsertAvatarAsync(path, slot: 5, itemId: SecondAvatarId, avatarUid: 9002, Facts(2));

            var first = await OneAvatarAsync(side, slot: 4);
            var second = await OneAvatarAsync(side, slot: 5);
            Assert.NotNull(first.ItemUid);
            Assert.NotNull(second.ItemUid);
            Assert.NotEqual(first.ItemUid, second.ItemUid);

            await SwapAsync(path);

            var atFour = await OneAvatarAsync(side, slot: 4);
            var atFive = await OneAvatarAsync(side, slot: 5);

            // The identities have traded positions...
            Assert.Equal(second.ItemUid, atFour.ItemUid);
            Assert.Equal(first.ItemUid, atFive.ItemUid);

            // ...and so have the facts, because they are the identity's and not the position's.
            Assert.Equal(Facts(2).Color1, atFour.Snapshot.AvatarDetail!.Color1);
            Assert.Equal(Facts(2).Color2, atFour.Snapshot.AvatarDetail.Color2);
            Assert.Equal(Facts(2).ExpireDate, atFour.Snapshot.AvatarDetail.ExpireDate);
            Assert.Equal(Facts(2).ClearAvatarId, atFour.Snapshot.AvatarDetail.ClearAvatarId);
            Assert.Equal(Facts(2).DeleteDate, atFour.Snapshot.AvatarDetail.DeleteDate);
            Assert.Equal(Facts(2).JewelSocket, atFour.Snapshot.AvatarDetail.JewelSocket);

            Assert.Equal(Facts(1).Color1, atFive.Snapshot.AvatarDetail!.Color1);
            Assert.Equal(Facts(1).Color2, atFive.Snapshot.AvatarDetail.Color2);
            Assert.Equal(Facts(1).ExpireDate, atFive.Snapshot.AvatarDetail.ExpireDate);
            Assert.Equal(Facts(1).ClearAvatarId, atFive.Snapshot.AvatarDetail.ClearAvatarId);
            Assert.Equal(Facts(1).DeleteDate, atFive.Snapshot.AvatarDetail.DeleteDate);
            Assert.Equal(Facts(1).JewelSocket, atFive.Snapshot.AvatarDetail.JewelSocket);
        });
    }

    /// <summary>The six facts of the nth avatar, every one of them distinct and none defaulted.</summary>
    private static InventoryAvatarDetailState Facts(int which) => new(
        ExpireDate: 1_700_000_000 + which,
        ClearAvatarId: 6100 + which,
        JewelSocket: [.. Enumerable.Range(0, 30).Select(index => (byte)(index + which))],
        Color1: (ushort)(1000 + which),
        Color2: (ushort)(2000 + which),
        DeleteDate: 1_800_000_000 + which);

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

    /// <summary>Move the issued item record; neither its identity nor its details are reconstructed.</summary>
    private static Task MoveAsync(string path, int from, int to, int itemId, int avatarUid) =>
        SettleAsync(path, async session =>
        {
            var source = (await session.GetAsync(Avatars, from, default))!;
            Assert.Equal(itemId, source.ItemId);
            Assert.Equal(avatarUid, source.Snapshot.InstanceValue);
            await session.PutAsync(Avatars, to, await session.TakeAsync(source, null, default), default);
        });

    private static Task SwapAsync(string path) =>
        SettleAsync(path, async session =>
        {
            var first = await session.TakeAsync((await session.GetAsync(Avatars, 4, default))!, null, default);
            var second = await session.TakeAsync((await session.GetAsync(Avatars, 5, default))!, null, default);
            await session.PutAsync(Avatars, 5, first, default);
            await session.PutAsync(Avatars, 4, second, default);
        });

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

    /// <summary>The same eighty-two bytes the seeding writes, so a move recognises the row.</summary>
    private static InventoryItemCoreState Core(int itemId, int avatarUid) =>
        InventoryItemCore.Decode(CoreBytes(itemId, avatarUid));

    private static byte[] CoreBytes(int itemId, int avatarUid)
    {
        var core = new byte[82];
        core[0] = AvatarKind;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1), itemId);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5), avatarUid);
        return core;
    }

    private static async Task InsertAvatarAsync(
        string path, int slot, int itemId, int avatarUid, InventoryAvatarDetailState facts)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO items(item_core,
                avatar_expire_date,avatar_clear_id,avatar_jewel_socket,avatar_color1,
                avatar_color2,avatar_delete_date)
            VALUES(@core,@expire,@clear,@socket,@color1,@color2,@delete);
            INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
            VALUES('character',@cid,1,@slot,'world',last_insert_rowid(),last_insert_rowid(),1);
            """;
        command.Parameters.AddWithValue("@cid", Character);
        command.Parameters.AddWithValue("@slot", slot);
        command.Parameters.AddWithValue("@core", CoreBytes(itemId, avatarUid));
        command.Parameters.AddWithValue("@expire", facts.ExpireDate);
        command.Parameters.AddWithValue("@clear", facts.ClearAvatarId);
        command.Parameters.AddWithValue("@socket", facts.JewelSocket);
        command.Parameters.AddWithValue("@color1", facts.Color1);
        command.Parameters.AddWithValue("@color2", facts.Color2);
        command.Parameters.AddWithValue("@delete", facts.DeleteDate);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WithASeededCharacterAsync(Func<string, InventorySide, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var path = Path.Combine(Path.GetTempPath(), $"dfo-avatarid-{Guid.NewGuid():N}.db");
        try
        {
            var side = new InventorySide(path, new AvatarTemplates());
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    INSERT INTO accounts(account_id,m_id) VALUES({Account},'pin');
                    INSERT INTO characters(character_id,account_id,name,job,level)
                    VALUES({Character},{Account},'pin',3,60);
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
