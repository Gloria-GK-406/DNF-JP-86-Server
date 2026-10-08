using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Persistence.Achievements;

internal sealed class SqliteAchievementPersistence(SqliteDatabase database) : IAchievementPersistence, ITitleBookPersistence
{
    public ValueTask<IReadOnlyList<AchievementProgress>> ReadAsync(CharacterId character, CancellationToken ct) =>
        database.ReadAllAsync("SELECT achievement_id,p1,p2,p3,p4 FROM character_achievement_complete WHERE character_id=@cid ORDER BY sort_order",
            p => p.AddWithValue("@cid", character.Value), r => new AchievementProgress(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4),
                r.GetInt32(1) == 0 && r.GetInt32(2) == 0 && r.GetInt32(3) == 0), ct);
    public async ValueTask EnsureAsync(CharacterId character, int achievementId, int remaining, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT OR IGNORE INTO character_achievement_complete(character_id,sort_order,achievement_id,p1,p2,p3,p4)
            VALUES(@cid,(SELECT COALESCE(MAX(sort_order),-1)+1 FROM character_achievement_complete WHERE character_id=@cid),@aid,@remaining,0,0,0)
            """, p => { p.AddWithValue("@cid", character.Value); p.AddWithValue("@aid", achievementId); p.AddWithValue("@remaining", remaining); }, ct);
    public async ValueTask<bool> TryConsumeFactAsync(CharacterId character, Guid eventId, CancellationToken ct) =>
        await database.ExecuteAsync("INSERT OR IGNORE INTO achievement_consumed_facts(character_id,event_id) VALUES(@cid,@event)",
            p => { p.AddWithValue("@cid", character.Value); p.AddWithValue("@event", eventId.ToString("D")); }, ct) == 1;
    public async ValueTask<bool> AdvanceAsync(CharacterId character, int achievementId, int count, CancellationToken ct)
    {
        if (count <= 0) return false;
        var changed = await database.ExecuteAsync("UPDATE character_achievement_complete SET p1=MAX(0,p1-@count) WHERE character_id=@cid AND achievement_id=@aid AND p1>0 AND p2=0 AND p3=0",
            p => { p.AddWithValue("@cid", character.Value); p.AddWithValue("@aid", achievementId); p.AddWithValue("@count", count); }, ct);
        return changed == 1 && await IsCompletedAsync(character, achievementId, ct);
    }
    public async ValueTask<bool> IsCompletedAsync(CharacterId character, int achievementId, CancellationToken ct) =>
        await database.ReadFirstAsync("SELECT 1 FROM character_achievement_complete WHERE character_id=@cid AND achievement_id=@aid AND p1=0 AND p2=0 AND p3=0",
            p => { p.AddWithValue("@cid", character.Value); p.AddWithValue("@aid", achievementId); }, _ => new Present(), ct) is not null;
    public async ValueTask<bool> TryUnlockTitleAsync(CharacterId character, int category, int index, int itemId, CancellationToken ct) =>
        await database.ExecuteAsync("INSERT OR IGNORE INTO title_book_unlocks(character_id,category,slot_index,item_id) VALUES(@cid,@cat,@slot,@item)",
            p => { p.AddWithValue("@cid", character.Value); p.AddWithValue("@cat", category); p.AddWithValue("@slot", index); p.AddWithValue("@item", itemId); }, ct) == 1;
    private sealed record Present;
    public ValueTask<IReadOnlyList<StoredTitleBookItem>> ReadTitlesAsync(CharacterId character, CancellationToken ct) =>
        database.ReadAllAsync("SELECT s.list_type,s.slot_index,i.item_uid,i.item_core FROM item_slots s JOIN items i ON i.item_uid=s.core_uid WHERE s.owner_scope='character' AND s.owner_id=@cid AND s.list_type BETWEEN 19 AND 23 ORDER BY s.list_type,s.slot_index",
            p => p.AddWithValue("@cid", character.Value), r => new StoredTitleBookItem(r.GetInt32(0)-19,r.GetInt32(1),
                ItemSnapshot.FromCore(InventoryItemCore.Decode((byte[])r[3]),false,new(checked((int)r.GetInt64(2))))),ct);
}
