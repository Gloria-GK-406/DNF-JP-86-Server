using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Microsoft.Extensions.Logging.Abstractions;
namespace Dfo.Server.Protocol.Tests.Support;
internal sealed record ObjectDestructionFacts(SelectedSessionIdentity Session,DungeonRunId RunId,long RoomVersion,int ObjectCode,
    IReadOnlyList<SelectedSessionIdentity>? Recipients=null) : ICommand<ObjectDestructionResult>
{
    public IReadOnlyList<SelectedSessionIdentity> Recipients {get;init;}=Recipients??[Session];
}
// The existing ambient-transaction domain fixtures call the actual whole-owned object algorithm.
// They do not emulate a production work unit or its request scope.
internal sealed class ObjectDestructionAlgorithmDriver(IDungeonRunDomainService runs,ISelectedSessionDomainService sessions,
    ICharacterQuestLogDomainService logs,ICharacterContainerDomainService containers,IItemDeliveryDomainService items,
    IQuestProgressDomainService progress) : GameMonsterDeathCommandHandler(sessions,runs,null!,null!,null!,null!,null!,null!,
        logs,containers,items,progress,null!,null!,null!,null!,null!,new Dfo.Server.Protocol.Tests.Game.MonsterDeath.DriverOutbox(null!),null!,TimeProvider.System,null!,null!,NullLogger<GameMonsterDeathCommandHandler>.Instance)
{
    internal ValueTask<ObjectDestructionResult> HandleAsync(CommandExecutionContext unused,ObjectDestructionFacts input,CancellationToken ct)=>
        DestroyObjectAsync(input.Session,input.RunId,input.RoomVersion,input.ObjectCode,input.Recipients,ct);
}
