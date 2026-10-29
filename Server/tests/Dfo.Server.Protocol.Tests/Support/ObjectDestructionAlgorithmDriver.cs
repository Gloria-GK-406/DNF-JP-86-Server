using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Testing;
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
    IQuestProgressDomainService progress)
{
    private readonly GameMonsterDeathCommandHandler command = ProtocolAlgorithmFixture.Create<GameMonsterDeathCommandHandler>(
        sessions, runs, new NoProjectionOutbox(), ProtocolAlgorithmFixture.Create<Dfo.Server.Application.World.MonsterDeathDomainService>(
            sessions, runs, new Dfo.Server.Application.World.QuestItemDropDomainService(logs, containers, items), progress, TimeProvider.System));

    internal ValueTask<ObjectDestructionResult> HandleAsync(CommandExecutionContext unused,ObjectDestructionFacts input,CancellationToken ct)=>
        command.DestroyObjectAsync(input.Session,input.RunId,input.RoomVersion,input.ObjectCode,input.Recipients,ct);
}
