using Dfo.Server.Testing;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// A character's keys and options, asked of the service that answers them, over a real database.
/// </summary>
/// <remarks>
/// The answer is assembled out of two tables and three columns, so the way to get it wrong is
/// silently: read the neighbouring column, read the wrong table, or put one value in another's
/// field. None of that shows up against a row whose values could stand in for one another.
///
/// So the row below is written with values that cannot: the three hotkey slots are three numbers
/// none of which is any other, the option block is bytes no column of numbers reads as, and the
/// visibility bits are larger than a hotkey slot can hold. Any value landing in the wrong field
/// moves an assertion, and the failure names which one.
///
/// A real database rather than a stand-in for the two tables, because what is being checked is
/// that what the service answers is what is stored -- and a stand-in would be answering with what
/// the test handed it.
/// </remarks>
public sealed class CharacterSettingsTests
{
    private static readonly CharacterId Who = new(70);

    /// <summary>Three slots, three numbers, in the order their slot indices put them.</summary>
    private static readonly ushort[] Slots = [4113, 8226, 12339];

    /// <summary>Bytes, so a number read into this field cannot come back looking right.</summary>
    private static readonly byte[] OptionBlock = [0x5B, 0x6C, 0x7D, 0x8E];

    /// <summary>Larger than a hotkey slot holds, so it cannot pass for one.</summary>
    private const long VisibilityBits = 1234567;

    [Fact]
    public async Task WhatIsAnsweredIsWhatTheRowsHold()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSettingsAsync(Who, OptionBlock, VisibilityBits);
        // Written out of order on purpose: the order of the answer is the slot index's, not the
        // order the rows happened to be inserted in.
        await fixture.SeedHotkeysAsync(Who, (2, Slots[2]), (0, Slots[0]), (1, Slots[1]));

        var settings = await fixture.Settings.FindAsync(Who, CancellationToken.None);

        Assert.NotNull(settings);
        Assert.Equal(Slots, settings.HotkeySlots);
        Assert.Equal(OptionBlock, settings.OptionBitmap);
        Assert.Equal(VisibilityBits, settings.VisibilityBits);
    }

    /// <remarks>
    /// The settings row is what a character has; a character with none is not one this server can
    /// answer for. Answering an empty set of keys instead would describe somebody who is not here.
    /// </remarks>
    [Fact]
    public async Task ACharacterWithNoSettingsRowIsAnsweredWithNothing()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.Null(await fixture.Settings.FindAsync(Who, CancellationToken.None));
    }

    /// <remarks>
    /// A character who has a settings row and no hotkey rows has never had keys stored, which is
    /// not the same as not being there -- so the answer is a settings record with no slots in it.
    /// </remarks>
    [Fact]
    public async Task ACharacterWithNoHotkeyRowsIsStillAnsweredForWithNoSlots()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSettingsAsync(Who, OptionBlock, VisibilityBits);

        var settings = await fixture.Settings.FindAsync(Who, CancellationToken.None);

        Assert.NotNull(settings);
        Assert.Empty(settings.HotkeySlots);
        Assert.Equal(OptionBlock, settings.OptionBitmap);
    }

    /// <remarks>
    /// Nothing and nothing-at-all are different here, and the reader underneath keeps them apart:
    /// a character with no option block stored answers null, and one with an empty block answers
    /// an empty block. A service that collapsed the two would pass every assertion above.
    /// </remarks>
    [Fact]
    public async Task AnOptionBlockNeverStoredAndAnEmptyOneAreDifferentAnswers()
    {
        var never = new CharacterId(71);
        var empty = new CharacterId(72);
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSettingsAsync(never, optionBlock: null, VisibilityBits);
        await fixture.SeedSettingsAsync(empty, optionBlock: [], VisibilityBits);

        var neverStored = await fixture.Settings.FindAsync(never, CancellationToken.None);
        var emptyStored = await fixture.Settings.FindAsync(empty, CancellationToken.None);

        Assert.NotNull(neverStored);
        Assert.Null(neverStored.OptionBitmap);
        Assert.NotNull(emptyStored);
        Assert.Empty(emptyStored.OptionBitmap!);
    }

    /// <summary>One database, wired the way the server wires this service.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string path)
        {
            this.Path = path;
            var database = new SqliteDatabase(path, TestRequired.TransactionScope());
            this.Settings = new CharacterSettingsDomainService(
                new SqliteCharacterSettingsTable(database),
                new SqliteCharacterHotkeyTable(database));
        }

        public string Path { get; }

        public ICharacterSettingsDomainService Settings { get; }

        public static Task<Fixture> CreateAsync()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-charsettings-{Guid.NewGuid():N}.db");
            // The database owns the schema; this only puts rows in it.
            return Task.FromResult(new Fixture(path));
        }

        /// <summary>Puts the settings row there directly: no verb in this server writes one.</summary>
        public async Task SeedSettingsAsync(
            CharacterId characterId, byte[]? optionBlock, long visibilityBits)
        {
            await using var connection = Open(this.Path);
            await using var character = connection.CreateCommand();
            character.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(@cid,7,'settings')";
            character.Parameters.AddWithValue("@cid", characterId.Value);
            await character.ExecuteNonQueryAsync();

            await using var settings = connection.CreateCommand();
            settings.CommandText =
                "INSERT INTO character_settings(character_id,character_option_blob,user_state_bits) "
                + "VALUES(@cid,@blob,@bits)";
            settings.Parameters.AddWithValue("@cid", characterId.Value);
            settings.Parameters.AddWithValue("@blob", (object?)optionBlock ?? DBNull.Value);
            settings.Parameters.AddWithValue("@bits", visibilityBits);
            await settings.ExecuteNonQueryAsync();
        }

        /// <summary>Puts hotkey rows there directly: no verb in this server writes one either.</summary>
        public async Task SeedHotkeysAsync(
            CharacterId characterId, params (int SlotIndex, int Value)[] rows)
        {
            await using var connection = Open(this.Path);
            foreach (var (slotIndex, value) in rows)
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO character_hotkey_slots(character_id,slot_index,hotkey_value) "
                    + "VALUES(@cid,@slot,@value)";
                command.Parameters.AddWithValue("@cid", characterId.Value);
                command.Parameters.AddWithValue("@slot", slotIndex);
                command.Parameters.AddWithValue("@value", value);
                await command.ExecuteNonQueryAsync();
            }
        }

        private static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection(
                SqliteTestPools.ConnectionString(path));
            connection.Open();
            return connection;
        }

        public ValueTask DisposeAsync()
        {
            SqliteTestPools.ClearOwned(this.Path);
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
