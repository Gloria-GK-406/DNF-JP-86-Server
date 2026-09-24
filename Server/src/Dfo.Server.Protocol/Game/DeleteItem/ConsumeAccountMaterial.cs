using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed record ConsumeAccountMaterialCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    AccountMaterialKind Kind, int ItemId, int Count, byte EntryCount, ushort OperationType)
    : ICommand<ConsumeAccountMaterialResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class ConsumeAccountMaterialCommandHandler(IItemDiscardDomainService discards)
    : ICommandHandler<ConsumeAccountMaterialCommand, ConsumeAccountMaterialResult>
{
    public async ValueTask<ConsumeAccountMaterialResult> HandleAsync(CommandExecutionContext context,
        ConsumeAccountMaterialCommand command, CancellationToken ct) =>
        new(await discards.ConsumeMaterialAsync(command.Session,command.Kind,command.ItemId,command.Count,
            command.EntryCount,command.OperationType,ct));
}

public sealed record ConsumeAccountMaterialResult(AccountMaterialChange? Change);
