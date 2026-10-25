using System.Buffers.Binary;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.MarriageHousing;

public sealed record CapsuleRequest(GameMessageMetadata Metadata, MarriageCapsuleAction Action) : IEffectSettlementRequest
{
    public ClientMessageType MessageType => ClientMessageType.GameMarriageCapsule;
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.CoupleCapsules | ReplayProjectionNeeds.Inventory;
}
internal abstract class CapsuleDecoder(MarriageCapsuleAction action) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => body.Length == 0
        ? new CapsuleRequest(metadata, action) : throw new InvalidDataException("Capsule request must have an empty body.");
}
internal sealed class CapsuleMakeDecoder() : CapsuleDecoder(MarriageCapsuleAction.Make);
internal sealed class CapsuleGiveDecoder() : CapsuleDecoder(MarriageCapsuleAction.Give);
internal sealed class CapsuleTakeDecoder() : CapsuleDecoder(MarriageCapsuleAction.Take);

internal sealed record CapsuleAck(MarriageCapsuleAction Action, MarriageCapsuleResult Result) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameMarriageCapsule; }
internal sealed class CapsuleAckEncoder : GameCmdMessageEncoder<CapsuleAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CapsuleAck m)
    {
        var body = new byte[m.Result.Status != 0 ? 4 : m.Action == MarriageCapsuleAction.Make ? 9 : 5];
        BinaryPrimitives.WriteInt32LittleEndian(body, m.Result.Status);
        if (m.Result.Status == 0)
        {
            if (m.Action == MarriageCapsuleAction.Make) { BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), m.Result.State.Energy); body[8] = m.Result.State.Owned; }
            else body[4] = m.Action == MarriageCapsuleAction.Give ? m.Result.State.Owned : m.Result.State.Pending;
        }
        return OnePayload(checked((ushort)(0x0404 + (int)m.Action)), GameCmdEnvelope.Accept(body));
    }
}
internal sealed class CapsuleHandler(ICommandBus commands) : ClientMessageHandler<CapsuleRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameCapsuleCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, CapsuleRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameCapsuleCommand(context, message), ct).ConfigureAwait(false);
}
internal sealed record CapsuleRefresh(MarriageCapsuleSnapshot Snapshot) : INotification<CapsuleRefresh>;
internal sealed class CapsuleRefreshHandler(ISelectedSessionDomainService sessions, IMarriageCapsuleQueryDomainService query) : INotificationHandler<CapsuleRefresh>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, CapsuleRefresh n, CancellationToken ct)
    {
        if (!sessions.TryGetInWorld(recipient.ConnectionId, out var actor) || actor.CharacterId != recipient.CharacterId || recipient.ExpectedSession is { } expected && expected != actor) return [];
        var snapshot = await query.ReadAsync(actor.AccountId, actor.CharacterId, ct).ConfigureAwait(false);
        return sessions.IsInWorld(actor) ? [new CapsuleStateMessage(snapshot)] : [];
    }
}
internal sealed record CapsuleStateMessage(MarriageCapsuleSnapshot Snapshot) : Dfo.Server.Protocol.Game.StateSynchronization.GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x03DF;
    internal override byte[] EncodeBody() => CapsuleStateEncoder.Body(Snapshot);
}
internal sealed class CapsuleStateEncoder : GameNotiMessageEncoder<CapsuleStateMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CapsuleStateMessage m)
        => OnePayload(0x03DF, Body(m.Snapshot));
    internal static byte[] Body(MarriageCapsuleSnapshot snapshot)
    {
        var b = new byte[7]; BinaryPrimitives.WriteInt32LittleEndian(b, snapshot.State.Energy);
        b[4] = snapshot.State.Owned; b[5] = snapshot.State.Pending; b[6] = snapshot.AwaitingPartner ? (byte)1 : (byte)0;
        return b;
    }
}
internal sealed record CapsuleEnergyMessage(int Energy) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameMarriageCapsuleEnergy; }
internal sealed class CapsuleEnergyEncoder : GameNotiMessageEncoder<CapsuleEnergyMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CapsuleEnergyMessage m)
    { var b = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(b, m.Energy); return OnePayload(0x03E0, b); }
}
internal sealed record CapsuleEnergyRefresh(int Energy) : INotification<CapsuleEnergyRefresh>;
internal sealed class CapsuleEnergyRefreshHandler(ISelectedSessionDomainService sessions, IMarriageCapsuleQueryDomainService query) : INotificationHandler<CapsuleEnergyRefresh>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, CapsuleEnergyRefresh n, CancellationToken ct)
    {
        if (!sessions.TryGetInWorld(recipient.ConnectionId, out var actor) || actor.CharacterId != recipient.CharacterId || recipient.ExpectedSession is { } expected && expected != actor) return [];
        var snapshot = await query.ReadAsync(actor.AccountId, actor.CharacterId, ct).ConfigureAwait(false);
        return sessions.IsInWorld(actor) ? [new CapsuleEnergyMessage(snapshot.State.Energy)] : [];
    }
}
internal sealed class CapsuleEnergySubscriber(IEventService events) : IDomainEventSubscriber<MarriageCapsuleEnergyChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<MarriageCapsuleEnergyChanged> message, CancellationToken ct) =>
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters([message.Payload.Character]), new CapsuleEnergyRefresh(message.Payload.Energy), ct).ConfigureAwait(false);
}
internal sealed class CapsuleGiftSubscriber(IEventService events) : IDomainEventSubscriber<MarriageCapsuleGiftChanged>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<MarriageCapsuleGiftChanged> message, CancellationToken ct) =>
        _ = await events.ProjectAsync(new NotificationAudience.TheseCharacters([message.Payload.Character]), new CapsuleRefresh(new(new())), ct).ConfigureAwait(false);
}
