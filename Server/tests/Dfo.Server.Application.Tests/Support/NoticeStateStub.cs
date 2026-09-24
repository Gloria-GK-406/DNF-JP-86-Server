using Dfo.Server.Abstractions.Notices;
using Dfo.Server.Data.Notices;
using Dfo.Server.Data.OnlineUsers;
namespace Dfo.Server.Application.Tests.Support;
internal sealed class NoticeStateStub : INoticeStateDomainService
{
    public ValueTask<bool> MarkSeenAsync(GameSessionIdentity session, NoticeOwner owner, NoticeGroup group, byte noticeId, CancellationToken ct) => throw new NotSupportedException();
    public ValueTask<IReadOnlyList<byte>> GetSeenAsync(NoticeOwner owner, NoticeGroup group, CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<byte>>(group == NoticeGroup.Primary ? [8] : [15,16]);
}
