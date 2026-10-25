using Dfo.Server.Application.Tests.Support;
using System.Reflection;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.World;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class TownMovementProtocolCommandFlowTests
{
    private static Type? OldArea => typeof(SetUserAreaHandler).Assembly.GetType("Dfo.Server.Protocol.Game.TownProjection.Task41OldSetUserAreaHandler");
    [Fact]
    public async Task Real_wire_crossing_then_same_area_step_keep_current_location_and_one_durable_crossing()
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        using var f = Fixture(delivery);var physical=delivery.Add(f.Connection);
        Assert.Same(f.Services.GetRequiredService<ISelectedSessionPositionPersistence>(),f.Services.GetRequiredService<ICharacterLocationDomainService>());
        await Area(f,11,3,123,456); await delivery.Settled();
        Assert.Equal(new ushort[] {0x17,0x18},Frames(physical.Sends).Select(Type));
        Assert.Equal(11,f.Scalar("SELECT town_id FROM characters WHERE character_id=1"));
        await Area(f,11,3,130,460,8);
        await Wire(f,0x23,[140,0,240,0,2,107,0],9); await delivery.Settled();
        Assert.Equal(new ushort[] {0x17,0x18,0x17,0x18,0x16},Frames(physical.Sends).Select(Type));
        Assert.Equal(new byte[] {140,0,240,0,2,107,0},Frames(physical.Sends).Last()[17..]);
        TestGameplay.Run(f.Services,()=>
        {
            var runtime=f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().PositionOf(f.Session);
            Assert.Equal(new CharacterPositionState(11,3,140,240,2),runtime);
            Assert.Equal(runtime,f.Services.GetRequiredService<ITownAreaDomainService>().Find(f.Session.CharacterId)!.Position);
        });
        // A same-area report updates live state, but only crossing writes the next-login place.
        Assert.Equal(123,f.Scalar("SELECT pos_x FROM characters WHERE character_id=1"));
        Assert.Equal(0,physical.CloseCalls);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Action_or_native_commit_failure_keeps_old_membership_and_position_without_frames(bool native)
    {
        await using var delivery = new RemainingOpeningProtocolCommandFlowTests.SenderProbe();
        var transactions = new SqliteTransactionProbe();
        using var f=Fixture(delivery, transactions.Configure);var physical=delivery.Add(f.Connection);
        f.Sql(native?"CREATE TABLE task41_area_parent(id INTEGER PRIMARY KEY); CREATE TABLE task41_area_child(id INTEGER REFERENCES task41_area_parent(id) DEFERRABLE INITIALLY DEFERRED); CREATE TRIGGER task41_area_fail AFTER UPDATE OF town_id ON characters BEGIN INSERT INTO task41_area_child VALUES(404); END":"CREATE TRIGGER task41_area_fail BEFORE UPDATE OF town_id ON characters BEGIN SELECT RAISE(ABORT,'area fail'); END");
        var failure=await Assert.ThrowsAsync<SqliteException>(()=>Area(f,11,3,123,456).AsTask());
        Assert.Equal(native?787:1811,failure.SqliteExtendedErrorCode);if(native)SqliteFailureAssertions.FromCommit(Assert.Single(transactions.Transactions), failure);
        Assert.Equal(0,f.Scalar("SELECT town_id FROM characters WHERE character_id=1"));
        TestGameplay.Run(f.Services,()=>
        { Assert.Equal((byte)0,f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().PositionOf(f.Session)!.TownId);Assert.Equal(0,f.Services.GetRequiredService<ITownAreaDomainService>().Find(f.Session.CharacterId)!.Area.TownId); });
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await delivery.Settled();
        Assert.Empty(physical.Sends);Assert.Equal(1,physical.CloseCalls);Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
    }
    [Fact]
    public async Task Required_actual_encoder_failure_after_area_commit_keeps_empty_selected_protection_and_closes_source()
    {
        await using var delivery=new RemainingOpeningProtocolCommandFlowTests.SenderProbe();using var f=Fixture(delivery);var physical=delivery.Add(f.Connection);
        var cause=new IOException("TASK41 area actual encoder");f.Sender.Outbound.Encoder.Fail((c,_)=>{Assert.Same(f.Connection,c);return cause;});
        var failure=await Record.ExceptionAsync(()=>Area(f,11,3,123,456).AsTask());
        if(OldArea is not null)Assert.Same(cause,failure);else Assert.Same(cause,Assert.IsType<TransactionOutcomeUncertainException>(failure).InnerException);
        Assert.Equal(11,f.Scalar("SELECT town_id FROM characters WHERE character_id=1"));
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await delivery.Settled();Assert.Empty(physical.Sends);Assert.Equal(1,physical.CloseCalls);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Replaced_town_member_is_never_moved_by_area_or_position(bool area)
    {
        using var f=TeleportConsumableTests.Fixture();
        TestGameplay.Run(f.Services,()=>{var replacement=f.Session with{ConnectionGeneration=f.Session.ConnectionGeneration+1};f.Services.GetRequiredService<ITownAreaDomainService>().Enter(new(f.Session.CharacterId,replacement.TownAreaLease(),new(1,0,0),new(0,0,12,34,5),true));});
        if(area)await Area(f,11,3,123,456);else await Wire(f,0x23,[140,0,240,0,2,107,0],7);
        Assert.Empty(f.Sender.Messages);Assert.Equal(0,f.Scalar("SELECT town_id FROM characters WHERE character_id=1"));
        TestGameplay.Run(f.Services,()=>{Assert.Equal((byte)0,f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().PositionOf(f.Session)!.TownId);Assert.False(f.Session.Owns(f.Services.GetRequiredService<ITownAreaDomainService>().Find(f.Session.CharacterId)!));});
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Housing_return_is_remembered_after_the_ordinary_position_is_durably_committed(bool failRemember)
    {
        ShopFixture f=null!;var remember=0;
        using(f=TeleportConsumableTests.Fixture(s=>
        {
            ConfigureOldArea(s);
            s.AddSingleton<ITownMovementDomainService>(new Towns());
            s.AddSingleton<IMarriageHousingQueryDomainService>(Proxy<IMarriageHousingQueryDomainService>.Create((name,args)=>name switch
            { "IsEntrySource"=>true,"InstanceAsync"=>ValueTask.FromResult<int?>(42),"ReadAsync"=>ValueTask.FromResult<HousingSnapshot?>(null),_=>throw new NotSupportedException(name) }));
            var d=s.Last(x=>x.ServiceType==typeof(IMarriageHousingSessionDomainService));
            s.AddSingleton<IMarriageHousingSessionDomainService>(p=>new HousingObserver((IMarriageHousingSessionDomainService)(d.ImplementationInstance??d.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,d.ImplementationType!)),()=>
            {
                Assert.Null(p.GetRequiredService<IDomainTransactionAccessor>().Current);
                Assert.Equal(12,f.Scalar("SELECT pos_x FROM characters WHERE character_id=1"));
                remember++;if(failRemember)throw new IOException("remember after durable save");
            }));
        }))
        {
            var failure=await Record.ExceptionAsync(()=>Area(f,101,0,500,600).AsTask());Assert.Equal(1,remember);
            Assert.Equal(12,f.Scalar("SELECT pos_x FROM characters WHERE character_id=1"));Assert.Equal(0,f.Scalar("SELECT town_id FROM characters WHERE character_id=1"));
            var sessions=f.Services.GetRequiredService<IMarriageHousingSessionDomainService>();
            if(failRemember)
            {
                if(OldArea is not null)Assert.IsType<IOException>(failure);else Assert.IsType<TransactionOutcomeUncertainException>(failure);
                Assert.Null(sessions.ReturnFor(f.Session));Assert.Empty(f.Sender.Messages);
                TestGameplay.Run(f.Services,()=>Assert.Equal((byte)0,f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().PositionOf(f.Session)!.TownId));
            }
            else
            {
                Assert.Null(failure);Assert.Equal(new CharacterPositionState(0,0,12,34,5),sessions.ReturnFor(f.Session));
                TestGameplay.Run(f.Services,()=>Assert.Equal(42,f.Services.GetRequiredService<ITownAreaDomainService>().Find(f.Session.CharacterId)!.Area.Instance));
                await Area(f,11,3,30,40,8);Assert.NotNull(sessions.ReturnFor(f.Session));
                TestGameplay.Run(f.Services,()=>Assert.Equal((byte)101,f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().PositionOf(f.Session)!.TownId));
                await Area(f,0,0,30,40,9);Assert.Null(sessions.ReturnFor(f.Session));Assert.Equal(30,f.Scalar("SELECT pos_x FROM characters WHERE character_id=1"));
            }
        }
    }
    [Theory] [InlineData("source")] [InlineData("instance")] [InlineData("area")] [InlineData("cross")] [InlineData("leave")]
    public async Task Housing_and_wedding_refusals_restore_old_location_without_a_durable_write(string reason)
    {
        using var f=TeleportConsumableTests.Fixture(s=>
        {
            ConfigureOldArea(s);s.AddSingleton<ITownMovementDomainService>(new Towns());
            s.AddSingleton<IMarriageHousingQueryDomainService>(Proxy<IMarriageHousingQueryDomainService>.Create((name,args)=>name switch
            {"IsEntrySource"=>reason!="source","InstanceAsync"=>ValueTask.FromResult<int?>(reason=="instance"?null:42),_=>throw new NotSupportedException(name)}));
            s.AddSingleton<IMarriageQueryDomainService>(Proxy<IMarriageQueryDomainService>.Create((name,args)=>name switch
            {"CanCrossAsync"=>ValueTask.FromResult(reason!="cross"),"CanLeaveAsync"=>ValueTask.FromResult(reason!="leave"),_=>throw new NotSupportedException(name)}));
        });
        await Area(f,101,reason=="area"?(byte)1:(byte)0,500,600);
        Assert.Equal(new[]{ServerMessageType.GameUserArea,ServerMessageType.GameServerNotice},f.Sender.Messages.Select(m=>m.MessageType));
        Assert.Equal(0,f.Scalar("SELECT town_id FROM characters WHERE character_id=1"));Assert.Equal(0,f.Scalar("SELECT pos_x FROM characters WHERE character_id=1"));
        TestGameplay.Run(f.Services,()=>Assert.Equal((byte)0,f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().PositionOf(f.Session)!.TownId));
    }
    [Fact]
    public async Task Leaving_a_started_wedding_restores_appearance_after_the_complete_town_transfer()
    {
        var church=new MarriageChurchSnapshot(new(1,Guid.NewGuid(),1,9,8,DateTimeOffset.UnixEpoch,DateTimeOffset.MaxValue),new(2),[],[],new(9,8,1,2));
        using var f=TeleportConsumableTests.Fixture(s=>
        {
            ConfigureOldArea(s);s.AddSingleton<IMarriageQueryDomainService>(Proxy<IMarriageQueryDomainService>.Create((name,args)=>name switch
            {"CanCrossAsync" or "CanLeaveAsync"=>ValueTask.FromResult(true),"AppearanceAsync"=>ValueTask.FromResult<MarriageChurchSnapshot?>(church),"WeddingPhaseAsync"=>ValueTask.FromResult<MarriageWeddingPhase?>(MarriageWeddingPhase.Completed),_=>throw new NotSupportedException(name)}));
        });
        await Area(f,11,3,123,456);
        Assert.Equal(new[]{ServerMessageType.GameUserArea,ServerMessageType.GameAreaUsers,ServerMessageType.GameWeddingAvatar},f.Sender.Messages.Select(m=>m.MessageType));
    }
    private sealed class Towns:ITownMovementDomainService
    {public ValueTask<bool> DeclaresAreaAsync(int town,int area,CancellationToken ct)=>ValueTask.FromResult(true);public ValueTask<bool> SharesTownPresenceAsync(int town,int area,CancellationToken ct)=>ValueTask.FromResult(true);public ValueTask<bool> CanEnterTownAsync(ChannelEntryIdentity channel,int town,CancellationToken ct)=>ValueTask.FromResult(true);}
    private sealed class HousingObserver(IMarriageHousingSessionDomainService inner,Action remember):IMarriageHousingSessionDomainService
    {public void CloseExact(SelectedSessionIdentity actor)=>inner.CloseExact(actor);public void RememberReturn(SelectedSessionIdentity actor,CharacterPositionState pos){remember();inner.RememberReturn(actor,pos);}public CharacterPositionState? ReturnFor(SelectedSessionIdentity actor)=>inner.ReturnFor(actor);}
    public class Proxy<T>:DispatchProxy where T:class
    {private Func<string,object?[]?,object?> call=null!;internal static T Create(Func<string,object?[]?,object?> call){var p=Create<T,Proxy<T>>();((Proxy<T>)(object)p).call=call;return p;}protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>call(targetMethod!.Name,args);}
    private static void ConfigureOldArea(IServiceCollection s)
    {
        if(OldArea is not {} old)return;
        foreach(var d in s.Where(d=>d.ServiceType==typeof(CommandHandlerRegistration)&&d.ImplementationInstance is CommandHandlerRegistration r&&r.CommandType.Name=="GameSetUserAreaCommand").ToArray())s.Remove(d);
        var registration=s.Single(d=>d.ServiceType==typeof(Dfo.Server.Application.Dispatching.MessageHandlerRegistration)&&d.ImplementationInstance is Dfo.Server.Application.Dispatching.MessageHandlerRegistration r&&r.MessageType==ClientMessageType.GameSetUserArea);
        s.Remove(registration);s.AddSingleton(new Dfo.Server.Application.Dispatching.MessageHandlerRegistration(ClientMessageType.GameSetUserArea,old));s.AddTransient(old);
    }
    private static ShopFixture Fixture(RemainingOpeningProtocolCommandFlowTests.SenderProbe delivery, Action<IServiceCollection>? configure = null)=>TeleportConsumableTests.Fixture(s=>
    {
      ConfigureOldArea(s);
      configure?.Invoke(s);
      s.AddSingleton<IOutboundSender>(delivery.Recorder);s.AddSingleton(p=>new GameplayOutboxes(p.GetRequiredService<IOutboundSender>(),p.GetRequiredService<Dfo.Server.Application.Execution.GameplayCloseCauses>(),p.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GameplayOutbox>>())); });
    private static ValueTask Area(ShopFixture f,byte town,byte area,short x,short y,ushort sequence=7)=>Wire(f,0x24,[town,area,(byte)x,(byte)(x>>8),(byte)y,(byte)(y>>8)],sequence);
    private static ValueTask Wire(ShopFixture f,ushort type,byte[] body,ushort sequence)
    {
        var metadata=new GameMessageMetadata(1,type,0,sequence){InboundOrdinal=sequence,SequenceIsFresh=true}.WithRawBody(body);
        var registration=f.Services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==1&&x.Type==type);
        var message=((IGameMessageDecoder)f.Services.GetRequiredService(registration.DecoderType)).Decode(metadata,body)!;
        return f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),message,default);
    }
    private static ushort Type(byte[] f)=>BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(1));
    private static IEnumerable<byte[]> Frames(IEnumerable<byte[]> sends){foreach(var bytes in sends){var offset=0;while(offset<bytes.Length){var length=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset+3));yield return bytes.AsSpan(offset,length).ToArray();offset+=length;}}}
}
