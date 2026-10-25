using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.World;
public sealed partial class BossTowerDomainServiceTests
{
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Task42_real_boss_selection_release_preserves_choice_and_stale_guards_and_uses_an_ordinary_protected_unit(bool stale)
    {
        var units = new List<CommandWorkUnitDeclaration>();
        using var f = Fixture(configure: services =>
        {
            services.AddSingleton<Dfo.Server.Abstractions.Sessions.ISelectedCharacterCurrentViewDomainService, Task39NoReturnView>();
            Task42UnitObserver.Install(services, units);
        });
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); await f.Seed(65, 2749110, 3);
            var tower = Tower(f); var run = (await Enter(f)).Run!; var context = Context(run, f.Session);
            await tower.LoadAsync(context, default); await tower.StartAsync(context, default);
            await Dfo.Server.Application.Tests.Support.TestEffectFootprints.Run(f.Session,
                footprint => tower.DieAsync(context, footprint, default));
            Assert.True((await tower.SelectResultAsync(context, 0, default)).Changed);
        });
        await Selection(stale ? (ushort)3505 : (ushort)0, stale);
        TestGameplay.Run(f.Services, () => Assert.NotNull(Tower(f).Find(f.Session)));
        Assert.Equal(stale ? 0 : 1, units.Count);
        await Selection(3505, false);
        TestGameplay.Run(f.Services, () => Assert.Null(Tower(f).Find(f.Session)));
        Assert.Equal(2, f.Count(2749110)); Assert.Empty(f.Sender.Messages);
        Assert.Equal(stale ? 1 : 2, units.Count);
        Assert.All(units, unit =>
        {
            Assert.Equal("selection-release-boss-tower", unit.Name);
            Assert.Equal(CommandWorkUnitKind.Ordinary, unit.Kind);
            Assert.Equal(f.Session, Assert.Single(unit.Participants).Session);
            Assert.Same(f.Connection, unit.Participants[0].Connection);
            Assert.Equal(CommandFootprintMode.None, unit.Footprint);
        });
        async ValueTask Selection(ushort dungeon, bool oldSequence)
        {
            var body = BitConverter.GetBytes(dungeon);
            var metadata = new GameMessageMetadata(1, 0xF, 0, 7) { InboundOrdinal = 7, SequenceIsFresh = !oldSequence }.WithRawBody(body);
            var decoder = f.Services.GetServices<GameMessageDecoderRegistration>().Single(value => value.Selector == 1 && value.Type == 0xF);
            var message = ((IGameMessageDecoder)f.Services.GetRequiredService(decoder.DecoderType)).Decode(metadata, body)!;
            await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection), message, default);
        }
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Task42_back_to_village_commits_tower_departure_before_actual_host_encoding_and_isolates_only_failed_peer(bool failedEncoder)
    {
        var units=new List<CommandWorkUnitDeclaration>();
        using var f=Fixture(configure:s=>{s.AddSingleton<Dfo.Server.Abstractions.Sessions.ISelectedCharacterCurrentViewDomainService,Task39NoReturnView>();Task42UnitObserver.Install(s,units);});
        SelectedSessionIdentity peer=null!;BossTowerSnapshot run=null!;
        await TestGameplay.RunAsync(f.Services,async()=>
        {f.SetLevel(65);await f.Seed(65,2749110,3);peer=await AddMember(f,1);Assert.True(f.Services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(peer.ConnectionId,out var user));f.Sender.Outbound.Connections.Add(user!.Connection);run=(await Tower(f).EnterAsync([new(f.Session,0),new(peer,1)],f.Session,42,default)).Run!;});
        var attempts=0;var cause=new IOException("task42 host encoding after tower departure");
        if(failedEncoder)f.Sender.Outbound.Fail(peer.ConnectionId,m=>m.MessageType==ServerMessageType.GameUdpHost,()=>
        {attempts++;Assert.Null(Tower(f).Find(f.Session));Assert.Equal(peer,Assert.Single(Tower(f).Find(peer)!.Members).Session);return cause;});
        // Fail is installed only for the failure case; the other branch exercises a normal host projection.
        var metadata=new GameMessageMetadata(1,0x84,0,7){InboundOrdinal=7,SequenceIsFresh=true}.WithRawBody([]);
        var decoder=f.Services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==1&&x.Type==0x84);
        var message=((IGameMessageDecoder)f.Services.GetRequiredService(decoder.DecoderType)).Decode(metadata,[])!;
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),message,default);
        TestGameplay.Run(f.Services,()=>{Assert.Null(Tower(f).Find(f.Session));Assert.Equal(peer,Assert.Single(Tower(f).Find(peer)!.Members).Session);});
        var unit=Assert.Single(units,d=>d.Name=="party-leave-boss-tower");
        Assert.Equal(CommandWorkUnitKind.Ordinary,unit.Kind);Assert.Equal(CommandFootprintMode.DeclaredNonRequest,unit.Footprint);
        Assert.Equal(new[]{f.Session,peer},unit.Participants.Select(p=>p.Session));Assert.Equal(new[]{f.Session,peer},unit.FootprintOwners!);
        Assert.Same(f.Connection,unit.Participants[0].Connection);Assert.Null(unit.Participants[1].Connection);
        if(failedEncoder){Assert.Equal(1,attempts);Assert.Equal([peer.ConnectionId],f.Sender.Outbound.Closes);Assert.DoesNotContain(f.Sender.Messages,m=>m.MessageType==ServerMessageType.GameUdpHost);}
        else {Assert.Empty(f.Sender.Outbound.Closes);Assert.Contains(f.Sender.Messages,m=>m.MessageType==ServerMessageType.GameUdpHost);}
    }
}

internal sealed class Task42UnitObserver(ICommandWorkUnits inner,IServiceProvider provider,List<CommandWorkUnitDeclaration> declarations):ICommandWorkUnits
{
    internal static void Install(IServiceCollection services,List<CommandWorkUnitDeclaration> declarations)
    {
        var d=services.Last(x=>x.ServiceType==typeof(ICommandWorkUnits));
        services.Remove(d);services.AddScoped<ICommandWorkUnits>(p=>new Task42UnitObserver((ICommandWorkUnits)(d.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,d.ImplementationType!)),p,declarations));
    }
    public async ValueTask<T> WriteAsync<T>(CommandExecutionContext context,CommandWorkUnitDeclaration declaration,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> action,CancellationToken token=default)where T:notnull
    {
        declarations.Add(declaration);DomainTransaction? transaction=null;
        var result=await inner.WriteAsync(context,declaration,(unit,ct)=>{transaction=Assert.IsType<DomainTransaction>(provider.GetRequiredService<IDomainTransactionAccessor>().Current);Assert.NotNull(transaction);return action(unit,ct);},token);
        Assert.Equal(DomainTransactionCompletion.Committed,transaction!.Completion);return result;
    }
    public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext context,CommandWorkUnitDeclaration declaration,Func<CommandWorkUnit,CancellationToken,ValueTask<T>> action,CancellationToken token=default)where T:notnull=>inner.WriteResultAsync(context,declaration,action,token);
    public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext context,CommandWorkUnitDeclaration declaration,Func<CancellationToken,ValueTask<T>> action,CancellationToken token=default)where T:notnull=>inner.ReadOnlyAsync(context,declaration,action,token);
}
