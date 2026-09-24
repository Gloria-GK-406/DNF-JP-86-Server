using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Notices;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Persistence.Notices;

internal sealed class NoticeStateTable(SqliteDatabase database) : INoticeStateTable
{
    public async ValueTask InsertIfAbsentAsync(NoticeOwner owner, NoticeGroup group, byte noticeId, CancellationToken ct) =>
        _ = await database.ExecuteAsync("""
            INSERT INTO notice_states(owner_type,owner_id,notice_group,notice_id)
            VALUES(@type,@owner,@group,@id)
            ON CONFLICT(owner_type,owner_id,notice_group,notice_id) DO NOTHING;
            """, p => { Bind(p, owner, group); p.AddWithValue("@id", noticeId); }, ct);

    public ValueTask<IReadOnlyList<byte>> ReadAsync(NoticeOwner owner, NoticeGroup group, CancellationToken ct) =>
        database.ReadAllAsync("""
            SELECT notice_id FROM notice_states
            WHERE owner_type=@type AND owner_id=@owner AND notice_group=@group ORDER BY notice_id;
            """, p => Bind(p, owner, group), r => checked((byte)r.GetInt32(0)), ct);

    private static void Bind(SqliteParameterCollection p, NoticeOwner owner, NoticeGroup group)
    {
        p.AddWithValue("@type", (int)owner.Type);
        p.AddWithValue("@owner", owner.Id);
        p.AddWithValue("@group", (int)group);
    }
}
