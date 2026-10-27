using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol.Game.CollectionBox;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class CollectionBoxNetworkSettlementTests
{
    [Theory]
    [InlineData(1000,2000,0x12)] [InlineData(9999,1000,0x15)]
    public async Task Wrong_source_item_and_item_outside_the_box_use_the_documented_protocol_refusals(int requested,int held,byte code)
    {
        using var f=new ShopFixture(s=>s.AddSingleton<ICollectionBoxDefinitionSource,Definitions>());
        await f.Seed(65,held,2);
        var counter=new GameInboundFrameCounter();var (ordinal,fresh)=counter.Observe(1);
        byte[] body=[1,0,65,0,0,0,0,0];BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4),requested);
        var request=new CollectionBoxRequest((new GameMessageMetadata(1,0x03E6,0,1){InboundOrdinal=ordinal,SequenceIsFresh=fresh}).WithRawBody(body),CollectionBoxAction.Put,1,65,requested);
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),request,default);
        Assert.Equal(new byte[]{0,code},Wire(f,Assert.Single(f.Sender.Messages))[15..]);
        Assert.Equal(2,f.Count(held));Assert.Equal(0,f.Scalar("SELECT COUNT(*) FROM item_slots WHERE list_type=241"));
    }
    [Fact]
    public async Task Replayed_selection_restores_current_boxes_without_inventory_or_old_ack()
    {
        using var f=new ShopFixture(s=>s.AddSingleton<ICollectionBoxDefinitionSource,Definitions>());
        var counter=new GameInboundFrameCounter();
        CollectionBoxRequest Request()
        {
            var (ordinal,fresh)=counter.Observe(1);
            return new((new GameMessageMetadata(1,0x03E5,0,1){InboundOrdinal=ordinal,SequenceIsFresh=fresh}).WithRawBody([4]),CollectionBoxAction.Select,4);
        }
        var dispatcher=f.Services.GetRequiredService<INetworkMessageDispatcher>();
        f.Sender.FailBatch=true;
        await Assert.ThrowsAsync<IOException>(async()=>await dispatcher.DispatchAsync(new(f.Connection),Request(),default));
        Assert.Equal(4,f.Scalar("SELECT selected_box_index FROM character_collectbox_state WHERE character_id=1"));
        f.Sql("UPDATE character_collectbox_state SET selected_box_index=1 WHERE character_id=1");
        f.Sender.FailBatch=false;f.Sender.Batches.Clear();f.Sender.Messages.Clear();
        await dispatcher.DispatchAsync(new(f.Connection),Request(),default);
        var frames=Assert.Single(f.Sender.Batches).Frames;
        Assert.Equal(3,frames.Length);
        Assert.All(frames,b=>Assert.Equal(0x03EB,BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(1))));
        Assert.Equal(1,frames[^1][15]);Assert.Equal(1,frames[^1][16]);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Protocol_maps_timed_or_locked_business_rejection_without_changing_the_item(bool timed)
    {
        using var f=new ShopFixture(s=>s.AddSingleton<ICollectionBoxDefinitionSource,Definitions>());
        await f.SeedCore(65,StackableItemCore.Create(2,1000,2) with { ExpireTime=timed?1:0,SortLockFlag=timed?(byte)0:(byte)1 });
        var counter=new GameInboundFrameCounter();var (ordinal,fresh)=counter.Observe(1);
        byte[] body=[1,0,65,0,0xE8,3,0,0];
        var request=new CollectionBoxRequest((new GameMessageMetadata(1,0x03E6,0,1){InboundOrdinal=ordinal,SequenceIsFresh=fresh}).WithRawBody(body),CollectionBoxAction.Put,1,65,1000);
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),request,default);
        Assert.Equal(new byte[]{0,0x17},Wire(f,Assert.Single(f.Sender.Messages))[15..]);
        Assert.Equal(2,f.Count(1000));
    }
    [Fact]
    public async Task Ack_cannot_follow_a_reused_connection_id_after_the_inventory_batch()
    {
        var transport = new ReusingConnectionSender();
        using var f = new ShopFixture(s =>
        {
            s.AddSingleton<ICollectionBoxDefinitionSource,Definitions>();
        });
        transport.Use(f.Sender.Outbound);
        transport.Current = f.Connection;
        transport.Replacement = f.Connection with { };
        await f.Seed(65,1000,2);
        var counter=new GameInboundFrameCounter();var (ordinal,fresh)=counter.Observe(1);
        byte[] body=[1,0,65,0,0xE8,3,0,0];
        var request=new CollectionBoxRequest((new GameMessageMetadata(1,0x03E6,0,1){InboundOrdinal=ordinal,SequenceIsFresh=fresh}).WithRawBody(body),CollectionBoxAction.Put,1,65,1000);
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),request,default);
        var ack=Assert.Single(transport.Delivered,x=>x.Message.MessageType==ServerMessageType.GameCollectionBox);
        Assert.Same(f.Connection,ack.Connection);
        Assert.DoesNotContain(transport.Delivered,x=>ReferenceEquals(x.Connection,transport.Replacement));
    }
    /// <summary>
    /// The reusable-ID lookup, through the real registration (TASK-25): once a prepared batch is registered the ID
    /// names <see cref="Replacement"/> in the connection table, so a later send by ID would be registered for it.
    /// <see cref="Delivered"/> is what was registered, with the exact context each entry was registered for.
    /// </summary>
    private sealed class ReusingConnectionSender
    {
        private RecordedOutbound outbound = null!;
        public NetworkConnectionContext Current { get; set; } = null!;
        public NetworkConnectionContext Replacement { get; set; } = null!;
        public List<(NetworkConnectionContext Connection,IServerNetworkMessage Message)> Delivered =>
            [.. outbound.Entries.Where(entry => !entry.IsClose).SelectMany(entry => entry.Messages.Select(message => (entry.Connection, message)))];

        public void Use(RecordedOutbound recorded)
        {
            outbound = recorded;
            recorded.BeforeSend = () =>
            {
                if (recorded.Batches.Count == 0 || ReferenceEquals(Current, Replacement)) return;
                recorded.Connections.Remove(Current.ConnectionId);
                recorded.Connections.Add(Replacement);
                Current = Replacement;
            };
        }
    }
    private sealed class Definitions : ICollectionBoxDefinitionSource
    {
        private static readonly CollectionBoxDefinition[] Boxes = [
            new(1,1,1,null,null,[new(0,0,1000,1)],[]), new(4,1,1,null,null,[new(0,0,1000,1)],[])];
        public ValueTask<IReadOnlyList<CollectionBoxDefinition>> GetAllAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<CollectionBoxDefinition>>(Boxes);
        public ValueTask<CollectionBoxDefinition?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult(Boxes.SingleOrDefault(x => x.BoxIndex == id));
    }
    [Fact]
    public async Task Committed_put_with_lost_delivery_replays_current_box_and_inventory_without_moving_a_second_item()
    {
        using var f = new ShopFixture(s => s.AddSingleton<ICollectionBoxDefinitionSource,Definitions>());
        await f.Seed(65,1000,3);
        var counter = new GameInboundFrameCounter();
        CollectionBoxRequest Request()
        {
            var (ordinal,fresh)=counter.Observe(1);
            byte[] body=[1,0,65,0,0xE8,3,0,0];
            return new((new GameMessageMetadata(1,0x03E6,0,1) { InboundOrdinal=ordinal,SequenceIsFresh=fresh }).WithRawBody(body),CollectionBoxAction.Put,1,65,1000);
        }
        var dispatcher=f.Services.GetRequiredService<INetworkMessageDispatcher>();
        f.Sender.FailBatch=true;
        await Assert.ThrowsAsync<IOException>(async()=>await dispatcher.DispatchAsync(new(f.Connection),Request(),default));
        Assert.Equal(2,f.Scalar("SELECT count FROM item_slots WHERE owner_id=1 AND list_type=0 AND slot_index=65"));
        Assert.Equal(1,f.Scalar("SELECT count FROM item_slots WHERE owner_id=1 AND list_type=241 AND slot_index=256"));
        // Fresh facts changed after the lost response; a retry restores these, never an old receipt.
        f.Sql("UPDATE character_collectbox_state SET selected_box_index=4 WHERE character_id=1");
        f.Sender.FailBatch=false;f.Sender.Messages.Clear();f.Sender.Batches.Clear();
        await dispatcher.DispatchAsync(new(f.Connection),Request(),default);
        Assert.Equal(3,f.Count(1000));
        Assert.Equal(2,f.Scalar("SELECT count FROM item_slots WHERE owner_id=1 AND list_type=0 AND slot_index=65"));
        Assert.DoesNotContain(f.Sender.Messages,x=>x.MessageType==ServerMessageType.GameCollectionBox);
        var frames=Assert.Single(f.Sender.Batches).Frames;
        var boxes=frames.Where(b=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(1))==0x03EB).ToArray();
        Assert.Equal(new byte[]{1,4,4},boxes.Select(b=>b[15]));
        Assert.Equal(new byte[]{0,0,1},boxes.Select(b=>b[16]));
        Assert.Contains(frames,b=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(1))==0x000D);
    }
    [Fact]
    public async Task Put_publishes_inventory_then_box_then_ack_and_wrong_box_or_extension_cannot_spend_items()
    {
        using var f = new ShopFixture(s=>s.AddSingleton<ICollectionBoxDefinitionSource,Definitions>());
        await f.Seed(65,1000,2);
        var counter=new GameInboundFrameCounter();
        async Task Send(ushort opcode,CollectionBoxAction action,byte box,ushort sequence)
        {
            var (ordinal,fresh)=counter.Observe(sequence);
            byte[] b=action==CollectionBoxAction.Withdraw?[box,0xE8,3,0,0]:[box,0,65,0,0xE8,3,0,0];
            var request=new CollectionBoxRequest((new GameMessageMetadata(1,opcode,0,sequence){InboundOrdinal=ordinal,SequenceIsFresh=fresh}).WithRawBody(b),action,box,65,1000);
            await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),request,default);
        }
        await Send(0x03E6,CollectionBoxAction.Put,1,1);
        var delivered=f.Sender.Messages.ToArray();
        Assert.Equal(0x000E,BinaryPrimitives.ReadUInt16LittleEndian(Wire(f,delivered[0]).AsSpan(1)));
        Assert.Equal(0x03EB,BinaryPrimitives.ReadUInt16LittleEndian(Wire(f,delivered[1]).AsSpan(1)));
        Assert.Equal(new byte[]{1},Wire(f,delivered[^1])[15..]);
        f.Sender.Messages.Clear();
        await Send(0x03E7,CollectionBoxAction.Withdraw,4,2);
        Assert.Equal(new byte[]{0,0x15},Wire(f,Assert.Single(f.Sender.Messages,x=>x.MessageType==ServerMessageType.GameCollectionBox))[15..]);
        f.Sender.Messages.Clear();
        await Send(0x03E8,CollectionBoxAction.Extend,1,3);
        Assert.Equal(new byte[]{0,0x5F},Wire(f,Assert.Single(f.Sender.Messages,x=>x.MessageType==ServerMessageType.GameCollectionBox))[15..]);
        Assert.Equal(2,f.Count(1000));
        Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM item_slots WHERE list_type=241"));
    }
    private static byte[] Wire(ShopFixture f, IServerNetworkMessage message)
    {
        var registration=f.Services.GetServices<ServerMessageEncoderRegistration>().Single(x=>x.MessageType==message.GetType());
        return Assert.Single(((IServerMessageEncoder)f.Services.GetRequiredService(registration.EncoderType)).Encode(message));
    }

}
