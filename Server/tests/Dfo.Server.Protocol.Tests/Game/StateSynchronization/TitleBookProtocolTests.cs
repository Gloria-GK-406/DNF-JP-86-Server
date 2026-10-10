using System.Buffers.Binary;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Achievements;
using Dfo.Server.Protocol.Game;
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
    public void Title_appearance_request_and_reply_use_native_0239_shape_without_inventory_movement()
    {
        var request = Assert.IsType<SetTitleAppearanceRequest>(new SetTitleAppearanceDecoder().Decode(new(1, 0x0239, 0, 1), BitConverter.GetBytes(26628)));
        Assert.True(request.Complete); Assert.Equal(26628, request.ItemId);
        Assert.False(Assert.IsType<SetTitleAppearanceRequest>(new SetTitleAppearanceDecoder().Decode(new(1, 0x0239, 0, 1), new byte[3])).Complete);
        Assert.Equal(new byte[] { 1 }.Concat(BitConverter.GetBytes(26628)), new SetTitleAppearanceResponse(true, 26628).Body());
        Assert.Equal(new byte[] { 0, 1 }, new SetTitleAppearanceResponse(false, 0).Body());
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
        Assert.Equal(2, notice.ChangedIndex);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mirror_removal_projection_keeps_generic_move_reconciliation_and_can_skip_carried_inventory_for_book_operations(bool refreshCarriedInventory)
    {
        var events=new RecordingEvents();var gate=new AchievementTitleProjectionGate();var subscriber=new TitleBookEventSubscriber(events,gate);
        using var held=await gate.EnterAsync(new(1),default);
        var delivery=subscriber.HandleAsync(new DomainEventEnvelope<Dfo.Server.Abstractions.Inventory.ItemMirrorRemoved>(Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,Guid.NewGuid(),new(new(1),InventoryContainerType.Equipment) { RefreshCarriedInventory = refreshCarriedInventory }),default).AsTask();
        Assert.Equal(!refreshCarriedInventory, delivery.IsCompleted);Assert.Empty(events.Projected);
        held.Dispose();await delivery.WaitAsync(TimeSpan.FromSeconds(2));
        if (!refreshCarriedInventory) { Assert.Empty(events.Projected); return; }
        var notice=Assert.IsType<TitleBookChanged>(Assert.Single(events.Projected));
        Assert.Equal(refreshCarriedInventory, notice.RefreshCarriedInventory);Assert.Equal(InventoryContainerType.Equipment,notice.ChangedContainer);Assert.Null(notice.Unlock);
        Assert.Null(notice.Category); Assert.Null(notice.ChangedIndex);
    }

    [Fact]
    public void Mirror_binding_extension_follows_the_native_list_and_is_omitted_for_other_users()
    {
        var state = new CharacterTitleBookCategoryState(4, 0, 0, [new(10, 26596, 17, 12, 47, 0, 123, 1, 3, 91)])
        { MirrorProjectionEnabled = true, WearSource = new(4, 10, 26596) };
        var body = StateSynchronizationPayloadCodec.EncodeTitleBook(state);
        Assert.Equal(61, body.Length);
        Assert.Equal(0x314D4254, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(37)));
        Assert.Equal(new[] { 1, 4, 10, 26596, 24 }, Enumerable.Range(0, 5).Select(i => BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(41 + i * 4))));
        Assert.Equal(body[..37], StateSynchronizationPayloadCodec.EncodeTitleBook(state with { InfoType = 1, OwnerId16 = 0 })[0..37].Select((x, i) => i == 0 ? (byte)0 : x));
        var none = StateSynchronizationPayloadCodec.EncodeTitleBook(state with { WearSource = null });
        Assert.Equal(new[] { -1, -1, 0 }, Enumerable.Range(0, 3).Select(i => BinaryPrimitives.ReadInt32LittleEndian(none.AsSpan(45 + i * 4))));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Title_mutations_use_silent_canonical_lists_without_gain_or_unlock_messages(bool fullBook)
    {
        var titles = new CurrentTitles();
        var handler = new TitleBookChangedHandler(titles, null!, null!, null!);
        var recipient = new NotificationRecipient(new(1), new("Game:1"));
        var messages = await handler.ComposeAsync(recipient, new(new(1), 4, ChangedIndex: 10, RefreshBook: fullBook), default);
        Assert.Equal(fullBook ? 5 : 1, messages.Count);
        Assert.All(messages, x => Assert.IsType<TitleBookStateMessage>(x));
        var book = ((TitleBookStateMessage)messages.Last()).Category;
        Assert.Equal(4, book.Category); Assert.Equal(2, book.Entries.Count);
        Assert.Equal(new(4, 10, 26596), book.WearSource);
        Assert.All(messages, x => Assert.Equal(0x0166, ((GameStateSynchronizationMessage)x).Type));
        Assert.Equal(1, titles.Reads);
        Assert.Empty(await handler.ComposeAsync(recipient, new(new(1)), default));
        Assert.Empty(await handler.ComposeAsync(new(new(2), new("Game:1")), new(new(1), 4), default));
        Assert.Equal(1, titles.Reads);
    }
    private sealed class CurrentTitles : ITitleBookDomainService
    {
        public int Reads { get; private set; }
        public ValueTask<IReadOnlyList<CharacterTitleBookCategoryState>> GetAsync(CharacterId character, CancellationToken ct)
        {
            Assert.Equal(new CharacterId(1), character); Reads++;
            return ValueTask.FromResult<IReadOnlyList<CharacterTitleBookCategoryState>>(Enumerable.Range(0, 5).Select(i => new CharacterTitleBookCategoryState((byte)i, 0, 0,
                i == 4 ? [new(10, 26596, 0, 0, 0, 0, 0, 0, 0, 0), new(11, 26596, 0, 0, 0, 0, 0, 0, 0, 0)] : [])
                { MirrorProjectionEnabled = true, WearSource = new(4, 10, 26596) }).ToArray());
        }
        public ValueTask<bool> SetAppearanceAsync(CharacterId character, int appearanceItemId, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<InventoryItemState>> GetItemsAsync(CharacterId character, int category, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask PrepareAsync(CharacterId character, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<TitleBookMutationResult> StoreAsync(CharacterId character, BusinessSlot source, int itemId, int category, int index, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<TitleBookMutationResult> WithdrawAsync(CharacterId character, BusinessSlot destination, int itemId, int category, int index, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<TitleBookMutationResult> EquipCarriedTitleReplacingMirrorAsync(CharacterId character, BusinessSlot source, int itemId, int category, int index, CancellationToken ct) => throw new NotSupportedException();
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
        public ValueTask DirectAsync<T>(T payload,CancellationToken ct=default) where T:class,IDomainEvent=>throw new NotSupportedException();
        public ValueTask<NotificationDelivery> ProjectAsync<T>(NotificationAudience audience,T notice,CancellationToken ct=default) where T:class,INotification<T> {Projected.Add(notice);return ValueTask.FromResult(new NotificationDelivery([]));}
    }
}
