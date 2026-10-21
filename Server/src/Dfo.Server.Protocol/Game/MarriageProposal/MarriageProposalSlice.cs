using System.Buffers.Binary;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MarriageProposal;

public sealed record WeddingProposalRequest(GameMessageMetadata Metadata, uint Target, byte Action, byte First, byte Second)
    : IClientNetworkMessage { public ClientMessageType MessageType => ClientMessageType.GameWeddingProposal; }
public sealed record WeddingResponseRequest(GameMessageMetadata Metadata, uint Target, byte Action, byte First, byte Second, byte Outcome)
    : IClientNetworkMessage { public ClientMessageType MessageType => ClientMessageType.GameWeddingResponse; }
public sealed record WeddingProposalAck(int Status, uint Target, byte Action, byte First = 0, byte Second = 0)
    : IGameCmdMessage { public ServerMessageType MessageType => ServerMessageType.GameWeddingProposal; }
public sealed record WeddingResponseAck(int Status, uint Target, byte Action, byte Outcome)
    : IGameCmdMessage { public ServerMessageType MessageType => ServerMessageType.GameWeddingResponse; }
public sealed record WeddingProposalNotification(uint Actor, byte Action = 1, byte Tier = 0)
    : IGameNotiMessage { public ServerMessageType MessageType => ServerMessageType.GameWeddingProposalNotification; }
public sealed record WeddingResponseNotification(uint Actor, byte Outcome, byte Action = 1, int Status = 0)
    : IGameNotiMessage { public ServerMessageType MessageType => ServerMessageType.GameWeddingResponseNotification; }

internal sealed class WeddingProposalDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 21) throw new InvalidDataException("WEDDING_REQUEST requires 21 body bytes.");
        // A21 WriteHeader emits the outer frame header separately. WriteRaw then copies a
        // complete 21B native struct, including 14 opaque bytes before these seven fields.
        // The opaque region is not an embedded wire header and has no authority over identity.
        var fields = body[14..];
        return new WeddingProposalRequest(metadata, BinaryPrimitives.ReadUInt32LittleEndian(fields), fields[4], fields[5], fields[6]);
    }
}
internal sealed class WeddingResponseDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 22) throw new InvalidDataException("WEDDING_RESPONSE requires 22 body bytes.");
        var fields = body[14..];
        return new WeddingResponseRequest(metadata, BinaryPrimitives.ReadUInt32LittleEndian(fields), fields[4], fields[5], fields[6], fields[7]);
    }
}
internal static class WeddingProposalPayload
{
    public static byte[] Fields(int status, uint target, byte action, byte? outcome = null, byte tier = 0)
    {
        var body = new byte[outcome is null ? 11 : 12];
        BinaryPrimitives.WriteInt32LittleEndian(body, status);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), target);
        body[8] = action;
        body[9] = tier;
        if (outcome is { } value) body[11] = value;
        return body;
    }
    public static byte[] Cmd(int status, uint target, byte action, byte? outcome = null) =>
        [1, .. Fields(status, target, action, outcome)];
}
internal sealed class WeddingProposalAckEncoder : GameCmdMessageEncoder<WeddingProposalAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingProposalAck message) =>
        OnePayload(GameProtocol.WeddingProposalType, WeddingProposalPayload.Cmd(message.Status, message.Target, message.Action));
}
internal sealed class WeddingResponseAckEncoder : GameCmdMessageEncoder<WeddingResponseAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingResponseAck message) =>
        OnePayload(GameProtocol.WeddingResponseType, WeddingProposalPayload.Cmd(message.Status, message.Target, message.Action, message.Outcome));
}
internal sealed class WeddingProposalNotificationEncoder : GameNotiMessageEncoder<WeddingProposalNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingProposalNotification message) =>
        OnePayload(GameProtocol.WeddingProposalNotificationType, WeddingProposalPayload.Fields(0, message.Actor, message.Action, tier: message.Tier));
}
internal sealed class WeddingResponseNotificationEncoder : GameNotiMessageEncoder<WeddingResponseNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingResponseNotification message) =>
        OnePayload(GameProtocol.WeddingResponseNotificationType, WeddingProposalPayload.Fields(message.Status, message.Actor, message.Action, message.Outcome));
}

public sealed record MarriageProposalCommand(SelectedSessionIdentity Actor, SelectedSessionIdentity Peer, byte? Response)
    : ICommand<MarriageProposalResult>, IMultiAccountCommand, IMultiSelectedSessionCommand
{
    public SelectedSessionIdentity? Session => Actor;
    public IReadOnlyList<SelectedSessionIdentity> Sessions => [Actor, Peer];
    public IReadOnlyList<AccountId> AccountIds => [Actor.AccountId, Peer.AccountId];
}
public sealed class MarriageProposalCommandHandler(IMarriageCoupleDomainService couple)
    : ICommandHandler<MarriageProposalCommand, MarriageProposalResult>
{
    public ValueTask<MarriageProposalResult> HandleAsync(CommandExecutionContext context, MarriageProposalCommand command, CancellationToken ct) =>
        command.Response is { } response ? couple.RespondProposalAsync(command.Actor, command.Peer, response, ct)
            : couple.ProposeAsync(command.Actor, command.Peer, ct);
}

internal sealed class WeddingProposalHandler(ISelectedSessionDomainService sessions, ICommandBus commands,
    IGameMessageSender sender, SelectedSessionTransitionGate lifecycle, ILogger<WeddingProposalHandler> logger) : ClientMessageHandler<WeddingProposalRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, WeddingProposalRequest message, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(context.Connection.ConnectionId);
        var peer = message.Target is > 0 and <= ushort.MaxValue && sessions.TryGetInWorld((ushort)message.Target, out var inWorld) ? inWorld : null;
        await WeddingProposalExecution.RunAsync(lifecycle, actor, peer, async () =>
        {
            if (message.Action == 2)
            {
                var ceremony = actor is null || peer is null
                    ? new MarriageCeremonyResult(MarriageCeremonyFailure.Invalid)
                    : await commands.SendAsync(new MarriageCeremonyCommand(actor, peer, message.First, message.Second, null), ct).ConfigureAwait(false);
                logger.LogInformation("Marriage ceremony request actor={Actor} target={Target} tier={Tier} waitIndex={WaitIndex} result={Result} id={Ceremony}",
                    actor?.CharacterId, message.Target, message.First, message.Second, ceremony.Failure, ceremony.Ceremony?.Id);
                var invited = ceremony.Succeeded && ceremony.Ceremony?.State == MarriageCeremonyState.Pending
                    && actor is not null && peer is not null && sessions.IsInWorld(peer)
                    ? new[] { new GameExplicitMessageTarget(peer.ConnectionId, [new WeddingProposalNotification(actor.WireUserId, 2, message.First)]) } : [];
                await sender.SendExplicitAsync(context.Connection.ConnectionId, new(
                    [new WeddingProposalAck(ceremony.Succeeded ? 0 : ceremony.Failure == MarriageCeremonyFailure.InsufficientFunds ? -7 : -1,
                        message.Target, 2)], invited), ct).ConfigureAwait(false);
                return;
            }
            var result = actor is null || peer is null || message.Action != 1 || message.First != 0 || message.Second != 0
                || !sessions.IsInWorld(actor) || !sessions.IsInWorld(peer)
                ? new MarriageProposalResult(MarriageProposalFailure.Invalid)
                : await commands.SendAsync(new MarriageProposalCommand(actor, peer, null), ct).ConfigureAwait(false);
            logger.LogInformation("Marriage proposal actor={Actor} target={Target} action={Action} result={Result} id={Proposal}",
                actor?.CharacterId, message.Target, message.Action, result.Failure, result.Proposal?.Id);
            var targets = result.Succeeded && actor is not null && peer is not null && sessions.IsInWorld(peer)
                ? new[] { new GameExplicitMessageTarget(peer.ConnectionId, [new WeddingProposalNotification(actor.WireUserId)]) } : [];
            await sender.SendExplicitAsync(context.Connection.ConnectionId, new(
                [new WeddingProposalAck(result.Succeeded ? 0 : result.Failure == MarriageProposalFailure.Occupied ? -151 : -1,
                    message.Target, message.Action)], targets), ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }
}

internal sealed class WeddingResponseHandler(ISelectedSessionDomainService sessions, IMarriageQueryDomainService marriage,
    ICommandBus commands, IGameMessageSender sender, SelectedSessionTransitionGate lifecycle, ILogger<WeddingResponseHandler> logger,
    IQuestOfferRefreshDomainService offers, ICharacterDomainService characters, TimeProvider clock, MarriageWeddingProjection? projection = null) : ClientMessageHandler<WeddingResponseRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, WeddingResponseRequest message, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(context.Connection.ConnectionId);
        if (message.Action == 2)
        {
            await RespondToCeremonyAsync(context, message, actor, ct).ConfigureAwait(false);
            return;
        }
        var pending = actor is null ? null : await marriage.ReceivedProposalAsync(actor.CharacterId, ct).ConfigureAwait(false);
        var peer = pending is not null && sessions.TryGetInWorld(pending.Proposer.CharacterId, out var inWorld) ? inWorld : null;
        await WeddingProposalExecution.RunAsync(lifecycle, actor, peer, async () =>
        {
            var result = actor is null || peer is null || message.Action != 1 || message.First != 0 || message.Second != 0
                || !sessions.IsInWorld(actor) || !sessions.IsInWorld(peer)
                || message.Outcome > 2 || (message.Target != 0 && message.Target != peer.WireUserId)
                ? new MarriageProposalResult(MarriageProposalFailure.Invalid)
                : await commands.SendAsync(new MarriageProposalCommand(actor, peer, message.Outcome), ct).ConfigureAwait(false);
            logger.LogInformation("Marriage response actor={Actor} target={Target} action={Action} outcome={Outcome} result={Result} id={Proposal}",
                actor?.CharacterId, message.Target, message.Action, message.Outcome, result.Failure, result.Proposal?.Id);
            var ownMessages = new List<IGameServerMessage>
                { new WeddingResponseAck(result.Succeeded ? 0 : -3, peer?.WireUserId ?? 0, message.Action, message.Outcome) };
            var peerMessages = new List<IGameServerMessage>();
            if (result.Succeeded && actor is not null && peer is not null && sessions.IsInWorld(peer))
            {
                peerMessages.Add(new WeddingResponseNotification(actor.WireUserId, message.Outcome));
                if (result.Proposal?.State == MarriageProposalState.Engaged)
                {
                    await AppendSnapshotAsync(ownMessages, actor, ct).ConfigureAwait(false);
                    await AppendSnapshotAsync(peerMessages, peer, ct).ConfigureAwait(false);
                }
            }
            var targets = peer is not null && peerMessages.Count > 0
                ? new[] { new GameExplicitMessageTarget(peer.ConnectionId, peerMessages) } : [];
            await sender.SendExplicitAsync(context.Connection.ConnectionId, new(
                ownMessages, targets), ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private async ValueTask RespondToCeremonyAsync(NetworkDispatchContext context, WeddingResponseRequest message,
        SelectedSessionIdentity? actor, CancellationToken ct)
    {
        var pending = actor is null ? null : await marriage.ReceivedReservationAsync(actor.CharacterId, ct).ConfigureAwait(false);
        var peer = pending is not null && sessions.TryGetInWorld(pending.Applicant.CharacterId, out var inWorld) ? inWorld : null;
        await WeddingProposalExecution.RunAsync(lifecycle, actor, peer, async () =>
        {
            MarriageCeremonyResult result;
            try
            {
                result = actor is null || peer is null || message.First != 0 || message.Second != 0 || message.Outcome > 2
                    || (message.Target != 0 && message.Target != peer.WireUserId)
                    ? new MarriageCeremonyResult(MarriageCeremonyFailure.Invalid)
                    : await commands.SendAsync(new MarriageCeremonyCommand(actor, peer, 0, 0, message.Outcome), ct).ConfigureAwait(false);
            }
            catch (MarriageWeddingUnavailableException)
            {
                if (actor is not null && peer is not null)
                    await commands.SendAsync(new MarriageCeremonyCommand(actor, peer, 0, 0, 2), ct).ConfigureAwait(false);
                result = new(MarriageCeremonyFailure.Unavailable);
            }
            logger.LogInformation("Marriage ceremony response actor={Actor} target={Target} outcome={Outcome} result={Result} id={Ceremony}",
                actor?.CharacterId, message.Target, message.Outcome, result.Failure, result.Ceremony?.Id);
            var own = new List<IGameServerMessage> { new WeddingResponseAck(result.Succeeded ? 0 : -3, peer?.WireUserId ?? 0, 2, message.Outcome) };
            var other = new List<IGameServerMessage>();
            if (result.Succeeded && actor is not null && peer is not null && sessions.IsInWorld(peer))
            {
                other.Add(new WeddingResponseNotification(actor.WireUserId, message.Outcome, 2));
                if (result.Ceremony?.State == MarriageCeremonyState.Reserved)
                {
                    own.Add(new WeddingInfoMessage(await marriage.SnapshotAsync(actor.AccountId, actor.CharacterId, ct).ConfigureAwait(false)));
                    other.Add(new WeddingInfoMessage(await marriage.SnapshotAsync(peer.AccountId, peer.CharacterId, ct).ConfigureAwait(false)));
                    var actorChurch = await commands.SendAsync(new PrepareMarriageChurchCommand(actor), ct).ConfigureAwait(false);
                    var peerChurch = await commands.SendAsync(new PrepareMarriageChurchCommand(peer), ct).ConfigureAwait(false);
                    if (actorChurch.Snapshot is { } a) own.Add(new WeddingChurchPreparationNotification(a, await marriage.RemainingInvitationsAsync(a.Room.CeremonyId, actor.CharacterId, ct).ConfigureAwait(false)));
                    if (peerChurch.Snapshot is { } p) other.Add(new WeddingChurchPreparationNotification(p, await marriage.RemainingInvitationsAsync(p.Room.CeremonyId, peer.CharacterId, ct).ConfigureAwait(false)));
                    if (result.Admissions.Count > 0 && result.Ceremony is { } accepted)
                    {
                        var billTo = accepted.Applicant.CharacterId == actor.CharacterId ? own : other;
                        billTo.Add(GameInventoryDelivery.CarriedGoldNotification(await characters.Get(accepted.Applicant.CharacterId).GoldAsync(ct), clock.GetUtcNow()));
                    }
                }
            }
            else if (pending?.State == MarriageCeremonyState.Pending && actor == pending.Partner && peer == pending.Applicant
                && message.First == 0 && message.Second == 0 && message.Outcome <= 2
                && (message.Target == 0 || message.Target == peer.WireUserId)
                && sessions.IsInWorld(actor) && sessions.IsInWorld(peer))
            {
                // Native 03F1 failures close only the responder's window. 03CE closes
                // the applicant's waiting window even on failure (RVA0089197D).
                other.Add(new WeddingResponseNotification(actor.WireUserId, message.Outcome, 2, -3));
            }
            await sender.SendExplicitAsync(context.Connection.ConnectionId, new(own,
                peer is not null && other.Count > 0 ? [new(peer.ConnectionId, other)] : []), ct).ConfigureAwait(false);
            if (projection is not null)
            {
                foreach (var entry in result.Admissions)
                    await projection.MoveAsync(entry.Actor, entry.Church.Room.ChannelId, entry.Church.Location, ct).ConfigureAwait(false);
                foreach (var entry in result.Admissions)
                    await projection.AppearanceAsync(entry.Actor, entry.Church, false, ct).ConfigureAwait(false);
            }
        }, ct).ConfigureAwait(false);
    }

    private async ValueTask AppendSnapshotAsync(List<IGameServerMessage> messages, SelectedSessionIdentity session, CancellationToken ct)
    {
        messages.Add(new WeddingInfoMessage(await marriage.SnapshotAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false)));
        var progress = await characters.Get(session.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
        if (progress is not null)
            messages.Add(new AvailableQuestListMessage(progress.Level,
                await offers.ReadAvailableAsync(session.AccountId, session.CharacterId, progress.Level, ct).ConfigureAwait(false)));
    }
}

internal static class WeddingProposalExecution
{
    // Borrowed by the command pipeline; retain the existing lifecycle fence through ACK + peer NOTI.
    public static async ValueTask RunAsync(SelectedSessionTransitionGate gate, SelectedSessionIdentity? actor,
        SelectedSessionIdentity? peer, Func<ValueTask> action, CancellationToken ct)
    {
        if (actor is null || peer is null) { await action().ConfigureAwait(false); return; }
        var leases = new List<IAsyncDisposable>();
        try
        {
            foreach (var account in new[] { actor.AccountId, peer.AccountId }.Distinct().OrderBy(x => x.Value))
                leases.Add(await gate.AcquireAsync(account, UserAccessMode.Exclusive, ct).ConfigureAwait(false));
            await action().ConfigureAwait(false);
        }
        finally { for (var i = leases.Count - 1; i >= 0; i--) await leases[i].DisposeAsync().ConfigureAwait(false); }
    }
}
