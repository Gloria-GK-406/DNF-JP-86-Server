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

public sealed record GameSortItemCommand(NetworkDispatchContext Dispatch, SortItemRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed record SortItemReceipt(SelectedSessionIdentity Session, CharacterInventoryEntryState Character,
    AccountInventoryEntryState Account, DateTimeOffset PreparedAt);

public sealed class GameSortItemCommandHandler(IInventorySortDomainService sorting,
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ICharacterInventoryEntryDomainService characterInventory, IAccountInventoryEntryDomainService accountInventory,
    TimeProvider clock, ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox,
    Dfo.Server.Abstractions.World.IDungeonRunDomainService? runs = null,
    Dfo.Server.Abstractions.World.IllusionTower.IIllusionTowerDomainService? towers = null)
    : ICommandHandler<GameSortItemCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext context, GameSortItemCommand command, CancellationToken ct)
    {
        var message = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        SortItemReceipt? receipt = null;
        if (session is not null && users.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var online)
            && online is not null && SortItemMapping.TryMap(message, out var space, out var order))
        {
            var onlineUserId = online.OnlineUserId;
            var outcome = await units.WriteAsync(context, new("SortItem", []), async (_, token) =>
            {
                if (!sessions.IsInWorld(session)
                    || !users.TryGetByConnection(session.ConnectionId, out var current)
                    || current?.OnlineUserId != onlineUserId)
                    return new Outcome(null);
                if (space is BusinessInventorySpace.PersonalCargo or BusinessInventorySpace.AccountCargo
                    && (runs?.Find(session.CharacterId) is not null || towers?.IsInTower(session.CharacterId) == true))
                    return new Outcome(null);
                if (!await sorting.SortAsync(session.CharacterId, space, order, token).ConfigureAwait(false))
                    return new Outcome(null);
                return new Outcome(new(session,
                    await characterInventory.LoadAsync(session.AccountId, session.CharacterId, token).ConfigureAwait(false),
                    await accountInventory.LoadAsync(session.AccountId, session.CharacterId, token).ConfigureAwait(false),
                    clock.GetUtcNow()));
            }, ct).ConfigureAwait(false);
            receipt = outcome.Receipt;
        }
        if (receipt is null)
            await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId,
                new SortItemResponse(message.ListType, false, 1), ct).ConfigureAwait(false);
        else
            await projections.ProjectAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new InventorySorted(receipt, (InventoryContainerType)message.ListType), ct).ConfigureAwait(false);
        return receipt is not null;
    }
    private sealed record Outcome(SortItemReceipt? Receipt);
}
