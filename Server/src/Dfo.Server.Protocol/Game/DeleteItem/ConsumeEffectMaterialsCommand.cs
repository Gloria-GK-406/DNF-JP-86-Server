using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed record ConsumeEffectMaterialsCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    IReadOnlyList<MaterialCost> Costs) : ICommand<MaterialConsumptionResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class ConsumeEffectMaterialsCommandHandler(IMaterialConsumptionDomainService materials)
    : ICommandHandler<ConsumeEffectMaterialsCommand, MaterialConsumptionResult>
{
    public ValueTask<MaterialConsumptionResult> HandleAsync(CommandExecutionContext context,
        ConsumeEffectMaterialsCommand command, CancellationToken ct) => materials.ConsumeAsync(command.Session, command.Costs, ct);
}
