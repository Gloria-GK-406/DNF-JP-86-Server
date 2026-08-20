using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Schema;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Characters;

internal sealed class SqliteCharacterPvpSkillProfileRepository : ICharacterPvpSkillProfileRepository
{
    private readonly string connectionString;

    public SqliteCharacterPvpSkillProfileRepository(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
        CharacterDatabaseSchema.Ensure(connectionString);
    }

    public async ValueTask<CharacterSkillsEntryState> LoadOrInitializeAsync(
        AccountId accountId, CharacterId characterId, CharacterSkillsEntryState initialProfile,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var owner = connection.CreateCommand();
        owner.CommandText = "SELECT account_id FROM characters WHERE character_id=@cid AND delete_flag=0";
        owner.Parameters.AddWithValue("@cid", characterId.Value);
        var storedOwner = await owner.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (storedOwner is null || Convert.ToInt32(storedOwner) != accountId.Value)
            throw new InvalidDataException("PvP skill profile owner does not match the account.");

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var initialized = connection.CreateCommand(); initialized.Transaction = transaction;
        initialized.CommandText = "SELECT 1 FROM character_pvp_skill_state WHERE character_id=@cid LIMIT 1";
        initialized.Parameters.AddWithValue("@cid", characterId.Value);
        if (await initialized.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null)
        {
            foreach (var page in initialProfile.Pages)
            foreach (var skill in page.Skills)
            {
                await using var insert = connection.CreateCommand(); insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO character_pvp_skills(character_id,page_index,slot,skill_id,level,extra_values)
                    VALUES(@cid,@page,@slot,@skill,@level,@extra)
                    """;
                insert.Parameters.AddWithValue("@cid", characterId.Value); insert.Parameters.AddWithValue("@page", page.PageIndex);
                insert.Parameters.AddWithValue("@slot", skill.Slot); insert.Parameters.AddWithValue("@skill", skill.SkillId);
                insert.Parameters.AddWithValue("@level", skill.Level);
                insert.Parameters.AddWithValue("@extra", skill.ExtraValues.Count > 0 ? skill.ExtraValues.ToArray() : DBNull.Value);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await using var mark = connection.CreateCommand(); mark.Transaction = transaction;
            mark.CommandText = "INSERT INTO character_pvp_skill_state(character_id) VALUES(@cid)";
            mark.Parameters.AddWithValue("@cid", characterId.Value);
            await mark.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var pages = new[] { new List<CharacterSkillState>(), new List<CharacterSkillState>() };
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            SELECT page_index, slot, skill_id, level, extra_values
            FROM character_pvp_skills WHERE character_id=@cid ORDER BY page_index, slot
            """;
        command.Parameters.AddWithValue("@cid", characterId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var page = reader.GetInt32(0);
            var slot = reader.GetInt32(1);
            var skillId = reader.GetInt32(2);
            var level = reader.GetInt32(3);
            if (page is < 0 or > 1 || slot is < short.MinValue or > short.MaxValue
                || skillId is < 0 or > ushort.MaxValue || level is < 0 or > byte.MaxValue)
                throw new InvalidDataException("PvP skill profile contains an out-of-range row.");
            var extra = reader.IsDBNull(4) ? [] : (byte[])reader.GetValue(4);
            pages[page].Add(new CharacterSkillState(
                checked((short)slot), checked((ushort)skillId), checked((byte)level), extra));
        }
        await reader.DisposeAsync().ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        return new CharacterSkillsEntryState(
            [new CharacterSkillPageState(0, pages[0]), new CharacterSkillPageState(1, pages[1])],
            [], new CharacterSkillPointBasisState(0, 0), [], null,
            new CharacterSkillProtocolMirrorState(
                ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue));
    }
}
