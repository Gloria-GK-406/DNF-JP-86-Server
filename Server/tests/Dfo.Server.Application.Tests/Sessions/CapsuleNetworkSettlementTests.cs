using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol.Game.MarriageHousing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed partial class HousingNetworkSettlementTests
{
    [Fact]
    public async Task Gift_notification_commits_independently_of_sender_ack_and_offline_spouse_recovers_it()
    {
        using var f=Fixture();var relation=await Marry(f);
        f.Sql($"INSERT INTO marriage_capsules VALUES('{relation:N}',1,0,1,0)");
        var counter=new GameInboundFrameCounter();
        CapsuleRequest Request()
        {
            var (ordinal,fresh)=counter.Observe(1);
            return new((new GameMessageMetadata(1,0x0405,0,1){InboundOrdinal=ordinal,SequenceIsFresh=fresh}).WithRawBody([]),MarriageCapsuleAction.Give);
        }
        var dispatch=f.Services.GetRequiredService<INetworkMessageDispatcher>();f.Sender.FailBatch=true;
        await dispatch.DispatchAsync(new(f.Connection),Request(),default);
        Assert.Equal(0,f.Scalar("SELECT owned FROM marriage_capsules WHERE character_id=1"));
        Assert.Equal(1,f.Scalar("SELECT pending FROM marriage_capsules WHERE character_id=3"));
        Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM domain_events e JOIN domain_event_deliveries d USING(event_id) WHERE e.event_type='marriage.capsule-gift' AND d.subscriber_id='marriage.capsule-gift-notification'"));
        f.Sender.FailBatch=false;await dispatch.DispatchAsync(new(f.Connection),Request(),default);
        Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='marriage.capsule-gift'"));
        Assert.Equal((byte)1,(await f.Services.GetRequiredService<IMarriageCapsuleQueryDomainService>().ReadAsync(new(2),new(3),default)).State.Pending);
    }
    [Fact]
    public async Task Capsule_lost_ack_replays_absolute_state_without_making_a_second_capsule()
    {
        using var f = Fixture(); var relation = await Marry(f);
        f.Sql($"INSERT INTO marriage_capsules VALUES('{relation:N}',1,10,0,0)");
        var counter = new GameInboundFrameCounter();
        CapsuleRequest Request()
        {
            var (ordinal,fresh) = counter.Observe(1);
            return new((new GameMessageMetadata(1,0x0404,0,1) {InboundOrdinal=ordinal,SequenceIsFresh=fresh}).WithRawBody([]), MarriageCapsuleAction.Make);
        }
        var dispatch=f.Services.GetRequiredService<INetworkMessageDispatcher>(); f.Sender.FailBatch=true;
        await dispatch.DispatchAsync(new(f.Connection), Request(), default);
        Assert.Equal(1,f.Scalar("SELECT owned FROM marriage_capsules WHERE character_id=1"));
        f.Sql("UPDATE marriage_capsules SET energy=10 WHERE character_id=1");
        f.Sender.FailBatch=false; f.Sender.Messages.Clear();
        await dispatch.DispatchAsync(new(f.Connection), Request(), default);
        Assert.Equal(1,f.Scalar("SELECT owned FROM marriage_capsules WHERE character_id=1"));
        Assert.Equal(10,f.Scalar("SELECT energy FROM marriage_capsules WHERE character_id=1"));
        Assert.DoesNotContain(f.Sender.Messages,x=>x.GetType().Name=="CapsuleAck");
        Assert.Contains(f.Sender.Messages,x=>x.GetType().Name=="CapsuleStateMessage");
    }
    [Fact]
    public async Task Capsule_full_bag_rolls_back_pending_gift_and_leaves_session_usable()
    {
        using var f=Fixture(); var relation=await Marry(f);
        f.Sql($"INSERT INTO marriage_capsules VALUES('{relation:N}',1,0,0,1)");
        foreach (var slot in Enumerable.Range(3,6).Concat(Enumerable.Range(65,55))) await f.Seed(slot,1000,1000);
        var counter=new GameInboundFrameCounter(); var (ordinal,fresh)=counter.Observe(1);
        var request=new CapsuleRequest((new GameMessageMetadata(1,0x0406,0,1) {InboundOrdinal=ordinal,SequenceIsFresh=fresh}).WithRawBody([]),MarriageCapsuleAction.Take);
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection), request, default);
        var ack=Assert.Single(f.Sender.Messages,x=>x.GetType().Name=="CapsuleAck");
        Assert.Equal(-6,Assert.IsType<MarriageCapsuleResult>(ack.GetType().GetProperty("Result")!.GetValue(ack)).Status);
        Assert.Equal(1,f.Scalar("SELECT pending FROM marriage_capsules WHERE character_id=1")); Assert.Equal(0,f.Count(10002910));
        await f.Services.GetRequiredService<Dfo.Server.Application.Commands.ICommandBus>().SendAsync(new InitializeHousingCommand(f.Session),default);
    }
}
