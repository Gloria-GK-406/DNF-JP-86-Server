using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.World.IllusionTower;

namespace Dfo.Server.Protocol.Game.InventorySort;

public sealed record SortItemCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    BusinessInventorySpace Space, InventorySortOrder Order)
    : ICommand<SortItemResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed record SortItemResult(SortItemReceipt? Receipt);
public sealed record SortItemReceipt(SelectedSessionIdentity Session, CharacterInventoryEntryState Character,
    AccountInventoryEntryState Account, DateTimeOffset PreparedAt);

public sealed class SortItemCommandHandler(IInventorySortDomainService sorting,
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    ICharacterInventoryEntryDomainService characterInventory, IAccountInventoryEntryDomainService accountInventory,
    TimeProvider clock, Dfo.Server.Abstractions.World.IDungeonRunDomainService? runs = null,
    IIllusionTowerDomainService? towers = null) : ICommandHandler<SortItemCommand, SortItemResult>
{
    public async ValueTask<SortItemResult> HandleAsync(CommandExecutionContext context,
        SortItemCommand command, CancellationToken ct)
    {
        var session = command.Session;
        if (sessions.Find(session.AccountId) != session
            || !users.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.OnlineUserId != command.OnlineUserId
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId)
            return new(null);
        if (command.Space is BusinessInventorySpace.PersonalCargo or BusinessInventorySpace.AccountCargo
            && (runs?.Find(session.CharacterId) is not null || towers?.IsInTower(session.CharacterId) == true)) return new(null);
        if (!await sorting.SortAsync(session.CharacterId, command.Space, command.Order, ct).ConfigureAwait(false))
            return new(null);
        return new(new(session,
            await characterInventory.LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false),
            await accountInventory.LoadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false),
            clock.GetUtcNow()));
    }
}
