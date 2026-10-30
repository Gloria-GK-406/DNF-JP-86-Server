using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.InventorySort;

public sealed record GameSortLockCommand(NetworkDispatchContext Dispatch, SortLockRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record SortLockReceipt(SelectedSessionIdentity Session, BusinessSlot Slot, bool Locked);

public sealed class GameSortLockCommandHandler(IInventorySortDomainService sorting,
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users, ICommandWorkUnits units, ICommandProjections projections)
    : ICommandHandler<GameSortLockCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameSortLockCommand command, CancellationToken ct)
    {
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (session is null || !users.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
            || online is null) return false;
        BusinessSlot slot;
        try { slot = A21InventoryLayout.ToBusinessSlot(command.Input.Slot); }
        catch (InventorySlotMappingException) { return false; }
        var onlineUserId = online.OnlineUserId;
        var outcome = await units.WriteAsync(context, new("SortLock", []), async (_, token) =>
        {
            if (!sessions.IsInWorld(session)
                || !users.TryGetByConnection(session.ConnectionId, out var current)
                || current?.OnlineUserId != onlineUserId) return new Outcome(null);
            var locked = await sorting.ChangeLockAsync(session.CharacterId, slot, command.Input.Action, token).ConfigureAwait(false);
            return new Outcome(locked is { } value ? new(session, slot, value) : null);
        }, ct).ConfigureAwait(false);
        if (outcome.Receipt is { } receipt)
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
                new SortLockChanged(receipt), ct).ConfigureAwait(false);
        return outcome.Receipt is not null;
    }
    private sealed record Outcome(SortLockReceipt? Receipt);
}
