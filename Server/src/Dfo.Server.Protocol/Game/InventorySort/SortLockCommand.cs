using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.InventorySort;

public sealed record SortLockCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    BusinessSlot Slot, InventorySortLockAction Action)
    : ICommand<SortLockResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed record SortLockResult(SortLockReceipt? Receipt);
public sealed record SortLockReceipt(SelectedSessionIdentity Session, BusinessSlot Slot, bool Locked);

public sealed class SortLockCommandHandler(IInventorySortDomainService sorting,
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users)
    : ICommandHandler<SortLockCommand, SortLockResult>
{
    public async ValueTask<SortLockResult> HandleAsync(CommandExecutionContext context, SortLockCommand command,
        CancellationToken ct)
    {
        var session = command.Session;
        if (sessions.Find(session.AccountId) != session
            || !users.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.OnlineUserId != command.OnlineUserId || online.AccountId != session.AccountId
            || online.CurrentCharacterId != session.CharacterId) return new(null);
        var locked = await sorting.ChangeLockAsync(session.CharacterId, command.Slot, command.Action, ct).ConfigureAwait(false);
        return new(locked is { } value ? new(session, command.Slot, value) : null);
    }
}
