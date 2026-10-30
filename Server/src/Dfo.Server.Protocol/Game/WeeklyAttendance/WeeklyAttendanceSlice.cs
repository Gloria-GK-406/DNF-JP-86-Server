using Dfo.Server.Abstractions.DomainEvents;
using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
namespace Dfo.Server.Protocol.Game.WeeklyAttendance;

internal static class EventPayloadCodec
{
    // ClientCompatibility patches A21 SetCodePage to 65001; the running client consumes UTF-8.
    private static readonly Encoding CalendarEncoding=new UTF8Encoding(false,true);
    public static byte[] Information(IReadOnlyList<EventInfo> events)
    {
        using var buffer=new MemoryStream(); using var writer=new BinaryWriter(buffer,CalendarEncoding,true);
        void Text(string text) { var bytes=CalendarEncoding.GetBytes(text); writer.Write((uint)bytes.Length);writer.Write(bytes); }
        writer.Write(checked((ushort)events.Count));
        foreach (var e in events)
        {
            writer.Write(checked((ushort)e.EventId));writer.Write(0u);Text(e.StartNotice);Text(e.EndNotice);writer.Write((byte)(e.HasDetail ? 1 : 0));
            // A21 HasDetail=0 still inserts the identity, then advances directly to the next record.
            if (!e.HasDetail) continue;
            // A21 reader VA014196B0; flags 0/5 are the upstream seed, with semantics still unclosed.
            writer.Write((byte)0);writer.Write((byte)5);Text(e.Title);Text(e.ShortName);Text("");
            writer.Write(checked((uint)e.StartsAt.ToUnixTimeSeconds()));writer.Write(checked((uint)e.EndsAt.ToUnixTimeSeconds()));
            Text(e.LinkKey);Text(e.Description);writer.Write((byte)1);
        }
        writer.Write((byte)0);
        return buffer.ToArray();
    }
    public static byte[] Attendance(WeeklyAttendanceState state)
    {
        // A21 VA009A9B90 reads 450 bytes; bonus selectors and unconfirmed fields retain zero.
        var body=new byte[450];
        if (!state.Active) return body;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4),checked((uint)state.AttendanceCount));
        foreach (var (no,index) in state.ClaimedAttendance.Take(20).Select((no,i)=>(no,i))) body[0x198+index]=checked((byte)no);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x1B0),checked((uint)state.RecommendedClears));
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(0x1BC),checked((uint)state.CurrentAttendanceNo));
        body[0x1C0]=state.CanClaim ? (byte)1 : (byte)0;
        return body;
    }
    public static WeeklyAttendanceRequest Decode(GameMessageMetadata metadata,ReadOnlySpan<byte> body) => body.Length==14
        ? new(metadata,body.ToArray()) : throw new InvalidDataException("TOTAL_ATTENDANCE_CHECK requires exactly 14 opaque bytes.");
}
internal sealed record EventInformationMessage(EventInfo[] Information) : GameStateSynchronizationMessage,IGameNotiMessage
{ internal override ushort Type=>0x006C;internal override byte[] EncodeBody()=>EventPayloadCodec.Information(Information); }
internal sealed record WeeklyAttendanceStateMessage(WeeklyAttendanceState State) : GameStateSynchronizationMessage,IGameNotiMessage
{ internal override ushort Type=>0x042D;internal override byte[] EncodeBody()=>EventPayloadCodec.Attendance(State); }
internal sealed record ScheduledActivityStateMessage(ScheduledActivityState State) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x033F;
    internal override byte[] EncodeBody()
    {
        var body = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(body, checked((uint)State.Category));
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), State.Open ? 1u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8), checked((uint)State.ContentId));
        return body;
    }
}
internal sealed record ScheduledDungeonStateMessage(ScheduledDungeonState State) : GameStateSynchronizationMessage,IGameNotiMessage
{
    internal override ushort Type => 0x033F;
    internal override byte[] EncodeBody()
    {
        // A21 VA01192C10: category, state, content ID, opaque value; four u32.
        var body = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4),State.Open ? 1u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8),checked((uint)State.DungeonId));
        return body;
    }
}
internal sealed record ScheduledQuestStateMessage(ScheduledQuestState State) : GameStateSynchronizationMessage,IGameNotiMessage
{
    internal override ushort Type => 0x033F;
    internal override byte[] EncodeBody()
    {
        // A21 VA011C2550 selects schedule category 1, consumed by VA00E4F660.
        var body = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(body,1u);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4),State.Open ? 1u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(8),checked((uint)State.QuestId));
        return body;
    }
}
public sealed record WeeklyAttendanceRequest(GameMessageMetadata Metadata,byte[] Opaque) : IClientNetworkMessage
{ public ClientMessageType MessageType=>ClientMessageType.GameWeeklyAttendance; }
internal sealed class WeeklyAttendanceDecoder : IGameMessageDecoder
{ public IClientNetworkMessage? Decode(GameMessageMetadata metadata,ReadOnlySpan<byte> body)=>EventPayloadCodec.Decode(metadata,body); }
public sealed record WeeklyAttendanceResponse(uint Result) : IGameCmdMessage
{ public ServerMessageType MessageType=>ServerMessageType.GameStateSynchronization; internal ushort Type=>0x0441; }
internal sealed class WeeklyAttendanceResponseEncoder : GameCmdMessageEncoder<WeeklyAttendanceResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeeklyAttendanceResponse message)
    { var bytes=new byte[4];BinaryPrimitives.WriteUInt32LittleEndian(bytes,message.Result);return OnePayload(message.Type,bytes); }
}
public sealed record AttendanceCompleted(SelectedSessionIdentity Session,bool Succeeded) : INotification<AttendanceCompleted>;
public sealed class AttendanceCompletedHandler(ISelectedSessionDomainService sessions) : INotificationHandler<AttendanceCompleted>
{
    public ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,AttendanceCompleted notice,CancellationToken ct)
        => ValueTask.FromResult<IReadOnlyList<IServerNetworkMessage>>(recipient.CharacterId==notice.Session.CharacterId
            && sessions.IsInWorld(notice.Session) ? [new WeeklyAttendanceResponse(notice.Succeeded ? 0u : 1u)] : []);
}
public sealed class WeeklyAttendanceHandler(ICommandBus commands) : ClientMessageHandler<WeeklyAttendanceRequest>, IProtocolCommandMessageHandler<GameWeeklyAttendanceCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, WeeklyAttendanceRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameWeeklyAttendanceCommand(context, message), ct).ConfigureAwait(false);
}
public sealed class EventsChangedHandler(ISelectedSessionDomainService sessions,IEventPlayerProjectionDomainService projection,
    Dfo.Server.Abstractions.Characters.ICharacterDomainService characters,
    Dfo.Server.Abstractions.Quests.IQuestQueryDomainService quests) : INotificationHandler<EventsChanged>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient,EventsChanged notice,CancellationToken ct)
    {
        if (!sessions.TryGetInWorld(recipient.CharacterId, out var session) || session.ConnectionId != recipient.ConnectionId
            || recipient.ExpectedSession is { } expected && expected != session) return [];
        var state=await projection.ReadNowAsync(session,ct);
        List<IServerNetworkMessage> messages = [new EventInformationMessage(state.Information),new WeeklyAttendanceStateMessage(state.Attendance),
            .. state.ScheduledDungeons.Select(x => new ScheduledDungeonStateMessage(x)),
            .. state.ScheduledActivities.Select(x => new ScheduledActivityStateMessage(x)),
            .. state.ScheduledQuests.Select(x => new ScheduledQuestStateMessage(x)),
            .. state.Joust is { } joust ? Dfo.Server.Protocol.Game.Joust.JoustPayloadCodec.Snapshot(joust) : []];
        if (notice.RefreshQuestOffers)
        {
            var progress = await characters
                .Get(session.CharacterId).ProgressAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Character progression is unavailable.");
            var offers = await quests
                .ReadAvailableAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
            messages.Add(new AvailableQuestListMessage(progress.Level, offers));
        }
        return sessions.IsInWorld(session) ? messages : [];
    }
}
