using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Characters;

internal sealed class SqliteCollectionBoxSelectionTable(SqliteDatabase database) : ICollectionBoxSelectionTable
{
    public async ValueTask<int?> ReadAsync(CharacterId character, CancellationToken ct) =>
        (await database.ReadAllAsync("SELECT selected_box_index FROM character_collectbox_state WHERE character_id=@id",
            p => p.AddWithValue("@id", character.Value), r => r.GetInt32(0), ct).ConfigureAwait(false)).Cast<int?>().SingleOrDefault();
    public ValueTask WriteAsync(CharacterId character, byte boxId, CancellationToken ct) => database.WriteAsync(async (command, token) =>
    {
        if (boxId == 0) throw new ArgumentOutOfRangeException(nameof(boxId));
        command.CommandText = "INSERT INTO character_collectbox_state(character_id,selected_box_index) VALUES(@id,@box) "
            + "ON CONFLICT(character_id) DO UPDATE SET selected_box_index=excluded.selected_box_index";
        command.Parameters.AddWithValue("@id", character.Value);
        command.Parameters.AddWithValue("@box", boxId);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }, ct);
}
