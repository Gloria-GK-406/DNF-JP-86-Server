using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Tests.Schema;

/// <summary>
/// The tables a thing lives in, against the shape the ruling settled on.
/// </summary>
/// <remarks>
/// The ruling in <c>Docs/Refactor/TableOwnershipRuling.md</c> decides where every column of the item
/// side goes. What follows is that decision written as assertions, and it exists because nothing
/// else would notice if the move dropped something: a missing column reads as a default, a missing
/// constraint refuses nothing, and neither produces a compiler error or a red test.
///
/// The column lists are the ruling's own, taken from its per-table lists rather than retyped from
/// its prose.
/// </remarks>
public sealed class ItemCarrierShapeTests
{
    /// <summary>items -- the ruling's columns less the four that said where, plus a creature's eight.</summary>
    /// <remarks>
    /// The eight are not the ruling's. They are <c>character_creatures</c>' own columns less its
    /// keys, arriving here because this row is now the creature rather than a row pointing at one.
    /// Named with the same prefix the avatar's carry, for the same reason: a column called
    /// <c>tail_flag</c> on a table of everything would say whose tail nowhere.
    ///
    /// The four that are not here are <c>owner_scope</c>, <c>owner_id</c>, <c>list_type</c> and
    /// <c>slot_index</c>. They said where a thing was, which is the other table's whole subject;
    /// keeping them here as well was what made a move rewrite the thing rather than the position,
    /// and it is the shape this stage retires.
    /// </remarks>
    private static readonly string[] Items =
    [
        "avatar_clear_id", "avatar_color1", "avatar_color2", "avatar_delete_date",
        "avatar_expire_date", "avatar_jewel_socket", "cargo_character_id",
        "created_at", "creature_field04", "creature_field_after_value", "creature_mode1_field0a",
        "creature_mode1_field0b", "creature_mode_flag", "creature_progress_value", "creature_tail_flag",
        "creature_text", "item_core", "item_uid", "updated_at",
    ];

    /// <summary>item_slots -- 8 columns, and the same eight whatever the slot holds.</summary>
    private static readonly string[] ItemSlots =
    [
        "core_uid", "count", "id", "kind", "list_type", "owner_id", "owner_scope", "slot_index",
    ];

    /// <summary>character_rentals -- 4 columns.</summary>
    private static readonly string[] CharacterRentals =
    [
        "character_id", "expire_time", "inventory_template_id", "item_id",
    ];

    /// <summary>character_item_cooldowns -- 4 columns.</summary>
    private static readonly string[] CharacterItemCooldowns =
    [
        "character_id", "item_id", "sort_order", "value",
    ];

    /// <summary>character_item_effects -- 4 columns.</summary>
    private static readonly string[] CharacterItemEffects =
    [
        "character_id", "item_id", "sort_order", "value",
    ];

    /// <summary>The nine tables whose columns move into the four above.</summary>
    /// <remarks>
    /// Four of them held a thing depending on who was carrying it; two held the same rental concept
    /// twice; one decided what its neighbour meant; and two are the previous generation, kept alive
    /// by nothing but an emptiness probe.
    /// </remarks>
    private static readonly string[] Retired =
    [
        "character_new_items", "account_cargo_new_items", "character_new_titlebook",
        "character_avatar_detail", "character_rental_items", "rental_items",
        "character_item_values", "character_items", "account_cargo_items",
    ];

    [Theory]
    [MemberData(nameof(Carriers))]
    public async Task AnItemSideTableHasExactlyTheColumnsTheRulingGivesIt(
        string table, string[] expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var shape = await ShapeAsync();

        Assert.True(shape.ContainsKey(table), $"{table} is not in the database at all.");
        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), shape[table]);
    }

    public static TheoryData<string, string[]> Carriers() => new()
    {
        { "items", Items },
        { "item_slots", ItemSlots },
        { "character_rentals", CharacterRentals },
        { "character_item_cooldowns", CharacterItemCooldowns },
        { "character_item_effects", CharacterItemEffects },
    };

    [Fact]
    public async Task TheTablesWhoseColumnsMovedAreGone()
    {
        var shape = await ShapeAsync();

        Assert.DoesNotContain(Retired, shape.ContainsKey);
    }

    /// <remarks>
    /// Every constraint the four source tables of <c>items</c> declared has to arrive somewhere,
    /// and two of them changed shape on the way: the title book's core check was an upper bound and
    /// became exact, and the avatar's jewel socket was unconditional and became conditional,
    /// because most rows of this table are not avatars.
    ///
    /// Two of them are no longer on this table, and both are checked where they went rather than
    /// dropped: the uniqueness over a position and the check on the owner's scope are the slot
    /// table's, asserted by <see cref="TheSlotTableKeepsThePositionUniqueAndHoldsEachKindToItsCount"/>.
    /// This one asserts they are not here as well, because two tables holding the same fact is how
    /// they come to disagree.
    /// </remarks>
    [Fact]
    public async Task ItemsCarriesEveryConstraintItsSourcesDeclared()
    {
        var sql = Squeeze(await DeclarationAsync("items"));

        Assert.Contains("item_uid INTEGER PRIMARY KEY AUTOINCREMENT", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "UNIQUE(owner_scope, owner_id, list_type, slot_index)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("owner_scope", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (length(item_core) = 82)", sql, StringComparison.Ordinal);
        Assert.Contains(
            "CHECK (avatar_jewel_socket IS NULL OR length(avatar_jewel_socket) = 30)",
            sql,
            StringComparison.Ordinal);
    }

    /// <remarks>
    /// A creature's identity goes on the wire as a signed 32-bit integer, and the identity it goes
    /// as is this column. A database able to allocate a larger one would be able to hand out an
    /// identity the server cannot say -- so the bound is on the column that allocates them, which
    /// is the only place one is ever made.
    /// </remarks>
    [Fact]
    public async Task TheWorldIdentityCannotLeaveWhatTheWireCanSay()
    {
        var sql = Squeeze(await DeclarationAsync("items"));

        Assert.Contains(
            "CONSTRAINT world_identity_fits_the_wire CHECK (item_uid BETWEEN 1 AND 2147483647)",
            sql,
            StringComparison.Ordinal);
    }

    /// <remarks>
    /// The slot table keeps the uniqueness the item table had, because it is the same fact: one
    /// position holds one thing. What it no longer keeps is the payload, which is why settlement
    /// may go on destroying and rebuilding a row of it without anything being lost.
    ///
    /// The other three say what the kind means for the rest of the row. A world row is one thing,
    /// so a slot naming one holds exactly one of it and names that very row as the one its bytes
    /// are on; a template slot holds a pile, and a pile of none is an empty slot rather than a row
    /// saying nothing.
    /// </remarks>
    [Fact]
    public async Task TheSlotTableKeepsThePositionUniqueAndHoldsEachKindToItsCount()
    {
        var sql = Squeeze(await DeclarationAsync("item_slots"));

        Assert.Contains(
            "UNIQUE(owner_scope, owner_id, list_type, slot_index)", sql, StringComparison.Ordinal);
        Assert.Contains(
            "CHECK (owner_scope IN ('character', 'account'))", sql, StringComparison.Ordinal);
        Assert.Contains(
            "CONSTRAINT slot_count_is_positive CHECK (count > 0)", sql, StringComparison.Ordinal);
        Assert.Contains(
            "CONSTRAINT world_slot_holds_exactly_one CHECK (kind <> 'world' OR count = 1)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CONSTRAINT world_slot_names_its_own_row CHECK (kind <> 'world' OR core_uid = id)",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("CHECK (kind IN ('world', 'template'))", sql, StringComparison.Ordinal);
    }

    /// <remarks>
    /// The strictest of the sources wins where they disagree. A composite primary key does not
    /// imply <c>NOT NULL</c> in SQLite, so the title book's three key columns arrive nullable
    /// unless this is stated outright.
    ///
    /// Four of the five are the slot table's columns now, and the requirement travelled with them
    /// unchanged: a position that left out which container or which slot would be a position that
    /// says nothing, whichever table it is a row of.
    /// </remarks>
    [Theory]
    [InlineData("item_slots", "owner_id")]
    [InlineData("item_slots", "list_type")]
    [InlineData("item_slots", "slot_index")]
    [InlineData("item_slots", "owner_scope")]
    [InlineData("item_slots", "core_uid")]
    [InlineData("items", "item_core")]
    public async Task AnItemColumnWithDisagreeingSourcesTakesTheStrictest(
        string table, string column)
    {
        var sql = Squeeze(await DeclarationAsync(table));
        var start = sql.IndexOf(column + " ", StringComparison.Ordinal);

        Assert.True(start >= 0, $"{table} has no column named {column}.");
        Assert.Contains("NOT NULL", DeclarationOf(sql, start), StringComparison.Ordinal);
    }

    /// <remarks>
    /// The avatar columns are sparse: only an item of kind 8 has them. A default of zero on a
    /// sparse column does not say absent, it says zero -- and the encoder tells the two apart.
    /// </remarks>
    [Theory]
    [InlineData("avatar_expire_date")]
    [InlineData("avatar_clear_id")]
    [InlineData("avatar_jewel_socket")]
    [InlineData("avatar_color1")]
    [InlineData("avatar_color2")]
    [InlineData("avatar_delete_date")]
    [InlineData("cargo_character_id")]
    [InlineData("creature_field04")]
    [InlineData("creature_mode_flag")]
    [InlineData("creature_progress_value")]
    [InlineData("creature_mode1_field0a")]
    [InlineData("creature_mode1_field0b")]
    [InlineData("creature_field_after_value")]
    [InlineData("creature_text")]
    [InlineData("creature_tail_flag")]
    public async Task ASparseItemColumnIsNullableAndHasNoDefault(string column)
    {
        var sql = Squeeze(await DeclarationAsync("items"));
        var start = sql.IndexOf(column + " ", StringComparison.Ordinal);

        Assert.True(start >= 0, $"items has no column named {column}.");
        var declaration = DeclarationOf(sql, start);
        Assert.DoesNotContain("NOT NULL", declaration, StringComparison.Ordinal);
        Assert.DoesNotContain("DEFAULT", declaration, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One column's declaration, which ends at the next one or at the table's own end.</summary>
    private static string DeclarationOf(string sql, int start)
    {
        var comma = sql.IndexOf(',', start);
        var close = sql.LastIndexOf(')');
        return sql[start..(comma >= 0 && comma < close ? comma : close)];
    }

    [Theory]
    [InlineData("character_rentals", "PRIMARY KEY (character_id, item_id)")]
    [InlineData("character_item_cooldowns", "PRIMARY KEY (character_id, sort_order)")]
    [InlineData("character_item_effects", "PRIMARY KEY (character_id, sort_order)")]
    public async Task AnItemSideTableIsKeyedTheWayTheRulingSays(string table, string key)
    {
        var sql = Squeeze(await DeclarationAsync(table));

        Assert.Contains(key, sql, StringComparison.Ordinal);
    }

    /// <remarks>
    /// The split puts the answer in the table name. A column whose value decides what another
    /// column means is the shape the red line forbids, and this was the clearest instance in the
    /// database.
    /// </remarks>
    [Theory]
    [InlineData("character_item_cooldowns")]
    [InlineData("character_item_effects")]
    public async Task NeitherHalfOfTheSplitKeepsTheColumnThatDecidedWhatTheOtherMeant(string table)
    {
        var shape = await ShapeAsync();

        Assert.DoesNotContain("list_kind", shape[table]);
    }

    /// <remarks>
    /// The detail table was keyed on the avatar UID, which says: at most one of these anywhere, not
    /// per character. That rule is still enforced and still tested: the lease that claims a UID
    /// another character holds is built by
    /// <c>DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision</c>,
    /// which reads the refusal. What has gone is the rule being a key of the table. D-15 stored
    /// the UID beside the core so an index could name it; the two writes that own the rule ask the
    /// core instead, and the column and its index are gone. What is asserted here is that nothing
    /// took their place.
    /// </remarks>
    [Fact]
    public async Task TheAvatarsUidIsNoLongerAKeyOfTheTable()
    {
        var shape = await ShapeAsync();
        var indexes = await IndexesAsync();

        Assert.DoesNotContain("avatar_uid", shape["items"]);
        Assert.DoesNotContain("idx_items_avatar_uid", indexes.Keys);
        Assert.DoesNotContain(
            indexes,
            entry => entry.Value.Contains("avatar", StringComparison.OrdinalIgnoreCase));
    }

    /// <remarks>
    /// Each of the six declared indexes has a stated fate in the ruling rather than a rule to infer
    /// one from. Four name a retired table and go with it; one was rebuilt on the column that
    /// replaced the one it named.
    ///
    /// The sixth answered "this owner's things by position" against <c>items</c>, and it is gone
    /// rather than rebuilt: those columns are the slot table's, that table already holds them
    /// unique, and every read of it names the owner's scope first -- so the constraint's own index
    /// answers what this one used to. What did have to be added is the other direction, from a row
    /// back to the position naming it, which is how a row nothing holds is found.
    /// </remarks>
    [Fact]
    public async Task EveryIndexThatNamedARetiredTableIsGoneAndTheOneThatMovedWasRebuilt()
    {
        var indexes = await IndexesAsync();

        Assert.DoesNotContain("idx_account_cargo_items_owner", indexes.Keys);
        Assert.DoesNotContain("idx_character_items_character", indexes.Keys);
        Assert.DoesNotContain("idx_character_new_items_character_space", indexes.Keys);
        Assert.DoesNotContain("idx_character_rental_items_char", indexes.Keys);
        Assert.DoesNotContain("idx_rental_items_char", indexes.Keys);
        Assert.DoesNotContain("idx_items_owner_space", indexes.Keys);

        Assert.Contains("idx_characters_account", indexes.Keys);
        Assert.Contains(
            indexes,
            entry => entry.Value.Contains("ON item_slots(core_uid)", StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ShapeAsync() =>
        await WithAFreshDatabaseAsync(async connection =>
        {
            var tables = new List<string>();
            await using (var names = connection.CreateCommand())
            {
                names.CommandText =
                    "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
                await using var reader = await names.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            var shape = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var table in tables)
            {
                var columns = new List<string>();
                await using var info = connection.CreateCommand();
                info.CommandText = $"PRAGMA table_info('{table}')";
                await using var reader = await info.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columns.Add(reader.GetString(1));
                }

                columns.Sort(StringComparer.Ordinal);
                shape[table] = columns;
            }

            return (IReadOnlyDictionary<string, IReadOnlyList<string>>)shape;
        });

    private static async Task<IReadOnlyDictionary<string, string>> IndexesAsync() =>
        await WithAFreshDatabaseAsync(async connection =>
        {
            var indexes = new Dictionary<string, string>(StringComparer.Ordinal);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT name, COALESCE(sql, '') FROM sqlite_master "
                + "WHERE type='index' AND name NOT LIKE 'sqlite_%'";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                indexes[reader.GetString(0)] = Squeeze(reader.GetString(1));
            }

            return (IReadOnlyDictionary<string, string>)indexes;
        });

    private static async Task<string> DeclarationAsync(string table) =>
        await WithAFreshDatabaseAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=@name";
            command.Parameters.AddWithValue("@name", table);

            return await command.ExecuteScalarAsync() as string
                ?? throw new InvalidOperationException($"{table} is not in the database at all.");
        });

    /// <summary>Whitespace made uniform, so an assertion is about the text and not its layout.</summary>
    private static string Squeeze(string sql) =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static async Task<T> WithAFreshDatabaseAsync<T>(
        Func<SqliteConnection, Task<T>> read)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-items-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteDatabase(path, new DomainTransactionScope());

            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            await connection.OpenAsync();

            return await read(connection);
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
}
