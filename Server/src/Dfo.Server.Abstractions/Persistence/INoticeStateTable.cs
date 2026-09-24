using Dfo.Server.Data.Notices;

namespace Dfo.Server.Abstractions.Persistence;

public interface INoticeStateTable
{
    ValueTask InsertIfAbsentAsync(NoticeOwner owner, NoticeGroup group, byte noticeId, CancellationToken ct);
    ValueTask<IReadOnlyList<byte>> ReadAsync(NoticeOwner owner, NoticeGroup group, CancellationToken ct);
}
