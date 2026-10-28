using Dfo.Server.Abstractions.Notices;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Notices;
using Dfo.Server.Data.OnlineUsers;

namespace Dfo.Server.Application.Notices;

internal sealed class NoticeStateDomainService(INoticeStateTable table,
    IOnlineUserQueryDomainService users, ISelectedSessionDomainService sessions,
    IDomainTransactionAccessor transactions) : INoticeStateDomainService
{
    public async ValueTask<bool> MarkSeenAsync(GameSessionIdentity session, NoticeOwner owner,
        NoticeGroup group, byte noticeId, CancellationToken cancellationToken)
    {
        Validate(owner, group);
        if (noticeId > 127) throw new ArgumentOutOfRangeException(nameof(noticeId));
        if (transactions.Current is null)
            throw new InvalidOperationException("Recording a seen notice requires a transaction.");
        if (!users.TryGetByConnection(session.ConnectionId, out var user)
            || user is null || user.SessionIdentity != session || user.AccountId is not { } accountId
            || user.State == OnlineUserState.Connected) return false;

        if (owner.Type == NoticeOwnerType.Account)
        {
            if (owner.Id != accountId.Value) return false;
        }
        else if (!sessions.TryGetInWorld(session.ConnectionId, out var playing)
            || playing.CharacterId != new CharacterId(owner.Id)
            || playing.AccountId != accountId)
        {
            return false;
        }

        await table.InsertIfAbsentAsync(owner, group, noticeId, cancellationToken);
        return true;
    }

    public ValueTask<IReadOnlyList<byte>> GetSeenAsync(NoticeOwner owner, NoticeGroup group,
        CancellationToken cancellationToken)
    {
        Validate(owner, group);
        return table.ReadAsync(owner, group, cancellationToken);
    }

    private static void Validate(NoticeOwner owner, NoticeGroup group)
    {
        if (owner.Id <= 0 || !Enum.IsDefined(owner.Type))
            throw new ArgumentOutOfRangeException(nameof(owner));
        if (!Enum.IsDefined(group)) throw new ArgumentOutOfRangeException(nameof(group));
    }
}
