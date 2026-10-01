using System.Buffers.Binary;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Inventory;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Inventory;

/// <summary>
/// Which creature entry revives, when the creature's own core says a different number.
/// </summary>
/// <remarks>
/// Entry revives the creature the character is wearing if it is down, and it has to find that
/// creature's condition in order to do so. It used to find it by the value inside the worn item's
/// core -- a number that meant "the creature key" back when a creature was a row of its own under a
/// key of its own, and that means nothing of the sort now: a creature is a world row, its condition
/// is a column of that row, and the key is the row's identity.
/// <para>
/// Nothing ever made the two agree. Where they happened to, entry worked; where they did not, the
/// lookup found no creature and entry returned quietly without reviving anything -- no exception,
/// no log, a player whose creature stays down. So the seeding here makes them differ on purpose:
/// the worn creature's identity is one number and the value in its core is another, and the test
/// says which of the two the revival is addressed by.
/// </para>
/// <para>
/// The frame's side of it is asserted too. The list the client is sent names each creature by its
/// identity, so a revival addressed by anything else would either miss, or -- if it hit -- name a
/// creature the list does not contain, which is the one case the service refuses outright.
/// </para>
/// </remarks>
public sealed class CreatureRevivalAddressesByIdentityTests
{
    private const int Account = 91;

    private const int Character = 92;

    /// <summary>The equipment slot a worn creature stands in. Not this outcome's to decide.</summary>
    private const int WornSlot = 25;

    /// <summary>What an item core says it is when it is a creature.</summary>
    private const byte CreatureKind = 5;

    /// <summary>The identity the creature's world row is given.</summary>
    private const int Identity = 31;

    /// <summary>
    /// What the creature's core carries in its value, which is deliberately not its identity.
    /// </summary>
    private const int CoreValue = 4242;

    [Fact]
    public async Task TheWornCreatureIsFoundByItsIdentityRatherThanByTheValueInItsCore()
    {
        await WithAWornDownCreatureAsync(async (scope, path) =>
        {
            var revival = await ReviveAsync(scope, path, listed: Identity);

            Assert.NotNull(revival);
            Assert.Equal(Identity, revival.State.CreatureKey);
            Assert.Equal(1, revival.State.StateValue);
            Assert.Equal(1, await ConditionAsync(path));
            Assert.Equal(
                1,
                Assert.Single(revival.Result.CharacterEntry.Features.Pets).Field04);
        });
    }

    /// <summary>
    /// The value inside the core is not a creature anybody can be revived by.
    /// </summary>
    /// <remarks>
    /// The other half of the same statement. A list naming the creature by what its core carries is
    /// a list that does not name the creature standing in the slot, and the service refuses that
    /// rather than reviving something and reporting it under a number the client cannot match.
    /// </remarks>
    [Fact]
    public async Task ACreatureListNamingTheCoresValueInsteadIsRefused()
    {
        await WithAWornDownCreatureAsync(async (scope, path) =>
        {
            var refused = await Assert.ThrowsAsync<InvalidDataException>(
                async () => await ReviveAsync(scope, path, listed: CoreValue));

            Assert.Contains("absent", refused.Message, StringComparison.Ordinal);
        });
    }

    private static async Task<SelectCharacterPetRevival?> ReviveAsync(
        DomainTransactionScope scope, string path, int listed)
    {
        await using var lease = scope.Begin();
        var service = new SelectCharacterPetEntryService(
            new SqliteCharacterDomainStore(path),
            new SqliteItemDomainStore(path),
            new AmbientStoreAccess(scope));
        var revival = await service.ReviveAsync(
            Session(), Prepared(listed), CancellationToken.None);
        await lease.CommitAsync(CancellationToken.None);
        return revival;
    }

    private static SelectedSessionIdentity Session() =>
        SelectedSessionIdentity.Create(
            new AccountId(Account),
            new CharacterId(Character),
            new NetworkConnectionId("Game:revive"),
            1,
            10021);

    /// <summary>
    /// The entry state the revival is handed, holding nothing but the creature list it reads.
    /// </summary>
    /// <remarks>
    /// Everything else the result carries is untouched by this service -- it reads the creature
    /// list and rebuilds the result around it -- so the rest is left unmade rather than built into
    /// a plausible-looking character nothing here looks at.
    /// </remarks>
    private static SelectCharacterResult Prepared(int listed) =>
        new(
            null!,
            new CharacterEntryState(
                new CharacterId(Character),
                null!,
                null!,
                null!,
                null!,
                null!,
                new CharacterFeaturesEntryState(
                    [new CharacterPetRuntimeState(listed, 0, 0, 0, 0, 0, 0, [], 0)],
                    null!,
                    null!,
                    null!,
                    [])),
            null!,
            null!,
            null!,
            null!,
            null!);

    /// <summary>The condition column of the creature's own world row.</summary>
    private static async Task<long?> ConditionAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT creature_field04 FROM items WHERE item_uid=@uid";
        command.Parameters.AddWithValue("@uid", Identity);
        return await command.ExecuteScalarAsync() is long found ? found : null;
    }

    private static async Task WithAWornDownCreatureAsync(
        Func<DomainTransactionScope, string, Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var path = Path.Combine(Path.GetTempPath(), $"dfo-revive-{Guid.NewGuid():N}.db");
        try
        {
            var side = new Support.CharacterSide(path);
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    INSERT INTO accounts(account_id,m_id) VALUES({Account},'revive');
                    INSERT INTO characters(character_id,account_id,name)
                    VALUES({Character},{Account},'revive');
                    INSERT INTO items(item_uid,item_core,creature_field04,creature_mode_flag,
                        creature_progress_value,creature_mode1_field0a,creature_mode1_field0b,
                        creature_field_after_value,creature_text,creature_tail_flag)
                    VALUES({Identity},@core,0,1,2,3,4,5,X'06',7);
                    INSERT INTO item_slots(
                        owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count)
                    VALUES('character',{Character},
                        {(int)InventoryContainerType.Equipment},{WornSlot},
                        'world',{Identity},{Identity},1);
                    """;
                command.Parameters.AddWithValue("@core", Core());
                await command.ExecuteNonQueryAsync();
            }

            await body(side.Scope, path);
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

    /// <summary>A creature's core, carrying a value that is not the row's identity.</summary>
    private static byte[] Core()
    {
        var core = new byte[82];
        core[0] = CreatureKind;
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(1), 63000);
        BinaryPrimitives.WriteInt32LittleEndian(core.AsSpan(5), CoreValue);
        return core;
    }
}
