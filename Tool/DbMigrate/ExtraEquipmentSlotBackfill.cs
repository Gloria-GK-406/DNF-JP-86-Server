using Microsoft.Data.Sqlite;

namespace DbMigrate;

/// <summary>
/// Opens the extra equipment slots of characters that finished the quests granting them before
/// the server honoured <c>[slot expansion]</c> (#308).
/// </summary>
/// <remarks>
/// PVF quest 649 (support equipment) declares slot index 0 and quest 650 (magic stone) index 1;
/// the server keeps one bit per index in <c>characters.ex_equip_slot_stat</c>. A finished quest is
/// a non-zero row of <c>character_invisible_flags</c> keyed by the quest id. Bits are only added,
/// so running it again changes nothing.
/// </remarks>
internal static class ExtraEquipmentSlotBackfill
{
    internal static void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE characters SET ex_equip_slot_stat = (ex_equip_slot_stat | 1)
             WHERE character_id IN (SELECT character_id FROM character_invisible_flags WHERE slot_index = 649 AND flag_value <> 0);
            UPDATE characters SET ex_equip_slot_stat = (ex_equip_slot_stat | 2)
             WHERE character_id IN (SELECT character_id FROM character_invisible_flags WHERE slot_index = 650 AND flag_value <> 0);
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }
}
