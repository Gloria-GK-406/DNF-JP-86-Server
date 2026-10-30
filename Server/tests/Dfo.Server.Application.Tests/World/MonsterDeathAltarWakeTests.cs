using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.World;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Altar;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Application.Tests.World;
public sealed partial class BossTowerDomainServiceTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Task45_altar_wake_follows_the_committed_kill_whatever_happens_to_its_projection(int fault)
    {
        var whole=MonsterKillOracle.Whole;var observation=new WakeObservation();
        await using var probe=new EntryFailureProbe(false,false,ordinary:true,commandOverride:whole?"GameMonsterDeathCommand":"ProcessMonsterDeathCommand");
        using var f=Fixture(configure:services=>
        {
            probe.Configure(services);
            var handler=typeof(MonsterDeathHandler).Assembly.GetType("Dfo.Server.Protocol.Game.GameMonsterDeathCommandHandler")??typeof(MonsterDeathHandler);
            var logger=typeof(ILogger<>).MakeGenericType(handler);
            services.AddScoped(logger,p=>Activator.CreateInstance(typeof(WakeLogger<>).MakeGenericType(handler),p.GetRequiredService<DungeonDeadlineScheduler>(),observation,probe.Cause,fault==1)!);
        });
        await TestGameplay.RunAsync(f.Services,async()=>
        {
            f.SetLevel(65);await using var tx=Scope(f).Begin();
            var layout=new DungeonLayout(144,0,3,1,new(0,0),new(2,0),61000,42,
                new Dictionary<MazeCell,DungeonCellDefinition>{[new(0,0)]=DungeonCellDefinition.Fixed(61000),[new(1,0)]=DungeonCellDefinition.Fixed(61001),[new(2,0)]=DungeonCellDefinition.Fixed(61002)})
                {Mode=new DungeonRunMode.Altar(new(AltarDungeonKind.Ultimate,new(2026,10,25)))};
            var run=f.Services.GetRequiredService<IDungeonRunDomainService>().PreparePartyEntry([new(f.Session.AccountId,f.Session.CharacterId,0,true)],layout,0,
                new(layout.StartCell,61000,1,[new(1,new(13099,1,0),false,false,[]),new(2,new(13099,1,0),false,false,[])],[]),DungeonRunId.New(),f.Clock.GetUtcNow()).Run;
            f.Services.GetRequiredService<DungeonRunDeliveryRegistry>().Register(run.RunId,[f.Session]);await tx.CommitAsync(default);
        });
        var signal=f.Services.GetRequiredService<DungeonDeadlineScheduler>();Assert.False(WakeObservation.Pending(signal));
        var source=probe.Sender.Add(f.Connection);
        f.Sender.Outbound.Encoder.Fail((_,_)=>{observation.Encodes++;return fault==2?probe.Cause:null;});
        var body=new byte[62];body[0]=1;
        var error=await Record.ExceptionAsync(()=>probe.Wire(f,0x27,body).AsTask());
        if(fault==0) Assert.Null(error);
        else if(whole||fault==2) Assert.Same(probe.Cause,Assert.IsType<TransactionOutcomeUncertainException>(error).InnerException);
        else Assert.Same(probe.Cause,error);
        Assert.Equal(fault!=2,observation.Delivered);Assert.True(WakeObservation.Pending(signal));
        Assert.True(observation.ReceivedInsideJob);Assert.Equal(fault==2?1:2,observation.Encodes);
        await probe.Sender.Settled();Assert.Equal(fault==2?0:1,source.Sends.Count);
        if(fault!=0)await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
internal sealed class WakeObservation
{
    internal int Encodes;internal bool Delivered,ReceivedInsideJob;
    internal static bool Pending(DungeonDeadlineScheduler signal)=>signal.IsWoken(DungeonDeadlineKind.Altar);
}
internal sealed class WakeLogger<T>(DungeonDeadlineScheduler signal,WakeObservation observed,Exception cause,bool fail):ILogger<T>,IDisposable
{
    public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;
    public bool IsEnabled(LogLevel level)=>true;
    public void Log<TState>(LogLevel level,EventId id,TState state,Exception? error,Func<TState,Exception?,string> formatter)
    {
        var text=formatter(state,error);
        if(text.StartsWith("Monster death received",StringComparison.Ordinal)){observed.ReceivedInsideJob=GameplayJob.Current is not null;Assert.False(WakeObservation.Pending(signal));}
        if(text.StartsWith("Monster death delivered",StringComparison.Ordinal)){Assert.NotNull(GameplayJob.Current);Assert.Equal(2,observed.Encodes);Assert.True(WakeObservation.Pending(signal));observed.Delivered=true;}
    }
    public void Dispose(){if(fail)throw cause;}
}

