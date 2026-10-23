using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed record ConsumeSkillMaterialsCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    IReadOnlyList<MaterialCost> Costs) : ICommand<MaterialConsumptionResult>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class ConsumeSkillMaterialsCommandHandler(IMaterialConsumptionDomainService materials)
    : ICommandHandler<ConsumeSkillMaterialsCommand, MaterialConsumptionResult>
{
    public ValueTask<MaterialConsumptionResult> HandleAsync(CommandExecutionContext context,
        ConsumeSkillMaterialsCommand command, CancellationToken ct) => materials.ConsumeAsync(command.Session, command.Costs, ct);
}
