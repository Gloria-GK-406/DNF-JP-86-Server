using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed record ConsumeSkillMaterialsCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session,
    IReadOnlyList<SkillMaterialCost> Costs) : ICommand<SkillMaterialConsumptionResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class ConsumeSkillMaterialsCommandHandler(ISkillMaterialConsumptionDomainService materials)
    : ICommandHandler<ConsumeSkillMaterialsCommand, SkillMaterialConsumptionResult>
{
    public ValueTask<SkillMaterialConsumptionResult> HandleAsync(CommandExecutionContext context,
        ConsumeSkillMaterialsCommand command, CancellationToken ct) => materials.ConsumeAsync(command.Session, command.Costs, ct);
}
