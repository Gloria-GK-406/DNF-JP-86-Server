using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.CollectionBox;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests;

public sealed class CollectionBoxCodecTests
{
    [Fact]
    public void Native_requests_have_exact_lengths_and_separate_reserved_bytes()
    {
        var put = Assert.IsType<CollectionBoxRequest>(new CollectionBoxPutDecoder().Decode(GameMessageMetadata.Empty,[3,0,0x21,1,0x78,0x56,0x34,0x12]));
        Assert.Equal(3, put.BoxId); Assert.Equal(289, put.SourceSlot); Assert.Equal(0x12345678, put.ItemId);
        var take = Assert.IsType<CollectionBoxRequest>(new CollectionBoxWithdrawDecoder().Decode(GameMessageMetadata.Empty,[2,0x78,0x56,0x34,0x12]));
        Assert.Equal(0x12345678, take.ItemId);
        var extend = Assert.IsType<CollectionBoxRequest>(new CollectionBoxExtendDecoder().Decode(GameMessageMetadata.Empty,[1,0,65,0,1,0,0,0]));
        Assert.Equal(CollectionBoxAction.Extend, extend.Action);
        var select = Assert.IsType<CollectionBoxRequest>(new CollectionBoxSelectDecoder().Decode(GameMessageMetadata.Empty,[4]));
        Assert.Equal(4, select.BoxId);
        Assert.Equal(ReplayProjectionNeeds.CollectionBox,select.MinimumReplayNeeds);
        foreach (var decoder in new Dfo.Server.Abstractions.Networking.IGameMessageDecoder[] {
            new CollectionBoxSelectDecoder(),new CollectionBoxPutDecoder(),new CollectionBoxWithdrawDecoder(),new CollectionBoxExtendDecoder() })
        {
            Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, []));
            Assert.Throws<InvalidDataException>(() => decoder.Decode(GameMessageMetadata.Empty, new byte[9]));
        }
        Assert.Throws<InvalidDataException>(() => new CollectionBoxPutDecoder().Decode(GameMessageMetadata.Empty,[1,1,65,0,1,0,0,0]));
        new ReplayProjectionRequest(put.MinimumReplayNeeds, []).Validate();
    }
    [Theory]
    [InlineData(CollectionBoxAction.Select,2)] [InlineData(CollectionBoxAction.Put,1)] [InlineData(CollectionBoxAction.Withdraw,1)]
    public void Ack_is_the_common_envelope_plus_only_the_native_success_payload(CollectionBoxAction action,int length)
    {
        var frame = Assert.Single(new CollectionBoxAckEncoder().Encode(new CollectionBoxAck(action,3)));
        Assert.Equal(0x03E5+(int)action,BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(length,frame.Length-15); Assert.Equal(1,frame[15]);
        if(action==CollectionBoxAction.Select) Assert.Equal(3,frame[16]);
        Assert.Equal(new byte[]{0,0x15},Assert.Single(new CollectionBoxAckEncoder().Encode(new CollectionBoxAck(action,3,0x15)))[15..]);
    }
    [Fact]
    public void Permanent_snapshot_fills_every_box_then_repeats_only_the_selected_box_as_active()
    {
        var state = new CollectionBoxSnapshot(2,[new(1,[100]),new(2,[200,201]),new(4,[])],[]);
        var frames = CollectionBoxContentsMessage.Full(state).Select(x => x.EncodeBody()).ToArray();
        Assert.Equal(new byte[]{1,2,4,2},frames.Select(x=>x[0]));
        Assert.Equal(new byte[]{0,0,0,1},frames.Select(x=>x[1]));
        Assert.All(frames,b=>{Assert.Equal(0U,BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(2)));Assert.Equal(1,b[6]);Assert.Equal(8+4*b[7],b.Length);});
        Assert.Equal(new byte[]{2,1,0,0,0,0,1,2,200,0,0,0,201,0,0,0},frames[^1]);
        var wire=Assert.Single(new CollectionBoxContentsEncoder().Encode(new CollectionBoxContentsMessage(state.Boxes[0],false)));
        Assert.Equal(0x03EB,BinaryPrimitives.ReadUInt16LittleEndian(wire.AsSpan(1)));
        Assert.Equal(frames[0],wire[15..]);
    }
    [Fact]
    public void Compatibility_0381_frames_ignore_progress_and_remain_fixed()
    {
        foreach(var id in new byte[]{1,2})
        {
            var frame = new CollectionBoxCompatibilityMessage(id);
            Assert.Equal(0x0381,frame.Type);
            Assert.Equal(new byte[]{id,1,255,255,255,255,0,0},frame.EncodeBody());
        }
    }
    [Fact]
    public void Entry_appends_all_real_series_and_restores_persisted_selection_after_fixed_compatibility_frames()
    {
        var result = Dfo.Server.Protocol.Tests.Game.SelectCharacter.SelectCharacterPayloadTestData.CreateComplete() with
        { CollectionBoxes = new(4,[new(1,[100]),new(2,[]),new(4,[200])],[]) };
        var messages = Dfo.Server.Protocol.Game.SelectCharacter.SelectedCharacterStateMessageFactory.Create(result);
        var boxes = messages.OfType<CollectionBoxContentsMessage>().ToArray();
        Assert.Equal(new byte[]{1,2,4,4},boxes.Select(x=>x.Box.BoxId));
        Assert.True(boxes[^1].Selected);
        Assert.Equal(2,messages.OfType<CollectionBoxCompatibilityMessage>().Count());
        Assert.All(messages,x=>Assert.NotNull(x.EncodeBody()));
    }
}
