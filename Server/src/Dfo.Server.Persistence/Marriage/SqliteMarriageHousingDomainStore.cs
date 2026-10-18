using System.Text.Json;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Persistence.Database;

namespace Dfo.Server.Persistence.Marriage;

internal sealed class SqliteMarriageHousingDomainStore(SqliteDatabase db) : IMarriageHousingDomainStore
{
    public async ValueTask<HousingSnapshot?> ReadAsync(Guid relation, CancellationToken ct)
    {
        var id = relation.ToString("N");
        var rooms = await db.ReadAllAsync("SELECT couple_no FROM marriage_housing_rooms WHERE relation_id=@id", p => p.AddWithValue("@id", id), r => r.GetInt32(0), ct).ConfigureAwait(false);
        if (rooms.Count == 0) return null;
        var storage = await db.ReadAllAsync("SELECT slot,item_id,count FROM marriage_housing_storage WHERE relation_id=@id ORDER BY slot", p => p.AddWithValue("@id", id), r => new HousingStorageItem(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)), ct).ConfigureAwait(false);
        var placed = await db.ReadAllAsync("SELECT slot,item_id FROM marriage_housing_placements WHERE relation_id=@id ORDER BY slot", p => p.AddWithValue("@id", id), r => new HousingPlacement(r.GetInt32(0), r.GetInt32(1)), ct).ConfigureAwait(false);
        var tree = await db.ReadAllAsync("SELECT payload_json FROM marriage_housing_trees WHERE relation_id=@id", p => p.AddWithValue("@id", id), r => JsonSerializer.Deserialize<HousingTree>(r.GetString(0))!, ct).ConfigureAwait(false);
        return new(relation, rooms.Single(), storage, placed, tree.SingleOrDefault());
    }
    public async ValueTask<HousingSnapshot> EnsureAsync(Guid relation, CancellationToken ct)
    {
        _ = await db.ExecuteAsync("INSERT OR IGNORE INTO marriage_housing_rooms(relation_id) VALUES(@id)", p => p.AddWithValue("@id", relation.ToString("N")), ct).ConfigureAwait(false);
        return (await ReadAsync(relation, ct).ConfigureAwait(false))!;
    }
    public async ValueTask SaveAsync(HousingSnapshot snapshot, CancellationToken ct)
    {
        var id = snapshot.RelationId.ToString("N");
        _ = await db.ExecuteAsync("DELETE FROM marriage_housing_storage WHERE relation_id=@id; DELETE FROM marriage_housing_placements WHERE relation_id=@id; DELETE FROM marriage_housing_trees WHERE relation_id=@id;", p => p.AddWithValue("@id", id), ct).ConfigureAwait(false);
        foreach (var item in snapshot.Storage)
            _ = await db.ExecuteAsync("INSERT INTO marriage_housing_storage VALUES(@id,@slot,@item,@count)", p => { p.AddWithValue("@id", id); p.AddWithValue("@slot", item.Slot); p.AddWithValue("@item", item.ItemId); p.AddWithValue("@count", item.Count); }, ct).ConfigureAwait(false);
        foreach (var item in snapshot.Placements)
            _ = await db.ExecuteAsync("INSERT INTO marriage_housing_placements VALUES(@id,@slot,@item)", p => { p.AddWithValue("@id", id); p.AddWithValue("@slot", item.Slot); p.AddWithValue("@item", item.ItemId); }, ct).ConfigureAwait(false);
        if (snapshot.Tree is { } tree)
            _ = await db.ExecuteAsync("INSERT INTO marriage_housing_trees VALUES(@id,@json)", p => { p.AddWithValue("@id", id); p.AddWithValue("@json", JsonSerializer.Serialize(tree)); }, ct).ConfigureAwait(false);
    }
    public async ValueTask<bool> RecordHarvestAsync(Guid planting, int round, IReadOnlyList<HousingReward> rewards, CancellationToken ct) =>
        await db.ExecuteAsync("INSERT OR IGNORE INTO marriage_housing_harvests VALUES(@planting,@round,@json)", p => { p.AddWithValue("@planting", planting.ToString("N")); p.AddWithValue("@round", round); p.AddWithValue("@json", JsonSerializer.Serialize(rewards)); }, ct).ConfigureAwait(false) == 1;
}
