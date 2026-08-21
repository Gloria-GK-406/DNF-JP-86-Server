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
    /// <summary>items -- 16 columns.</summary>
    private static readonly string[] Items =
    [
        "avatar_clear_id", "avatar_color1", "avatar_color2", "avatar_delete_date",
        "avatar_expire_date", "avatar_jewel_socket", "avatar_uid", "cargo_character_id",
        "created_at", "item_core", "item_uid", "list_type", "owner_id", "owner_scope",
        "slot_index", "updated_at",
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
    /// Every constraint the four source tables of <c>items</c> declared has to arrive here, and two
    /// of them change shape on the way: the title book's core check was an upper bound and becomes
    /// exact, and the avatar's jewel socket was unconditional and becomes conditional, because most
    /// rows of this table are not avatars.
    /// </remarks>
    [Fact]
    public async Task ItemsCarriesEveryConstraintItsSourcesDeclared()
    {
        var sql = Squeeze(await DeclarationAsync("items"));

        Assert.Contains("item_uid INTEGER PRIMARY KEY AUTOINCREMENT", sql, StringComparison.Ordinal);
        Assert.Contains(
            "UNIQUE(owner_scope, owner_id, list_type, slot_index)", sql, StringComparison.Ordinal);
        Assert.Contains(
            "CHECK (owner_scope IN ('character', 'account'))", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (length(item_core) = 82)", sql, StringComparison.Ordinal);
        Assert.Contains(
            "CHECK (avatar_jewel_socket IS NULL OR length(avatar_jewel_socket) = 30)",
            sql,
            StringComparison.Ordinal);
    }

    /// <remarks>
    /// The strictest of the sources wins where they disagree. A composite primary key does not
    /// imply <c>NOT NULL</c> in SQLite, so the title book's three key columns arrive nullable
    /// unless this is stated outright.
    /// </remarks>
    [Theory]
    [InlineData("owner_id")]
    [InlineData("list_type")]
    [InlineData("slot_index")]
    [InlineData("item_core")]
    [InlineData("owner_scope")]
    public async Task AnItemColumnWithDisagreeingSourcesTakesTheStrictest(string column)
    {
        var sql = Squeeze(await DeclarationAsync("items"));
        var start = sql.IndexOf(column + " ", StringComparison.Ordinal);

        Assert.True(start >= 0, $"items has no column named {column}.");
        var declaration = sql[start..sql.IndexOf(',', start)];
        Assert.Contains("NOT NULL", declaration, StringComparison.Ordinal);
    }

    /// <remarks>
    /// The avatar columns are sparse: only an item of kind 8 has them. A default of zero on a
    /// sparse column does not say absent, it says zero -- and the encoder tells the two apart.
    /// </remarks>
    [Theory]
    [InlineData("avatar_uid")]
    [InlineData("avatar_expire_date")]
    [InlineData("avatar_clear_id")]
    [InlineData("avatar_jewel_socket")]
    [InlineData("avatar_color1")]
    [InlineData("avatar_color2")]
    [InlineData("avatar_delete_date")]
    [InlineData("cargo_character_id")]
    public async Task ASparseItemColumnIsNullableAndHasNoDefault(string column)
    {
        var sql = Squeeze(await DeclarationAsync("items"));
        var start = sql.IndexOf(column + " ", StringComparison.Ordinal);

        Assert.True(start >= 0, $"items has no column named {column}.");
        var declaration = sql[start..sql.IndexOf(',', start)];
        Assert.DoesNotContain("NOT NULL", declaration, StringComparison.Ordinal);
        Assert.DoesNotContain("DEFAULT", declaration, StringComparison.OrdinalIgnoreCase);
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
    /// The cargo declared <c>UNIQUE(account_id, slot_index)</c>, which the merged table's four-part
    /// constraint weakens for account rows. One partial unique index takes it back to exactly
    /// today's strength -- see D-16 in the ruling.
    /// </remarks>
    [Fact]
    public async Task TheCargosOwnSlotUniquenessIsRestoredByAPartialIndex()
    {
        var indexes = await IndexesAsync();

        Assert.True(
            indexes.TryGetValue("idx_items_account_slot", out var sql),
            "The cargo's slot uniqueness has no successor index.");
        Assert.Contains("UNIQUE", sql!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("owner_id, slot_index", sql!, StringComparison.Ordinal);
        Assert.Contains("WHERE owner_scope = 'account'", sql!, StringComparison.Ordinal);
    }

    /// <remarks>
    /// The detail table was keyed on the avatar UID, which says: at most one of these anywhere, not
    /// per character. A lease that tries to claim a UID another character already holds is refused
    /// today and rolled back, and a test builds exactly that. A value inside a BLOB cannot be
    /// indexed, so the UID is stored beside the core and indexed there -- see D-15 in the ruling.
    /// </remarks>
    [Fact]
    public async Task TheAvatarDetailsPrimaryKeyIsRestoredByAPartialIndex()
    {
        var indexes = await IndexesAsync();

        Assert.True(
            indexes.TryGetValue("idx_items_avatar_uid", out var sql),
            "The avatar detail's primary key has no successor index.");
        Assert.Contains("UNIQUE", sql!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ON items(avatar_uid)", sql!, StringComparison.Ordinal);
        Assert.Contains("WHERE avatar_uid IS NOT NULL", sql!, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Each of the six declared indexes has a stated fate in the ruling rather than a rule to infer
    /// one from. Four name a retired table and go with it; one is rebuilt on the column that
    /// replaced the one it named.
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

        Assert.Contains("idx_characters_account", indexes.Keys);
        Assert.Contains(
            indexes,
            entry => entry.Value.Contains("ON items(owner_id, list_type, slot_index)",
                StringComparison.Ordinal));
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
