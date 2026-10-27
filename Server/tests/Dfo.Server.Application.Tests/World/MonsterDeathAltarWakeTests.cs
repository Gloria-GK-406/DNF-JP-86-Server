using System.Threading.Channels;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Execution;
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
    public async Task Task45_altar_wake_occurs_inside_job_after_projection_and_before_scope_disposal(int fault)
    {
        var whole=MonsterKillOracle.Whole;var observation=new WakeObservation();
        await using var probe=new EntryFailureProbe(false,false,ordinary:true,commandOverride:whole?"GameMonsterDeathCommand":"ProcessMonsterDeathCommand");
        using var f=Fixture(configure:services=>
        {
            probe.Configure(services);
            var handler=typeof(MonsterDeathHandler).Assembly.GetType("Dfo.Server.Protocol.Game.GameMonsterDeathCommandHandler")??typeof(MonsterDeathHandler);
            var logger=typeof(ILogger<>).MakeGenericType(handler);
            services.AddScoped(logger,p=>Activator.CreateInstance(typeof(WakeLogger<>).MakeGenericType(handler),p.GetRequiredService<AltarWakeSignal>(),observation,probe.Cause,fault==1)!);
        });
        await TestGameplay.RunAsync(f.Services,async()=>
        {
            f.SetLevel(65);await using var tx=Scope(f).Begin();
            var layout=new DungeonLayout(144,0,3,1,new(0,0),new(2,0),61000,42,
                new Dictionary<MazeCell,DungeonCellDefinition>{[new(0,0)]=DungeonCellDefinition.Fixed(61000),[new(1,0)]=DungeonCellDefinition.Fixed(61001),[new(2,0)]=DungeonCellDefinition.Fixed(61002)})
                {Altar=new(AltarDungeonKind.Ultimate,new(2026,10,25))};
            var run=f.Services.GetRequiredService<IDungeonRunDomainService>().PreparePartyEntry([new(f.Session.AccountId,f.Session.CharacterId,0,true)],layout,0,
                new(layout.StartCell,61000,1,[new(1,new(13099,1,0),false,false,[]),new(2,new(13099,1,0),false,false,[])],[]),DungeonRunId.New(),f.Clock.GetUtcNow()).Run;
            f.Services.GetRequiredService<DungeonRunDeliveryRegistry>().Register(run.RunId,[f.Session]);await tx.CommitAsync(default);
        });
        var signal=f.Services.GetRequiredService<AltarWakeSignal>();Assert.False(WakeObservation.Pending(signal));
        var source=probe.Sender.Add(f.Connection);
        f.Sender.Outbound.Encoder.Fail((_,_)=>{observation.Encodes++;return fault==2?probe.Cause:null;});
        var body=new byte[62];body[0]=1;
        var error=await Record.ExceptionAsync(()=>probe.Wire(f,0x27,body).AsTask());
        if(fault==0) Assert.Null(error);
        else if(whole||fault==2) Assert.Same(probe.Cause,Assert.IsType<TransactionOutcomeUncertainException>(error).InnerException);
        else Assert.Same(probe.Cause,error);
        Assert.Equal(fault!=2,observation.Delivered);Assert.Equal(fault!=2,WakeObservation.Pending(signal));
        Assert.True(observation.ReceivedInsideJob);Assert.Equal(fault==2?1:2,observation.Encodes);
        await probe.Sender.Settled();Assert.Equal(fault==2?0:1,source.Sends.Count);
        if(fault!=0)await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if(fault!=2) await signal.WaitAsync(null,default);
        Assert.False(WakeObservation.Pending(signal));
    }
}
internal sealed class WakeObservation
{
    internal int Encodes;internal bool Delivered,ReceivedInsideJob;
    internal static bool Pending(AltarWakeSignal signal)=>((Channel<bool>)typeof(AltarWakeSignal).GetField("wake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(signal)!).Reader.TryPeek(out _);
}
internal sealed class WakeLogger<T>(AltarWakeSignal signal,WakeObservation observed,Exception cause,bool fail):ILogger<T>,IDisposable
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

public sealed partial class BossTowerDomainServiceTests
{
    [Fact]
    public async Task Task45_altar_signal_keeps_same_scheduler_instance_coalescing_cancellation_and_deadline()
    {
        using var f=Fixture();var signal=f.Services.GetRequiredService<AltarWakeSignal>();
        var capability=typeof(IDungeonRunDomainService).Assembly.GetType("Dfo.Server.Abstractions.World.IAltarSchedulerWakeSignal");
        if(capability is not null) Assert.Same(signal,f.Services.GetRequiredService(capability));
        var scheduler=ActivatorUtilities.CreateInstance<AltarScheduler>(f.Services);
        Assert.Contains(typeof(AltarScheduler).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic),field=>ReferenceEquals(signal,field.GetValue(scheduler)));
        signal.Wake();signal.Wake();await signal.WaitAsync(null,default);Assert.False(WakeObservation.Pending(signal));
        using(var cancelled=new CancellationTokenSource())
        {
            var waiting=signal.WaitAsync(null,cancelled.Token).AsTask();Assert.False(waiting.IsCompleted);cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>waiting);
        }
        var clock=new Scheduling.SchedulerClock();var timed=new AltarWakeSignal(clock);
        var deadline=timed.WaitAsync(clock.GetUtcNow().AddSeconds(20),default).AsTask();Assert.False(deadline.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(19999));Assert.False(deadline.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));await deadline.WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal(0,clock.ActiveTimers);
        var preempted=timed.WaitAsync(clock.GetUtcNow().AddHours(1),default).AsTask();timed.Wake();
        await preempted.WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal(0,clock.ActiveTimers);Assert.False(WakeObservation.Pending(timed));
    }
}
