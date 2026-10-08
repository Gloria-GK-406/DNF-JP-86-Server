using System.Buffers.Binary;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Achievements;
using Dfo.Server.Protocol.Game.TitleBook;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

public sealed class TitleBookProtocolTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Store_and_withdraw_decode_five_i32_and_always_acknowledge_the_named_category(bool store)
    {
        var body = new byte[20];
        int[] fields = [3, 13, 26596, 2, 17];
        for(var i=0;i<5;i++) BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(i*4),fields[i]);
        var decoder = store ? (IGameMessageDecoder)new StoreTitleDecoder() : new WithdrawTitleDecoder();
        var request=Assert.IsType<TitleBookMutationRequest>(decoder.Decode(new(1,store?(ushort)0x019C:(ushort)0x019D,0,1),body));
        Assert.True(request.Complete); Assert.Equal(fields,new int[] {request.Space,request.Slot,request.ItemId,request.Category,request.Index});
        var success=new TitleBookMutationResponse(store,3,13,2,17).Body();
        Assert.Equal(17,success.Length); Assert.Equal(1,success[0]);
        Assert.Equal(13,BinaryPrimitives.ReadInt32LittleEndian(success.AsSpan(5)));
        var failure=new TitleBookMutationResponse(store,3,13,2,17,1).Body();
        Assert.Equal(10,failure.Length); Assert.Equal(new byte[]{0,1},failure[..2]);
        Assert.Equal(3,BinaryPrimitives.ReadInt32LittleEndian(failure.AsSpan(2)));
        Assert.Equal(2,BinaryPrimitives.ReadInt32LittleEndian(failure.AsSpan(6)));
        Assert.False(Assert.IsType<TitleBookMutationRequest>(decoder.Decode(new(1,0x019C,0,1),body[..19])).Complete);
    }
    [Fact]
    public void Achievement_reply_contains_remaining_counters_and_failure_still_contains_the_id()
    {
        var body=new byte[10]; BinaryPrimitives.WriteInt32LittleEndian(body,4303);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4),ushort.MaxValue);
        var request=Assert.IsType<AchievementTriggerRequest>(new AchievementTriggerDecoder().Decode(new(1,0x01A1,0,1),body));
        Assert.Equal(ushort.MaxValue,request.Delta1);
        var success=new AchievementTriggerResponse(4303,new(4303,27,2,1,0)).Body();
        Assert.Equal(11,success.Length);Assert.Equal(27,BinaryPrimitives.ReadUInt16LittleEndian(success.AsSpan(5)));
        var failure=new AchievementTriggerResponse(4303,null).Body();
        Assert.Equal(6,failure.Length); Assert.Equal(4303,BinaryPrimitives.ReadInt32LittleEndian(failure.AsSpan(2)));
    }
    [Fact]
    public void Title_list_uses_upstream_empty_extension_and_unlock_matches_native_prefix()
    {
        var body=StateSynchronizationPayloadCodec.EncodeTitleBook(new(0,0,0,[new(2,26596,0,12,47,0,123,1,3,91)]));
        Assert.Equal(37,body.Length); // header11 + upstream entry26; virtualized tail remains unverified.
        Assert.Equal(1,BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(7)));
        Assert.Equal(123,BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(25)));
        Assert.Equal(0,BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(33)));
        var unlock=new TitleUnlockedMessage(new(new(1),6501,0,2,26596)).EncodeBody();
        Assert.Equal(18,unlock.Length); Assert.Equal(26596,BinaryPrimitives.ReadInt32LittleEndian(unlock.AsSpan(12)));
        Assert.Equal(2,BinaryPrimitives.ReadUInt16LittleEndian(unlock.AsSpan(16)));
    }
    [Fact]
    public async Task Mutation_projection_waits_for_ack_and_never_turns_storage_into_an_unlock()
    {
        var gate=new AchievementTitleProjectionGate(); var events=new RecordingEvents();
        var subscriber=new TitleBookEventSubscriber(events,gate);
        var held=await gate.EnterAsync(new(1),default);
        var projecting=subscriber.HandleAsync(new DomainEventEnvelope<TitleStored>(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,Guid.NewGuid(),new(new(1),0,2,new(BusinessInventorySpace.CarriedEquipment,0))),default).AsTask();
        Assert.False(projecting.IsCompleted); Assert.Empty(events.Projected);
        held.Dispose(); await projecting.WaitAsync(TimeSpan.FromSeconds(2));
        var notice=Assert.IsType<TitleBookChanged>(Assert.Single(events.Projected));
        Assert.Null(notice.Unlock); Assert.NotNull(notice.ChangedSlot);
    }
    [Fact]
    public async Task Achievement_progress_projection_waits_until_request_ack_releases_the_character_gate()
    {
        var gate = new AchievementTitleProjectionGate(); var events = new RecordingEvents();
        var subscriber = new AchievementEventSubscriber(events, gate);
        var held = await gate.EnterAsync(new(1), default);
        var task = subscriber.HandleAsync(new DomainEventEnvelope<AchievementProgressChanged>(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), new(new(1), 4303)), default).AsTask();
        Assert.False(task.IsCompleted); Assert.Empty(events.Projected);
        held.Dispose(); await task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<AchievementChanged>(Assert.Single(events.Projected));
    }
    private sealed class RecordingEvents : IEventService
    {
        public List<object> Projected { get; }=[];
        public ValueTask<Guid> EnqueueAsync<T>(T payload,CancellationToken ct=default) where T:class,IDomainEvent=>throw new NotSupportedException();
        public ValueTask PublishAsync<T>(T payload,CancellationToken ct=default) where T:class,IDomainEvent=>throw new NotSupportedException();
        public ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience,T notice,CancellationToken ct=default) where T:class,INotification<T> {Projected.Add(notice);return ValueTask.FromResult(new NotificationDelivery([]));}
    }
}
