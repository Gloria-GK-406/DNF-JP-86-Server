using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CardReward;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Tests.Support;
// The pre-existing timer fixtures retain their controlled queue/award collaborators. This adapter
// runs the owning family algorithm; production bus/origin/scope is tested by CardSettlementLifecycleTests.
internal sealed class CardPresentationFixture : IDungeonCardPresentationControl,IGameplayProducerDrain,IDisposable
{
    internal DungeonCardPresentationCoordinator Producer { get; }
    private readonly CardWireDriver algorithm;
    internal CardPresentationFixture(ISelectedSessionDomainService sessions,IDungeonRunDomainService runs,ICommandBus award,
        IGameMessageSender sender,CardSelectionProjectionFixture projection,TimeProvider clock,
        IDungeonCardPresentationDelay delay,ILogger<DungeonCardPresentationCoordinator> logger,DungeonCardPresentationTiming timing,IGameplayQueue queue)
    {
        _=clock;
        var bus=new Bus(queue);
        Producer=new DungeonCardPresentationCoordinator(bus,delay,logger,timing);
        algorithm=new CardWireDriver(sessions,null!,runs,award,sender,logger,projection,this);
        bus.Algorithm=algorithm;
    }
    public void ScheduleAfterSettlement(SelectedSessionIdentity session,DungeonRunId run)=>Producer.ScheduleAfterSettlement(session,run);
    public void ScheduleAutoFlip(SelectedSessionIdentity session,DungeonRunId run)=>Producer.ScheduleAutoFlip(session,run);
    public void Cancel(CharacterId character,DungeonRunId run)=>Producer.Cancel(character,run);
    public ValueTask OpenOnRequestAsync(SelectedSessionIdentity session,DungeonRunId run,CancellationToken ct)=>algorithm.OpenAsync(session,run,ct);
    public ValueTask DrainAsync(CancellationToken ct)=>Producer.DrainAsync(ct);
    public void Dispose()=>Producer.Dispose();
    private sealed class Bus(IGameplayQueue queue):ICommandBus
    {
        internal CardWireDriver Algorithm=null!;
        public ValueTask<T> SendAsync<T>(ICommand<T> c,CancellationToken ct=default)where T:notnull=>throw new NotSupportedException();
        public async ValueTask<T> SendInternalAsync<T>(ICommand<T> c,InternalCommandOrigin origin,CancellationToken ct=default)where T:notnull
        {
            await queue.RunAsync(new(GameplayShardKey.World,origin.Chain,origin.CommandType,async jobToken=>
            {
                var execution=new CommandExecutionContext(Guid.NewGuid(),c.GetType(),typeof(GameCardCommandHandler),CommandExecutionKind.Direct);
                if(c is DungeonCardLayoutCommand layout)_=await Algorithm.HandleAsync(execution,layout,ct);
                else if(c is DungeonCardAutoFlipCommand auto)_=await Algorithm.HandleAsync(execution,auto,ct);
                else throw new NotSupportedException();
            }),ct);
            return (T)(object)true;
        }
    }
}
