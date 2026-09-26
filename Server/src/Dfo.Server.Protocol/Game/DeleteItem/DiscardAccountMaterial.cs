using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed record DiscardAccountMaterialCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    AccountMaterialKind Kind, int ItemId, int Count, byte EntryCount, ushort OperationType)
    : ICommand<DiscardAccountMaterialResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class DiscardAccountMaterialCommandHandler(IItemDiscardDomainService discards)
    : ICommandHandler<DiscardAccountMaterialCommand, DiscardAccountMaterialResult>
{
    public async ValueTask<DiscardAccountMaterialResult> HandleAsync(CommandExecutionContext context,
        DiscardAccountMaterialCommand command, CancellationToken ct) =>
        new(await discards.DiscardMaterialAsync(command.Session,command.Kind,command.ItemId,command.Count,
            command.EntryCount,command.OperationType,ct));
}

public sealed record DiscardAccountMaterialResult(AccountMaterialChange? Change);
