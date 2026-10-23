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
    : ICommand<SortLockResult>, IAccountCommand
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
        if (!sessions.IsInWorld(session)
            || !users.TryGetByConnection(session.ConnectionId, out var online)
            || online?.OnlineUserId != command.OnlineUserId) return new(null);
        var locked = await sorting.ChangeLockAsync(session.CharacterId, command.Slot, command.Action, ct).ConfigureAwait(false);
        return new(locked is { } value ? new(session, command.Slot, value) : null);
    }
}
