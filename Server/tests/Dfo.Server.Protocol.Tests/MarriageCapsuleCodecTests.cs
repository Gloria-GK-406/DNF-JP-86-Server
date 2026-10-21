using System.Buffers.Binary;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MarriageHousing;

namespace Dfo.Server.Protocol.Tests;

public sealed class MarriageCapsuleCodecTests
{
    private sealed class CapsuleQuery : Dfo.Server.Abstractions.Marriage.IMarriageCapsuleQueryDomainService
    {
        public ValueTask<MarriageCapsuleSnapshot> ReadAsync(Dfo.Server.Data.Accounts.AccountId account,Dfo.Server.Data.Characters.CharacterId character,CancellationToken ct) =>
            ValueTask.FromResult(new MarriageCapsuleSnapshot(new(0,1,0)));
    }
    [Fact]
    public async Task Delayed_capsule_notifications_read_current_state_and_ignore_replaced_sessions()
    {
        var services=new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        new Dfo.Server.Application.ApplicationModule().ConfigureServices(services,new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        using var provider=Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var sessions=Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>(provider);
        var state=Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<Dfo.Server.Application.Users.OnlineStateRegistry>(provider);
        var actor=Dfo.Server.Testing.TestSessions.InWorld(sessions,state,new(1),new(1),"capsule");
        var recipient=new Dfo.Server.Abstractions.Notifications.NotificationRecipient(actor.CharacterId,actor.ConnectionId,actor);
        var handler=new CapsuleEnergyRefreshHandler(sessions,new CapsuleQuery());
        Assert.Equal(0,Assert.IsType<CapsuleEnergyMessage>(Assert.Single(await handler.ComposeAsync(recipient,new(10),default))).Energy);
        var refreshed=Assert.IsType<CapsuleStateMessage>(Assert.Single(await new CapsuleRefreshHandler(sessions,new CapsuleQuery()).ComposeAsync(recipient,new(new(new(10,7,3))),default)));
        Assert.Equal(new(0,1,0),refreshed.Snapshot.State);
        _=Dfo.Server.Testing.TestSessions.ReselectOnSameConnection(sessions,state,actor);
        Assert.Empty(await handler.ComposeAsync(recipient,new(10),default));
    }
    [Theory]
    [InlineData(MarriageCapsuleAction.Make,0,10)] [InlineData(MarriageCapsuleAction.Make,-4,5)]
    [InlineData(MarriageCapsuleAction.Give,0,6)] [InlineData(MarriageCapsuleAction.Give,-6,5)]
    [InlineData(MarriageCapsuleAction.Take,0,6)] [InlineData(MarriageCapsuleAction.Take,-6,5)]
    public void Native_capsule_ack_consumes_common_envelope_before_status(MarriageCapsuleAction action,int status,int length)
    {
        var frame=Assert.Single(new CapsuleAckEncoder().Encode(new CapsuleAck(action,new(status) {State=new(8,3,2)})));
        Assert.Equal(0x0404+(int)action,BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(length,frame.Length-15); Assert.Equal(1,frame[15]); Assert.Equal(status,BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16)));
        if(status!=0)return;
        if(action==MarriageCapsuleAction.Make){Assert.Equal(8,BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(20)));Assert.Equal(3,frame[24]);}
        else Assert.Equal(action==MarriageCapsuleAction.Give?3:2,frame[20]);
    }
    [Fact]
    public void Capsule_requests_are_empty_and_snapshots_have_native_field_order()
    {
        var request=Assert.IsType<CapsuleRequest>(new CapsuleGiveDecoder().Decode(GameMessageMetadata.Empty,[]));
        new ReplayProjectionRequest(request.MinimumReplayNeeds,[]).Validate();
        Assert.Throws<InvalidDataException>(()=>new CapsuleTakeDecoder().Decode(GameMessageMetadata.Empty,[0]));
        var frame=Assert.Single(new CapsuleStateEncoder().Encode(new CapsuleStateMessage(new(new(8,3,2),true))));
        Assert.Equal(new byte[]{8,0,0,0,3,2,1},frame[15..]);
        var energy=Assert.Single(new CapsuleEnergyEncoder().Encode(new CapsuleEnergyMessage(10))); Assert.Equal(new byte[]{10,0,0,0},energy[15..]);
    }
}
