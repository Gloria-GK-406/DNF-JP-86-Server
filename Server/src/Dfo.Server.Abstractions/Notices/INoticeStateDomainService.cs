using Dfo.Server.Data.Notices;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Abstractions.Notices;

public interface INoticeStateDomainService
{
    ValueTask<bool> MarkSeenAsync(GameSessionIdentity session, NoticeOwner owner, NoticeGroup group,
        byte noticeId, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<byte>> GetSeenAsync(NoticeOwner owner, NoticeGroup group,
        CancellationToken cancellationToken);
}
