using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
namespace Dfo.Server.Protocol.Game.Joust;

internal static class JoustPayloadCodec
{
    internal static byte NativeState(JoustState s) => !s.Active ? (byte)0 : (byte)(s.Phase switch
    { JoustPhase.Betting => 1, JoustPhase.Locked => 3, JoustPhase.Result => 2, _ => 4 });
    internal static byte[] State(JoustState s)
    { var b=new byte[3];BinaryPrimitives.WriteUInt16LittleEndian(b,s.DailyRoundNo);b[2]=NativeState(s);return b; }
    // A21 JoustStateSync compatibility extension: update the native manager/UI,
    // but bypass the reader's announcement branch. Native lifecycle bytes stay unchanged.
    internal static byte[] StateSnapshot(JoustState s)
    { var b=State(s);b[2]|=0x80;return b; }
    internal static byte[] Info(JoustState s)
    {
        var b=new byte[90];BinaryPrimitives.WriteUInt16LittleEndian(b,s.DailyRoundNo);
        for(var i=0;i<8;i++)
        { var p=2+i*11;b[p]=255;if(i>=s.Knights.Length)continue;var k=s.Knights[i];b[p]=checked((byte)k.Knight.Index);
          // Condition and win/loss counters have no current server source; retain zero values.
          BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(p+2),(float)k.Odds);
          b[p+10]=k.Knight.Index>=8 && s.Phase is JoustPhase.Betting or JoustPhase.Locked ? (byte)1 : (byte)0; }
        return b;
    }
    internal static byte[] Bets(JoustState s)
    {
        var b=new byte[46];BinaryPrimitives.WriteUInt16LittleEndian(b,s.DailyRoundNo);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(2),s.PersonalTotal);
        for(var i=0;i<8;i++)
        {var p=6+i*5;b[p]=255;if(i>=s.Knights.Length)continue;b[p]=checked((byte)s.Knights[i].Knight.Index);
         BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(p+1),s.Knights[i].PersonalBet);}return b;
    }
    internal static byte[] Results(JoustState s)
    {
        var b=new byte[31];BinaryPrimitives.WriteUInt16LittleEndian(b,s.DailyRoundNo);
        b[2]=s.Phase switch {JoustPhase.SemiFinal=>1,JoustPhase.Final=>2,JoustPhase.Result=>3,_=>0};
        for(var i=0;i<7;i++){b[3+i*4]=255;b[5+i*4]=255;}
        var entrants=s.Knights.Select(k=>k.Knight.Index).ToArray();var offset=0;
        for(var size=4;size>=1;size/=2)
        {
            if(entrants.Length!=size*2 || s.Winners.Length<offset+size)break;
            for(var i=0;i<size;i++)
            {var winner=s.Winners[offset+i];var left=entrants[i*2];var right=entrants[i*2+1];
             if(winner!=left && winner!=right)throw new InvalidDataException("Joust outcome is outside its saved bracket.");
             var p=3+(offset+i)*4;b[p]=checked((byte)winner);b[p+2]=checked((byte)(winner==left?right:left));
             // Bytes +1/+3 are Win/Loss PVF content indices, not padding; first option is server policy.
            }
            entrants=s.Winners.Skip(offset).Take(size).ToArray();offset+=size;
        }return b;
    }
    internal static byte[] Ack(uint result)
    {var b=new byte[5];b[0]=1;BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(1),result);return b;}
    internal static byte[] History(uint result,JoustHistoryEntry[] entries)
    {
        var b=new byte[3505];b[0]=1;BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(1),result);
        if(result!=0)return b;
        foreach(var (entry,i) in entries.Take(500).Select((e,i)=>(e,i)))
        {if(entry.DailyRoundNo==0)throw new InvalidDataException("Joust history needs a nonzero saved schedule ordinal.");
         var p=5+i*7;BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p),entry.DailyRoundNo);b[p+2]=checked((byte)entry.WinnerIndex);
         BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(p+3),(float)entry.Odds);}return b;
    }
    // Both 04B8 and terminal 04BB have client announcement side effects.
    // Query, bet update and replay contain neither. Entry/configuration refresh
    // additionally initialize state through the explicitly silent compatibility extension.
    internal static GameStateSynchronizationMessage[] Snapshot(JoustState s) =>
        [new JoustBetsMessage(s),new JoustInfoMessage(s)];
    internal static GameStateSynchronizationMessage[] EntrySnapshot(JoustState s) =>
        [new JoustStateSnapshotMessage(s),..Snapshot(s)];
    internal static GameStateSynchronizationMessage[] Lifecycle(JoustState s,JoustLifecycleEventKind kind) => kind switch
    {
        JoustLifecycleEventKind.RoundStarted => [new JoustStateMessage(s)],
        JoustLifecycleEventKind.RoundEnded => [new JoustStateMessage(s),new JoustResultsMessage(s)],
        JoustLifecycleEventKind.PhaseChanged when !s.Active || s.Phase==JoustPhase.Locked => [new JoustStateMessage(s)],
        JoustLifecycleEventKind.PhaseChanged when s.Phase==JoustPhase.QuarterFinal => [new JoustStateMessage(s),new JoustResultsMessage(s)],
        JoustLifecycleEventKind.PhaseChanged when s.Phase==JoustPhase.SemiFinal => [new JoustResultsMessage(s)],
        // Native Stage2 can enter scene12 and announce the winner. Reveal the complete
        // bracket only through the once-only RoundEnded/Stage3 event instead.
        _ => [],
    };
}
internal sealed record JoustStateMessage(JoustState State):GameStateSynchronizationMessage,IGameNotiMessage
{internal override ushort Type=>0x04B8;internal override byte[] EncodeBody()=>JoustPayloadCodec.State(State);}
internal sealed record JoustStateSnapshotMessage(JoustState State):GameStateSynchronizationMessage,IGameNotiMessage
{internal override ushort Type=>0x04B8;internal override byte[] EncodeBody()=>JoustPayloadCodec.StateSnapshot(State);}
internal sealed record JoustInfoMessage(JoustState State):GameStateSynchronizationMessage,IGameNotiMessage
{internal override ushort Type=>0x04B9;internal override byte[] EncodeBody()=>JoustPayloadCodec.Info(State);}
internal sealed record JoustBetsMessage(JoustState State):GameStateSynchronizationMessage,IGameNotiMessage
{internal override ushort Type=>0x04BA;internal override byte[] EncodeBody()=>JoustPayloadCodec.Bets(State);}
internal sealed record JoustResultsMessage(JoustState State):GameStateSynchronizationMessage,IGameNotiMessage
{internal override ushort Type=>0x04BB;internal override byte[] EncodeBody()=>JoustPayloadCodec.Results(State);}
public sealed record JoustQueryRequest(GameMessageMetadata Metadata):IClientNetworkMessage
{public ClientMessageType MessageType=>Metadata.Type==0x04E8?ClientMessageType.GameJoustHistory:ClientMessageType.GameJoustInfo;}
internal sealed class JoustQueryDecoder:IGameMessageDecoder
{public IClientNetworkMessage Decode(GameMessageMetadata m,ReadOnlySpan<byte> b)=>b.Length==14?new JoustQueryRequest(m):throw new InvalidDataException("Joust query requires 14 opaque bytes.");}
public sealed record JoustBetRequest(GameMessageMetadata Metadata,byte KnightIndex,int MaterialSlotIndex,int Amount):IEffectSettlementRequest
{public ClientMessageType MessageType=>ClientMessageType.GameJoustBet;public ReplayProjectionNeeds MinimumReplayNeeds=>ReplayProjectionNeeds.Inventory|ReplayProjectionNeeds.Joust;}
internal sealed class JoustBetDecoder:IGameMessageDecoder
{public IClientNetworkMessage Decode(GameMessageMetadata m,ReadOnlySpan<byte> b)=>b.Length==23?new JoustBetRequest(m,b[14],BinaryPrimitives.ReadInt32LittleEndian(b[15..]),BinaryPrimitives.ReadInt32LittleEndian(b[19..])):throw new InvalidDataException("Joust bet requires 23 bytes.");}
public sealed record JoustResponse(ushort Type,uint Result,JoustHistoryEntry[]? History=null):IGameCmdMessage
{public ServerMessageType MessageType=>ServerMessageType.GameStateSynchronization;}
internal sealed class JoustResponseEncoder:GameCmdMessageEncoder<JoustResponse>
{protected override IReadOnlyList<GameMessagePayload> EncodePayloads(JoustResponse m)=>OnePayload(m.Type,m.Type==0x04E8?JoustPayloadCodec.History(m.Result,m.History??[]):JoustPayloadCodec.Ack(m.Result));}
public sealed record QueryJoustStateCommand(SelectedSessionIdentity Session):ICommand<JoustState>,IAccountCommand,ISelectedSessionCommand
{public AccountId AccountId=>Session.AccountId;}
public sealed class QueryJoustStateCommandHandler(IJoustDomainService joust,TimeProvider clock):ICommandHandler<QueryJoustStateCommand,JoustState>
{public ValueTask<JoustState> HandleAsync(CommandExecutionContext c,QueryJoustStateCommand q,CancellationToken ct)=>joust.GetStateAsync(q.Session.CharacterId,clock.GetUtcNow(),ct);}
public sealed record QueryJoustHistoryCommand(SelectedSessionIdentity Session):ICommand<JoustHistoryEntry[]>,IAccountCommand,ISelectedSessionCommand
{public AccountId AccountId=>Session.AccountId;}
public sealed class QueryJoustHistoryCommandHandler(IJoustDomainService joust):ICommandHandler<QueryJoustHistoryCommand,JoustHistoryEntry[]>
{public ValueTask<JoustHistoryEntry[]> HandleAsync(CommandExecutionContext c,QueryJoustHistoryCommand q,CancellationToken ct)=>joust.GetHistoryAsync(ct);}
public sealed record JoustBetCommand(OnlineUserId OnlineUserId,SelectedSessionIdentity Session,string RequestId,int KnightIndex,BusinessSlot Slot,int Amount):ICommand<JoustBetResult>,IRequestSettlementCommand,IOnlineUserCommand,IAccountCommand,ISelectedSessionCommand
{public AccountId AccountId=>Session.AccountId;}
public sealed class JoustBetCommandHandler(IJoustDomainService joust,TimeProvider clock,IRequestReplayProjectionDomainService projection):ICommandHandler<JoustBetCommand,JoustBetResult>
{
    public async ValueTask<JoustBetResult> HandleAsync(CommandExecutionContext c,JoustBetCommand q,CancellationToken ct)
    {
        var result=await joust.BetAsync(q.Session,q.RequestId,q.KnightIndex,q.Slot,q.Amount,clock.GetUtcNow(),ct);
        projection.RecordInventorySlots(q.Session,result.Debits.Select(d=>d.Slot).ToArray());return result;
    }
}
public sealed class JoustQueryHandler(ISelectedSessionDomainService sessions,ICommandBus bus,IGameMessageSender sender,SelectedSessionTransitionGate transitions,SelectedSessionSafety safety):ClientMessageHandler<JoustQueryRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext c,JoustQueryRequest m,CancellationToken ct)
    {
        if(sessions.FindByConnection(c.Connection.ConnectionId) is not {} session){await sender.SendCmdAsync(c.Connection.ConnectionId,new JoustResponse(m.Metadata.Type,6),ct);return;}
        var recipient=safety.Capture(session);
        await using var lease=await transitions.BorrowOrAcquireExclusiveAsync(session.AccountId,ct);
        if(sessions.FindByConnection(c.Connection.ConnectionId)!=session || !ReferenceEquals(recipient.Connection,c.Connection))return;
        safety.DemandCurrent(recipient);
        if(m.Metadata.Type==0x04E8){var history=await bus.SendAsync(new QueryJoustHistoryCommand(session),ct);await sender.SendCmdAsync(c.Connection.ConnectionId,new JoustResponse(0x04E8,0,history),ct);}
        else
        {
            var state=await bus.SendAsync(new QueryJoustStateCommand(session),ct);
            if(!state.Active)await sender.SendCmdAsync(c.Connection.ConnectionId,new JoustResponse(0x04E6,6),ct);
            else await sender.SendBatchAsync(c.Connection,JoustPayloadCodec.Snapshot(state),ct);
        }
    }
}
public sealed class JoustBetHandler(ISelectedSessionDomainService sessions,IOnlineUserQueryDomainService users,ICommandBus bus,
    INotificationBus notices,IGameMessageSender sender,IReplayStateProjectionDelivery replay,ISelectedSessionSnapshotQuery selected,SelectedSessionTransitionGate transitions,SelectedSessionSafety safety):ClientMessageHandler<JoustBetRequest>
{
    private sealed class PhysicalIdentity { public string Id {get;}=Guid.NewGuid().ToString("N"); }
    private static readonly ConditionalWeakTable<NetworkConnectionContext,PhysicalIdentity> Identities=new();
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext c,JoustBetRequest m,CancellationToken ct)
    {
        if(sessions.FindByConnection(c.Connection.ConnectionId) is not {} session || !users.TryGetByConnection(c.Connection.ConnectionId,out var user) || user is null || m.Metadata.InboundOrdinal is not >0)
        {await sender.SendCmdAsync(c.Connection.ConnectionId,new JoustResponse(0x04E7,6),ct);return;}
        var recipient=safety.Capture(session);
        if(!ReferenceEquals(recipient.Connection,c.Connection))return;
        await using var lease=await transitions.BorrowOrAcquireExclusiveAsync(session.AccountId,ct);
        if(sessions.FindByConnection(c.Connection.ConnectionId)!=session)return;
        safety.DemandCurrent(recipient);
        JoustBetResult result;
        try {result=await bus.SendAsync(new JoustBetCommand(user.OnlineUserId,session,$"joust:{Identities.GetOrCreateValue(c.Connection).Id}:{session.ConnectionGeneration}:{m.Metadata.InboundOrdinal}",m.KnightIndex,new InventorySlotMappingRuleService().ToBusinessSlot(new(InventoryContainerType.Main,m.MaterialSlotIndex)),m.Amount),ct);}
        catch(Exception e) when(e is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(e))
        {await sender.SendCmdAsync(c.Connection.ConnectionId,new JoustResponse(0x04E7,6),ct);return;}
        if(result.Succeeded)await notices.PublishAsync(new NotificationAudience.TheseSessions(selected.Snapshot()),new JoustDetailsChanged(result.State.ServerTime),ct);
        if(result.Succeeded)await replay.RefreshAsync(c.Connection,session,new(ReplayProjectionNeeds.Inventory|ReplayProjectionNeeds.Joust,result.Debits.Select(d=>d.Slot).ToImmutableArray()),ct);
        safety.DemandCurrent(recipient);
        await sender.SendBatchAsync(c.Connection,[new JoustResponse(0x04E7,result.Succeeded?0u:6u)],ct);
    }
}
public sealed class JoustDetailsChangedHandler(IJoustProjectionDomainService joust):INotificationHandler<JoustDetailsChanged>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient r,JoustDetailsChanged n,CancellationToken ct)
    {
        var state=await joust.ReadAsync(r.CharacterId,n.Now,ct);
        return JoustPayloadCodec.Snapshot(state);
    }
}
public sealed class JoustLifecycleNotificationHandler:INotificationHandler<JoustLifecycleNotification>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient r,JoustLifecycleNotification n,CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(JoustPayloadCodec.Lifecycle(n.State,n.Kind));
}
