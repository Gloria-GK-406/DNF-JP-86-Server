using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class LightweightProtocolCommandFlowTests(ITestOutputHelper output)
{
    private static readonly ushort[] Wire = [0x3F,0x65,0x7A,0xAB,0xBF,0xC2,0xFA,0x1BA,0x1DD,0x252,0x2A8,0x2B3,0x2C1,0x2CC,0x4DD];
    private static readonly string[] Categories = ["MapEntryReports.MapEntryReportHandler","Creatures.CreatureResponseReportHandler","Creatures.CreatureScriptMessageReportHandler","ClientSpecStatistic.ClientSpecStatisticHandler","StoryPause.StoryPauseHandler","ClientSpecProfile.ClientSpecProfileHandler","SessionMetrics.SessionMetricsHandler","MercenaryInfo.MercenaryInfoHandler","ClientHardwareDetail.ClientHardwareDetailHandler","MapEntryReports.SecurityStatusHandler","RosterFollowUp.RosterFollowUpHandler","MapEntryReports.MapEntryReportHandler","ContentsPlayInfo.ContentsPlayInfoHandler",""," "];
    private static bool Modern => typeof(ProtocolModule).Assembly.GetTypes().Any(t=>t.Name=="GameShopPurchaseCountCommand");
    public static IEnumerable<object[]> Entries() => Enumerable.Range(0,15).SelectMany(p=>new[] {new object[]{p,false},new object[]{p,true}});

    [Theory] [MemberData(nameof(Entries))]
    public async Task Every_registered_decoder_keeps_exact_wire_log_category_and_no_transaction(int protocol,bool unattributed)
    {
        var trace=new Trace{Unattributed=unattributed};using var f=Create(trace);
        await Send(f,protocol);AssertWire(f,protocol,unattributed);
        Assert.All(trace.Calls,c=>Assert.True(c.WithoutTransaction));
        Assert.Equal(protocol is 0 or 3 or 5 or 6 or 8 or 10 or 11 or 13,
            trace.Calls.Any(c=>c.Name=="online.TryGetByConnection"));
        Assert.Equal(protocol is 1 or 2 or 4 or 13,
            trace.Calls.Any(c=>c.Name=="sessions.FindByConnection"));
        if(protocol is not (13 or 14))
        {
            var entry=Assert.Single(trace.Logs);Assert.Equal(Category(protocol),entry.Category);
            Assert.Equal(protocol is 1 or 2?LogLevel.Debug:LogLevel.Information,entry.Level);
            if(protocol is 0 or 3 or 5 or 6 or 8 or 10 or 11)Assert.Contains(unattributed?"unattributed":"account=1",entry.Text);
            if(protocol==0)Assert.Contains("CERA (empty body)",entry.Text);
            if(protocol==1)Assert.Contains("actor=4660 selected=True",entry.Text);
            if(protocol==2)Assert.Contains("mode=3 target=4660 character=1 bytes=3 selected=True",entry.Text);
            if(protocol==3){Assert.Contains("cores=12 vendor=Intel clock=>2450MHz",entry.Text);Assert.Contains("pci=8086:A7A8",entry.Text);Assert.Contains("Windows10/11 sp=0",entry.Text);Assert.Contains("bucket=7 (>2056MB)",entry.Text);}
            if(protocol==4)Assert.Contains("pause=127 type=129",entry.Text);
            if(protocol==5){Assert.Contains("cores=12 clock=2611MHz",entry.Text);Assert.Contains("directX=9.0.0",entry.Text);Assert.Contains("6rows all-zero",entry.Text);}
            if(protocol==6)Assert.Contains("8tags nothing-sampled",entry.Text);
            if(protocol==8){Assert.Contains("cores=12 vendor=Intel clock=2611MHz",entry.Text);Assert.Contains("Windows10/11 bitness=64",entry.Text);Assert.Contains("16107MB",entry.Text);}
            if(protocol==9)Assert.Contains("0F0000007F00000199",entry.Text);
            if(protocol==10)Assert.Contains("0xFFFF (sentinel)",entry.Text);
            if(protocol==11)Assert.Contains("STATICS_RUNTIME_TING value=87",entry.Text);
        }
        Assert.Empty(f.Sender.Outbound.Closes);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        output.WriteLine($"modern={Modern} protocol={Wire[protocol]:X4} body={Convert.ToHexString(Body(protocol))} frames={f.Sender.Outbound.Frames.Count} calls={string.Join(',',trace.Calls.Select(c=>c.Name))}");
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Selection_without_world_keeps_story_echo_creature_attribution_and_shop_query_without_projection(bool missing)
    {
        var trace=new Trace();using var f=Create(trace);
        TestGameplay.Run(f.Services,()=>{var registry=f.Services.GetRequiredService<OnlineStateRegistry>();registry.ReleaseExact(f.Session);if(!missing)registry.Select(f.Session);});
        await Send(f,4);Assert.Equal(missing?0:1,f.Sender.Outbound.Frames.Count);
        if(!missing)Assert.Equal(new byte[]{(byte)f.Session.WireUserId,(byte)(f.Session.WireUserId>>8),127,129},f.Sender.Outbound.Frames[0][15..]);
        Assert.Equal(missing?LogLevel.Warning:LogLevel.Information,trace.Logs.Single().Level);
        f.Sender.Messages.Clear();trace.Logs.Clear();trace.Calls.Clear();
        await Send(f,1);await Send(f,2);Assert.All(trace.Logs,e=>Assert.Contains($"selected={!missing}",e.Text));Assert.Empty(f.Sender.Outbound.Frames);
        trace.Logs.Clear();trace.Calls.Clear();await Send(f,13);
        Assert.Equal(!missing,trace.Calls.Any(c=>c.Name=="shops.PurchaseCountsAsync"));
        if(missing)Assert.Equal(new byte[]{0,0},Assert.Single(f.Sender.Outbound.Frames)[15..]);else Assert.Empty(f.Sender.Outbound.Frames);
    }

    [Fact]
    public async Task Creature_script_character_mismatch_logs_false_and_does_not_send()
    {
        var trace=new Trace();using var f=Create(trace);var body=Body(2);body[3]=2;
        await Send(f,2,body);Assert.Contains("character=2 bytes=3 selected=False",Assert.Single(trace.Logs).Text);Assert.Empty(f.Sender.Outbound.Frames);
    }

    [Fact]
    public async Task Shop_keeps_session_online_clock_query_and_live_projection_order()
    {
        var trace=new Trace();using var f=Create(trace);await Send(f,13);
        var calls=trace.Calls.Select(c=>c.Name).ToArray();
        Assert.True(Array.IndexOf(calls,"sessions.FindByConnection")<Array.IndexOf(calls,"online.TryGetByConnection"));
        Assert.True(Array.IndexOf(calls,"online.TryGetByConnection")<Array.IndexOf(calls,"clock.GetUtcNow"));
        Assert.True(Array.IndexOf(calls,"clock.GetUtcNow")<Array.IndexOf(calls,"shops.PurchaseCountsAsync"));
        Assert.Contains("sessions.IsInWorld",calls);Assert.All(trace.Calls,c=>Assert.True(c.WithoutTransaction));
        Assert.Equal(f.Session,trace.QuerySession);Assert.Equal(2,trace.Npc);Assert.Equal(f.Clock.GetUtcNow(),trace.At);
        Assert.Equal(new byte[]{1,0,0,0,0,0,0,0,0},Assert.Single(f.Sender.Outbound.Frames)[15..]);
    }

    [Theory] [InlineData(4)] [InlineData(7)] [InlineData(9)] [InlineData(10)] [InlineData(12)] [InlineData(13)] [InlineData(14)]
    public async Task First_actual_encoder_fault_keeps_raw_cause_or_ignored_shop_projection_and_exact_physical_identity(int protocol)
    {
        await using var delivery=new RemainingOpeningProtocolCommandFlowTests.SenderProbe();var trace=new Trace();using var f=Create(trace,delivery);
        var old=delivery.Add(f.Connection);var replacement=f.Connection with {};var next=delivery.Add(replacement);var cause=new IOException("TASK56 actual encoder");var calls=0;
        f.Sender.Outbound.Encoder.Fail((target,_)=>{Assert.Same(f.Connection,target);calls++;f.Sender.Outbound.Connections.Remove(target.ConnectionId);f.Sender.Outbound.Connections.Add(replacement);return cause;});
        var failure=await Record.ExceptionAsync(()=>Send(f,protocol).AsTask());if(protocol==13)Assert.Null(failure);else Assert.Same(cause,failure);Assert.Equal(1,calls);
        if(protocol!=13)await old.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await delivery.Settled();
        Assert.Empty(old.Sends);Assert.Equal(protocol==13?0:1,old.CloseCalls);Assert.Empty(next.Events);Assert.Equal(0,next.CloseCalls);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));
    }

    [Theory] [InlineData(12)] [InlineData(13)]
    public async Task Actual_DI_scope_dispose_keeps_raw_cause_and_original_or_whole_frame_timing(int protocol)
    {
        await using var delivery=new RemainingOpeningProtocolCommandFlowTests.SenderProbe();var trace=new Trace{ScopeFailure=new IOException("TASK56 actual DI Dispose")};using var f=Create(trace,delivery);
        var physical=delivery.Add(f.Connection);var failure=await Record.ExceptionAsync(()=>Send(f,protocol).AsTask());Assert.Same(trace.ScopeFailure,failure);
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await delivery.Settled();Assert.Equal(1,physical.CloseCalls);
        Assert.Equal(protocol==13&&!Modern?0:1,physical.Sends.Count);Assert.Equal(protocol==13&&!Modern?new[]{"close"}:new[]{"send","close"},physical.Events.Select(e=>e.Kind));
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
        Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection,out var chain));Assert.Equal(trace.Chain.RootId,chain.RootId);
        Assert.All(trace.Calls,c=>Assert.True(c.WithoutTransaction));trace.ScopeFailure=null;
    }

    [Theory] [InlineData(3)] [InlineData(13)]
    public async Task Real_log_or_query_failure_closes_source_with_same_cause_and_no_frame(int protocol)
    {
        await using var delivery=new RemainingOpeningProtocolCommandFlowTests.SenderProbe();var trace=new Trace{ActionFailure=new IOException("TASK56 log or query")};using var f=Create(trace,delivery);var physical=delivery.Add(f.Connection);
        Assert.Same(trace.ActionFailure,await Record.ExceptionAsync(()=>Send(f,protocol).AsTask()));await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await delivery.Settled();Assert.Empty(physical.Sends);Assert.Equal(1,physical.CloseCalls);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
    }

    [Fact]
    public void Cold_actual_DI_has_unique_bindings_direct_execution_and_no_extra_capabilities()
    {
        using var f=Create(new Trace());
        _=f.Services.GetRequiredService<MessageHandlerRegistry>();
        var messages=f.Services.GetServices<MessageHandlerRegistration>().ToArray();
        var commands=f.Services.GetServices<CommandHandlerRegistration>().ToArray();
        foreach(var wire in Wire)
        {
            var id=(ClientMessageType)(65536+wire);var message=Assert.Single(messages,m=>m.MessageType==id);
            Assert.False(message.IsTelemetry);
            if(!Modern){Assert.Equal(MessageHandlerRouteKind.Telemetry,message.Route);continue;}
            Assert.Equal(MessageHandlerRouteKind.ProtocolCommand,message.Route);
            var command=Assert.Single(commands,c=>c.ProtocolMessages?.Any(b=>b.MessageType==id)==true);
            Assert.Equal(message.ProtocolCommandType,command.CommandType);Assert.Equal(CommandRouteKind.Protocol,command.Route);
            Assert.True(command.RegistrationExits is null or {Count:0});
            Assert.Equal(CommandSelfSettlementCapability.None,command.SelfSettlement);
        }
        Assert.Equal(Modern?0:1,commands.Count(c=>c.CommandType.Name=="QueryShopPurchaseCountsCommand"));
        Assert.Equal(Modern?0:1,typeof(ProtocolModule).Assembly.GetTypes().Count(t=>t.Name=="QueryShopPurchaseCountsCommand"));
        output.WriteLine($"loaded={typeof(ProtocolModule).Assembly.Location} mvid={typeof(ProtocolModule).Module.ModuleVersionId} modern={Modern} bindings=15");
    }

    private static ShopFixture Create(Trace trace,RemainingOpeningProtocolCommandFlowTests.SenderProbe? delivery=null)
    {
        var f=new ShopFixture(s=>{
            if(delivery is not null)s.AddSingleton<IOutboundSender>(delivery.Recorder);
            s.AddSingleton(p=>new GameplayOutboxes(p.GetRequiredService<IOutboundSender>(),p.GetRequiredService<GameplayCloseCauses>(),p.GetRequiredService<ILogger<GameplayOutbox>>()));
            var clock=s.Last(d=>d.ServiceType==typeof(TimeProvider));
            s.AddSingleton<TimeProvider>(p=>new ClockProbe((TimeProvider)Actual(clock,p),trace,p));
            Decorate<IOnlineUserQueryDomainService>(s,"online",trace);Decorate<ISelectedSessionDomainService>(s,"sessions",trace);Decorate<INpcShopDomainService>(s,"shops",trace);
            foreach(var category in Categories.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct())
            {
                var type=typeof(ProtocolModule).Assembly.GetType("Dfo.Server.Protocol.Game."+category,true)!;
                s.AddScoped(typeof(ILogger<>).MakeGenericType(type),p=>Activator.CreateInstance(typeof(SemanticLogger<>).MakeGenericType(type),trace)!);
            }
            var command=Modern?"GameShopPurchaseCountCommand":"QueryShopPurchaseCountsCommand";
            var d=s.Last(x=>x.ServiceType.IsGenericType&&x.ServiceType.GetGenericTypeDefinition()==typeof(ICommandHandler<,>)&&x.ServiceType.GenericTypeArguments[0].Name==command);
            s.AddScoped(d.ServiceType,p=>Activator.CreateInstance(typeof(ScopeProbe<,>).MakeGenericType(d.ServiceType.GenericTypeArguments),Actual(d,p),trace)!);
        });trace.Armed=true;return f;
    }
    private static void Decorate<T>(IServiceCollection s,string prefix,Trace trace)where T:class
    {var d=s.Last(x=>x.ServiceType==typeof(T));s.AddSingleton<T>(p=>{var proxy=DispatchProxy.Create<T,Probe>();var spy=(Probe)(object)proxy;spy.Actual=Actual(d,p);spy.Trace=trace;spy.Prefix=prefix;spy.Provider=p;return proxy;});}
    private static object Actual(ServiceDescriptor d,IServiceProvider p)=>d.ImplementationInstance??d.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,d.ImplementationType!);
    private static string Category(int p)=>"Dfo.Server.Protocol.Game."+Categories[p];
    private static ValueTask Send(ShopFixture f,int p,byte[]? body=null)=>EffectRequestTransport.SendRaw(f,new(1,Wire[p],0,7),body??Body(p));
    private static void AssertWire(ShopFixture f,int p,bool unattributed)
    {
        var frames=f.Sender.Outbound.Frames;if(p is 0 or 1 or 2 or 3 or 5 or 6 or 8 or 11){Assert.Empty(frames);return;}
        var frame=Assert.Single(frames);Assert.Equal(p==4?(byte)0:(byte)1,frame[0]);Assert.Equal(p==4?(ushort)0xAA:Wire[p],BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        byte[] expected=p switch {4=>[(byte)f.Session.WireUserId,(byte)(f.Session.WireUserId>>8),127,129],7=>[1,0,0,0,0,0,0],9=>new byte[6],10=>[0,0],12=>[1,..new byte[18]],13=>unattributed?[0,0]:[1,0,0,0,0,0,0,0,0],14=>[1],_=>throw new InvalidOperationException()};Assert.Equal(expected,frame[15..]);
    }
    private static byte[] Body(int p)
    {
        using var stream=new MemoryStream();using var w=new BinaryWriter(stream);
        switch(p)
        {
            case 0:case 14:break;
            case 1:w.Write((ushort)0x1234);break;
            case 2:w.Write((byte)3);w.Write((ushort)0x1234);w.Write(1u);w.Write(3);w.Write(new byte[]{0,255,42});break;
            case 3:w.Write(new byte[]{128,0,4});uint[] a=[12,0x8086,14,0],b=[3,0xA7A8,0,7];for(byte i=0;i<4;i++){w.Write(i);w.Write(a[i]);w.Write(b[i]);}break;
            case 4:w.Write(new byte[]{127,129,77});break;
            case 5:w.Write(2611u);w.Write((ushort)12);w.Write((ushort)0x8086);w.Write((ushort)0xA7A8);w.Write((ushort)4096);w.Write((ushort)8192);w.Write(new byte[]{14,1});w.Write(0x90000u);w.Write((byte)6);w.Write((short)-1);w.Write((short)-1);w.Write(new byte[337]);break;
            case 6:foreach(ushort tag in new ushort[]{0,1,2,3,4,5,6,9}){w.Write(tag);w.Write(0u);}w.Write((ushort)7);w.Write((ushort)0);w.Write((ushort)8);w.Write((ushort)0);break;
            case 7:w.Write(new byte[]{99,88});break; // Original decoder ignores this body.
            case 8:w.Write(new byte[]{0,12});w.Write(2611u);w.Write((ushort)16107);w.Write((ushort)0x8086);w.Write((ushort)0xA7A8);w.Write((ushort)4093);w.Write(new byte[]{14,2});break;
            case 9:w.Write(new byte[]{15,0,0,0,127,0,0,1,153});break;
            case 10:w.Write((ushort)65535);break;
            case 11:w.Write((byte)87);break;
            case 12:w.Write((byte)1);break;
            case 13:w.Write(2);break;
        }return stream.ToArray();
    }
    public sealed class Trace{internal bool Armed,Unattributed;internal Exception? ScopeFailure,ActionFailure;internal List<(string Category,LogLevel Level,string Text)> Logs=[];internal List<(string Name,bool WithoutTransaction)> Calls=[];internal Dfo.Server.Data.World.SelectedSessionIdentity? QuerySession;internal int Npc;internal DateTimeOffset At;internal GameplayCallChain Chain;}
    public class Probe:DispatchProxy
    {internal object Actual=null!;internal Trace Trace=null!;internal string Prefix="";internal IServiceProvider Provider=null!;protected override object? Invoke(MethodInfo? method,object?[]? args)
        {if(Trace.Armed){Trace.Calls.Add(($"{Prefix}.{method!.Name}",Provider.GetRequiredService<IDomainTransactionAccessor>().Current is null));Trace.Chain=GameplayJob.Current??Trace.Chain;
            if(Prefix=="online"&&method.Name=="TryGetByConnection"&&Trace.Unattributed){args![1]=null;return false;}
            if(Prefix=="shops"&&method.Name=="PurchaseCountsAsync"){Trace.QuerySession=(Dfo.Server.Data.World.SelectedSessionIdentity)args![0]!;Trace.Npc=(int)args[1]!;Trace.At=(DateTimeOffset)args[2]!;if(Trace.ActionFailure is {} cause)throw cause;}}
        try{return method!.Invoke(Actual,args);}catch(TargetInvocationException ex)when(ex.InnerException is {} cause){ExceptionDispatchInfo.Capture(cause).Throw();throw;}}
    }
    public sealed class SemanticLogger<T>(Trace trace):ILogger<T>,IDisposable
    {public IDisposable? BeginScope<S>(S state)where S:notnull=>null;public bool IsEnabled(LogLevel level)=>true;public void Log<S>(LogLevel level,EventId id,S state,Exception? error,Func<S,Exception?,string> format){if(!trace.Armed)return;trace.Chain=GameplayJob.Current!.Value;if(trace.ActionFailure is {} cause)throw cause;trace.Logs.Add((typeof(T).FullName!,level,format(state,error)));}public void Dispose(){if(trace.Armed&&trace.ScopeFailure is {} cause)throw cause;}}
    private sealed class ClockProbe(TimeProvider actual,Trace trace,IServiceProvider p):TimeProvider
    {public override DateTimeOffset GetUtcNow(){if(trace.Armed)trace.Calls.Add(("clock.GetUtcNow",p.GetRequiredService<IDomainTransactionAccessor>().Current is null));return actual.GetUtcNow();}}
    public sealed class ScopeProbe<T,R>(object actual,Trace trace):ICommandHandler<T,R>,IDisposable where T:class,ICommand<R>where R:notnull
    {public ValueTask<R> HandleAsync(CommandExecutionContext c,T command,CancellationToken ct){trace.Chain=GameplayJob.Current!.Value;return ((ICommandHandler<T,R>)actual).HandleAsync(c,command,ct);}public void Dispose(){if(trace.Armed&&trace.ScopeFailure is {} cause)throw cause;}}
}
