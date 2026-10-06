using System.Buffers.Binary;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Joust;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
namespace Dfo.Server.Protocol.Tests.Game.Joust;
public sealed class JoustProtocolTests
{
    [Theory]
    [InlineData(JoustPhase.Betting,JoustLifecycleEventKind.RoundStarted,1,0)]
    [InlineData(JoustPhase.Locked,JoustLifecycleEventKind.PhaseChanged,1,0)]
    [InlineData(JoustPhase.QuarterFinal,JoustLifecycleEventKind.PhaseChanged,1,1)]
    [InlineData(JoustPhase.SemiFinal,JoustLifecycleEventKind.PhaseChanged,0,1)]
    [InlineData(JoustPhase.Final,JoustLifecycleEventKind.PhaseChanged,0,0)]
    [InlineData(JoustPhase.Result,JoustLifecycleEventKind.RoundEnded,1,1)]
    public void Announcement_packets_are_reserved_for_lifecycle_transitions(
        JoustPhase phase,JoustLifecycleEventKind kind,int states,int results)
    {
        var state=State(phase);
        var messages=JoustPayloadCodec.Lifecycle(state,kind);
        Assert.Equal(states,messages.OfType<JoustStateMessage>().Count());
        Assert.Equal(results,messages.OfType<JoustResultsMessage>().Count());
        Assert.DoesNotContain(JoustPayloadCodec.Snapshot(state),m=>m is JoustStateMessage or JoustResultsMessage);
        if(kind==JoustLifecycleEventKind.RoundEnded)
            Assert.Equal(3,Assert.Single(messages.OfType<JoustResultsMessage>()).EncodeBody()[2]);
    }
    private static JoustState State(JoustPhase phase=JoustPhase.QuarterFinal)=>new(true,DateTimeOffset.UnixEpoch,phase,"round",null,null,null,
        new[]{0,1,2,3,4,5,6,8}.Select(i=>new JoustKnightState(new(i,"knight",0,new int[28],new int[28]),10,i,2.5m)).ToArray(),
        [1,2,5,8,2,8,8],36,1000,17){DailyRoundNo=7};
    [Fact] public void Bet_decoder_keeps_i32_slot_and_does_not_use_opaque_prefix_as_identity()
    {
        var b=Enumerable.Repeat((byte)0xAA,23).ToArray();b[14]=11;BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(15),70000);BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(19),1000);
        var q=Assert.IsType<JoustBetRequest>(new JoustBetDecoder().Decode(new(1,0x04E7,0,1),b));Assert.Equal(70000,q.MaterialSlotIndex);Assert.Equal(1000,q.Amount);Assert.Equal(11,q.KnightIndex);
        foreach(var n in new[]{14,22,24})Assert.Throws<InvalidDataException>(()=>new JoustBetDecoder().Decode(GameMessageMetadata.Empty,new byte[n]));
        foreach(var type in new ushort[]{0x04E6,0x04E8}){Assert.IsType<JoustQueryRequest>(new JoustQueryDecoder().Decode(new(1,type,0,1),new byte[14]));Assert.Throws<InvalidDataException>(()=>new JoustQueryDecoder().Decode(new(1,type,0,1),new byte[15]));}
    }
    [Fact] public void Notifications_match_fixed_client_strides_and_four_byte_match_records()
    {
        var s=State();Assert.Equal(new byte[]{7,0,4},JoustPayloadCodec.State(s));var info=JoustPayloadCodec.Info(s);Assert.Equal(90,info.Length);Assert.Equal(8,info[79]);Assert.Equal(2.5f,BinaryPrimitives.ReadSingleLittleEndian(info.AsSpan(81)));
        Assert.Equal(1,JoustPayloadCodec.Info(s with{Phase=JoustPhase.Betting})[89]);Assert.Equal(0,info[89]);
        var bets=JoustPayloadCodec.Bets(s);Assert.Equal(46,bets.Length);Assert.Equal(36,BinaryPrimitives.ReadInt32LittleEndian(bets.AsSpan(2)));Assert.Equal(8,bets[41]);Assert.Equal(8,BinaryPrimitives.ReadInt32LittleEndian(bets.AsSpan(42)));
        var result=JoustPayloadCodec.Results(s with{Winners=s.Winners[..4]});Assert.Equal(31,result.Length);Assert.Equal(new byte[]{1,0,0,0},result[3..7]);Assert.Equal(new byte[]{8,0,6,0},result[15..19]);Assert.Equal(255,result[19]);
        Assert.Equal(new byte[]{8,0,2,0},JoustPayloadCodec.Results(s with{Phase=JoustPhase.Result})[27..31]);
    }
    [Fact] public void History_has_five_byte_ack_and_exactly_500_records_with_zero_terminator()
    {
        var b=JoustPayloadCodec.History(0,[new("saved",DateTimeOffset.UnixEpoch,11,3.4m){DailyRoundNo=3}]);Assert.Equal(3505,b.Length);Assert.Equal(new byte[]{1,0,0,0,0,3,0,11},b[..8]);Assert.Equal(3.4f,BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(8)));Assert.Equal(0,BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(12)));Assert.Equal(new byte[]{1,6,0,0,0},JoustPayloadCodec.Ack(6));Assert.Equal(3505,JoustPayloadCodec.History(6,[]).Length);
    }
    [Theory]
    [InlineData(JoustPhase.Betting)]
    [InlineData(JoustPhase.Locked)]
    [InlineData(JoustPhase.QuarterFinal)]
    [InlineData(JoustPhase.SemiFinal)]
    [InlineData(JoustPhase.Final)]
    [InlineData(JoustPhase.Result)]
    public void Entry_initializes_native_state_once_without_replaying_results(JoustPhase phase)
    {
        var result=SelectCharacterPayloadTestData.CreateComplete() with{Events=new([],WeeklyAttendanceState.Empty,State(phase))};
        var messages=SelectedCharacterStateMessageFactory.Create(result);Assert.Empty(messages.OfType<JoustResultsMessage>());Assert.Single(messages.OfType<JoustInfoMessage>());Assert.Single(messages.OfType<JoustBetsMessage>());
        var snapshot=Assert.Single(messages.OfType<JoustStateMessage>());
        Assert.Equal(new byte[]{7,0,JoustPayloadCodec.NativeState(State(phase))},snapshot.EncodeBody());
        Assert.True(messages.ToList().IndexOf(snapshot)<messages.ToList().IndexOf(Assert.Single(messages.OfType<JoustInfoMessage>())));
    }
    [Fact] public void Disabled_entry_clears_native_state_with_zero()
    {
        var state=State() with{Active=false};
        var messages=JoustPayloadCodec.Initialization(state);
        Assert.Equal(new byte[]{7,0,0},Assert.Single(messages.OfType<JoustStateMessage>()).EncodeBody());
        Assert.DoesNotContain(messages,m=>m is JoustResultsMessage);
    }
}
