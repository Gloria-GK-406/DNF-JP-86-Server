using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Protocol.Game.MarriageProposal;
public sealed record GameWeddingProposalCommand(NetworkDispatchContext Dispatch, WeddingProposalRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record GameWeddingResponseCommand(NetworkDispatchContext Dispatch, WeddingResponseRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal sealed record GameWeddingPartnerCommand(NetworkDispatchContext Dispatch, WeddingPartnerQuery Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record GameWeddingChurchEnterCommand(NetworkDispatchContext Dispatch, WeddingChurchEnterRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record GameWeddingCommentCommand(NetworkDispatchContext Dispatch, WeddingCommentRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record GameWeddingLetterCommand(NetworkDispatchContext Dispatch, WeddingLetterRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed record GameWeddingGiftCommand(NetworkDispatchContext Dispatch, WeddingGiftRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
internal class GameMarriageCommandHandler : PartyMutationProtocolCommandHandler,
    ICommandHandler<GameWeddingProposalCommand, bool>,
    ICommandHandler<GameWeddingResponseCommand, bool>,
    ICommandHandler<GameWeddingPartnerCommand, bool>,
    ICommandHandler<GameWeddingChurchEnterCommand, bool>,
    ICommandHandler<GameWeddingCommentCommand, bool>,
    ICommandHandler<GameWeddingLetterCommand, bool>,
    ICommandHandler<GameWeddingGiftCommand, bool>
{
    private readonly IMarriageQueryDomainService marriage;
    private readonly IMarriageCoupleDomainService couple;
    private readonly IMarriageVenueDomainService venue;
    private readonly IQuestOfferRefreshDomainService offers;
    private readonly IOnlineUserQueryDomainService users;
    private readonly ICommandWorkUnits units;
    private readonly IGameCommandOutbox outbox;
    public GameMarriageCommandHandler(ISelectedSessionDomainService sessions, IMarriageQueryDomainService marriage,
        IMarriageCoupleDomainService couple, IMarriageVenueDomainService venue, IQuestOfferRefreshDomainService offers,
        ICharacterDomainService characters, ISelectedCharacterCurrentViewDomainService currentViews,
        IOnlineCharacterRuntimeDomainService onlineCharacters, ITownAreaDomainService townAreas, ITownMovementDomainService towns,
        IPartyService parties, IPartyProjectionStateDomainService projectionState, IOnlineUserQueryDomainService users,
        ICommandWorkUnits units, ICommandProjections projections, IGameCommandOutbox outbox,
        TimeProvider clock, ILogger<GameMarriageCommandHandler> logger,
        IMarriageHousingQueryDomainService? housing = null, IMarriageHousingSessionDomainService? housingSessions = null)
        : base(sessions, parties, townAreas, projections, projectionState.StreamOf, null, logger,
            currentViews: currentViews, characters: characters, onlineCharacters: onlineCharacters,
            towns: towns, housing: housing, clock: clock, register: outbox.RegisterExplicitAsync)
    { this.marriage = marriage; this.couple = couple; this.venue = venue; this.offers = offers;
      this.users = users; this.units = units; this.outbox = outbox; this.housingSessions = housingSessions; }
    private readonly IMarriageHousingSessionDomainService? housingSessions;
    protected override IMarriageHousingSessionDomainService? HousingSessions => housingSessions;
    private SelectedSessionRecipient Recipient(NetworkDispatchContext dispatch, GameMessageMetadata metadata, SelectedSessionIdentity actor)
    {
        var connection = actor.ConnectionId == dispatch.Connection.ConnectionId ? dispatch.Connection
            : users.TryGetByConnection(actor.ConnectionId, out var user) ? user?.Connection : null;
        return new(actor, connection, metadata);
    }
    private ValueTask<T> WriteAsync<T>(CommandExecutionContext execution, NetworkDispatchContext dispatch,
        GameMessageMetadata metadata, string name, SelectedSessionIdentity[] actors,
        Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct) where T : notnull =>
        units.WriteAsync(execution, new(name, actors.Select(x => Recipient(dispatch, metadata, x)).ToArray()), action, ct);
    private ValueTask<MarriageProposalResult> ProposalAsync(CommandExecutionContext execution, NetworkDispatchContext dispatch,
        GameMessageMetadata metadata, SelectedSessionIdentity actor, SelectedSessionIdentity peer, byte? response, CancellationToken ct) =>
        WriteAsync(execution, dispatch, metadata, "MarriageProposal", [actor, peer], (_, token) => response is { } value
            ? couple.RespondProposalAsync(actor, peer, value, token) : couple.ProposeAsync(actor, peer, token), ct);
    private ValueTask<MarriageChurchPreparationResult> PrepareAsync(CommandExecutionContext execution, NetworkDispatchContext dispatch,
        GameMessageMetadata metadata, SelectedSessionIdentity actor, CancellationToken ct) =>
        WriteAsync(execution, dispatch, metadata, "PrepareMarriageChurch", [actor], async (_, token) =>
            new MarriageChurchPreparationResult(await venue.PrepareChurchAsync(actor, token).ConfigureAwait(false)), ct);
    private sealed record CeremonyAttempt(MarriageCeremonyResult Result, bool Aborted = false);
    private ValueTask<CeremonyAttempt> CeremonyAsync(CommandExecutionContext execution, NetworkDispatchContext dispatch,
        GameMessageMetadata metadata, SelectedSessionIdentity actor, SelectedSessionIdentity peer, int tier, int wait,
        byte? response, bool recoverUnavailable, CancellationToken ct) =>
        WriteAsync(execution, dispatch, metadata, "MarriageCeremony", [actor, peer], async (unit, token) =>
        {
            try
            {
                var result = response is { } value
                    ? await couple.RespondReservationAsync(actor, peer, value, token).ConfigureAwait(false)
                    : await couple.RequestReservationAsync(actor, peer, tier, wait, token).ConfigureAwait(false);
                if (result.Changed && result.Ceremony is { State: MarriageCeremonyState.Reserved } ceremony)
                    result = result with { Admissions = await couple.OpenWeddingAsync(ceremony, token).ConfigureAwait(false) };
                return new CeremonyAttempt(result);
            }
            catch (MarriageWeddingUnavailableException error) when (recoverUnavailable)
            {
                unit.RequireRollback(error);
                return new CeremonyAttempt(new(MarriageCeremonyFailure.Unavailable), true);
            }
        }, ct);
    private ValueTask AppearanceAsync(SelectedSessionIdentity actor, MarriageChurchSnapshot church, bool restore, CancellationToken ct) =>
        outbox.RegisterExplicitAsync(actor.ConnectionId, new([new WeddingAvatarNotification(church, restore)], []), ct);
    private async ValueTask<bool> MoveAsync(SelectedSessionIdentity actor, int channel, MarriageChurchLocation location, CancellationToken ct)
    {
        if (!sessions.IsInWorld(actor) || townAreas.Find(actor.CharacterId) is not { } previous || !actor.Owns(previous)
            || await currentViews.TryLoadAsync(actor, ct).ConfigureAwait(false) is not { } view) return false;
        var moved = onlineCharacters.CrossInto(actor, location.TownId, location.AreaId, location.X, location.Y);
        return await TownTransferAsync(actor.ConnectionId, actor, view, channel, moved, previous, true, ct).ConfigureAwait(false);
    }

    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameWeddingProposalCommand command, CancellationToken ct)
    { await WeddingProposalAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    private async ValueTask WeddingProposalAsync(CommandExecutionContext execution, NetworkDispatchContext context, WeddingProposalRequest message, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(context.Connection.ConnectionId);
        var peer = message.Target is > 0 and <= ushort.MaxValue && sessions.TryGetInWorld((ushort)message.Target, out var inWorld) ? inWorld : null;
        if (message.Action == 2)
        {
            var ceremony = actor is null || peer is null
                ? new MarriageCeremonyResult(MarriageCeremonyFailure.Invalid)
                : (await CeremonyAsync(execution, context, message.Metadata, actor, peer, message.First, message.Second, null, false, ct).ConfigureAwait(false)).Result;
            logger.LogInformation("Marriage ceremony request actor={Actor} target={Target} tier={Tier} waitIndex={WaitIndex} result={Result} id={Ceremony}",
                actor?.CharacterId, message.Target, message.First, message.Second, ceremony.Failure, ceremony.Ceremony?.Id);
            var invited = ceremony.Succeeded && ceremony.Ceremony?.State == MarriageCeremonyState.Pending
                && actor is not null && peer is not null && sessions.IsInWorld(peer)
                ? new[] { new GameExplicitMessageTarget(peer.ConnectionId, [new WeddingProposalNotification(actor.WireUserId, 2, message.First)]) } : [];
            await outbox.RegisterExplicitAsync(context.Connection.ConnectionId, new(
                [new WeddingProposalAck(ceremony.Succeeded ? 0 : ceremony.Failure == MarriageCeremonyFailure.InsufficientFunds ? -7 : -1,
                    message.Target, 2)], invited), ct).ConfigureAwait(false);
            return;
        }
        var result = actor is null || peer is null || message.Action != 1 || message.First != 0 || message.Second != 0
            || !sessions.IsInWorld(actor) || !sessions.IsInWorld(peer)
            ? new MarriageProposalResult(MarriageProposalFailure.Invalid)
            : await ProposalAsync(execution, context, message.Metadata, actor, peer, null, ct).ConfigureAwait(false);
        logger.LogInformation("Marriage proposal actor={Actor} target={Target} action={Action} result={Result} id={Proposal}",
            actor?.CharacterId, message.Target, message.Action, result.Failure, result.Proposal?.Id);
        var targets = result.Succeeded && actor is not null && peer is not null && sessions.IsInWorld(peer)
            ? new[] { new GameExplicitMessageTarget(peer.ConnectionId, [new WeddingProposalNotification(actor.WireUserId)]) } : [];
        await outbox.RegisterExplicitAsync(context.Connection.ConnectionId, new(
            [new WeddingProposalAck(result.Succeeded ? 0 : result.Failure == MarriageProposalFailure.Occupied ? -151 : -1,
                message.Target, message.Action)], targets), ct).ConfigureAwait(false);
    }


    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameWeddingResponseCommand command, CancellationToken ct)
    { await WeddingResponseAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    private async ValueTask WeddingResponseAsync(CommandExecutionContext execution, NetworkDispatchContext context, WeddingResponseRequest message, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(context.Connection.ConnectionId);
        if (message.Action == 2)
        {
            await RespondToCeremonyAsync(execution, context, message, actor, ct).ConfigureAwait(false);
            return;
        }
        var pending = actor is null ? null : await marriage.ReceivedProposalAsync(actor.CharacterId, ct).ConfigureAwait(false);
        var peer = pending is not null && sessions.TryGetInWorld(pending.Proposer.CharacterId, out var inWorld) ? inWorld : null;
        var result = actor is null || peer is null || message.Action != 1 || message.First != 0 || message.Second != 0
            || !sessions.IsInWorld(actor) || !sessions.IsInWorld(peer)
            || message.Outcome > 2 || (message.Target != 0 && message.Target != peer.WireUserId)
            ? new MarriageProposalResult(MarriageProposalFailure.Invalid)
            : await ProposalAsync(execution, context, message.Metadata, actor, peer, message.Outcome, ct).ConfigureAwait(false);
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
        await outbox.RegisterExplicitAsync(context.Connection.ConnectionId, new(
            ownMessages, targets), ct).ConfigureAwait(false);
    }

    private async ValueTask RespondToCeremonyAsync(CommandExecutionContext execution, NetworkDispatchContext context, WeddingResponseRequest message,
        SelectedSessionIdentity? actor, CancellationToken ct)
    {
        var pending = actor is null ? null : await marriage.ReceivedReservationAsync(actor.CharacterId, ct).ConfigureAwait(false);
        var peer = pending is not null && sessions.TryGetInWorld(pending.Applicant.CharacterId, out var inWorld) ? inWorld : null;
        var attempt = actor is null || peer is null || message.First != 0 || message.Second != 0 || message.Outcome > 2
                || (message.Target != 0 && message.Target != peer.WireUserId)
            ? new CeremonyAttempt(new(MarriageCeremonyFailure.Invalid))
            : await CeremonyAsync(execution, context, message.Metadata, actor, peer, 0, 0, message.Outcome, true, ct).ConfigureAwait(false);
        var result = attempt.Result;
        if (attempt.Aborted && actor is not null && peer is not null)
            await CeremonyAsync(execution, context, message.Metadata, actor, peer, 0, 0, 2, false, ct).ConfigureAwait(false);
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
                var actorChurch = await PrepareAsync(execution, context, message.Metadata, actor, ct).ConfigureAwait(false);
                var peerChurch = await PrepareAsync(execution, context, message.Metadata, peer, ct).ConfigureAwait(false);
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
        await outbox.RegisterExplicitAsync(context.Connection.ConnectionId, new(own,
            peer is not null && other.Count > 0 ? [new(peer.ConnectionId, other)] : []), ct).ConfigureAwait(false);

        {
            foreach (var entry in result.Admissions)
                await MoveAsync(entry.Actor, entry.Church.Room.ChannelId, entry.Church.Location, ct).ConfigureAwait(false);
            foreach (var entry in result.Admissions)
                await AppearanceAsync(entry.Actor, entry.Church, false, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask AppendSnapshotAsync(List<IGameServerMessage> messages, SelectedSessionIdentity session, CancellationToken ct)
    {
        messages.Add(new WeddingInfoMessage(await marriage.SnapshotAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false)));
        var progress = await characters.Get(session.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
        if (progress is not null)
            messages.Add(new AvailableQuestListMessage(progress.Level,
                await offers.ReadAvailableAsync(session.AccountId, session.CharacterId, progress.Level, ct).ConfigureAwait(false)));
    }


    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameWeddingPartnerCommand command, CancellationToken ct)
    { await WeddingPartnerAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    private async ValueTask WeddingPartnerAsync(CommandExecutionContext execution, NetworkDispatchContext context, WeddingPartnerQuery message, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        var snapshot = session is null || !sessions.IsInWorld(session) ? MarriageEngagementSnapshot.Empty
            : await marriage.SnapshotAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var peer = snapshot.Partner;
        if (peer is not null && message.Target != 0 && message.Target != peer.CharacterId) peer = null;
        var messages = new List<IGameServerMessage> { new WeddingPartnerAck(peer) };
        if (peer is not null && snapshot.IsCeremonyReserved && session is not null)
        {
            var prepared = await PrepareAsync(execution, context, message.Metadata, session, ct).ConfigureAwait(false);
            if (prepared.Snapshot is { } church)
            {
                messages.Add(new WeddingChurchPreparationNotification(church, await marriage.RemainingInvitationsAsync(church.Room.CeremonyId, session.CharacterId, ct).ConfigureAwait(false)));
                messages.Add(new WeddingInvitedGuestsNotification(await marriage.InvitedAsync(church.Room.CeremonyId, ct).ConfigureAwait(false)));
            }
        }
        if (session is not null && await marriage.AppearanceAsync(session, ct).ConfigureAwait(false) is { } appearance)
            messages.Add(new WeddingAvatarNotification(appearance));
        await outbox.RegisterExplicitAsync(context.Connection.ConnectionId, new(messages, []), ct).ConfigureAwait(false);
    }


    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameWeddingChurchEnterCommand command, CancellationToken ct)
    { await WeddingChurchEnterAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    private async ValueTask WeddingChurchEnterAsync(CommandExecutionContext execution, NetworkDispatchContext context,
        WeddingChurchEnterRequest message, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(context.Connection.ConnectionId);
        var previous = actor is null ? null : townAreas.Find(actor.CharacterId);
        var view = actor is null ? null : await currentViews.TryLoadAsync(actor, ct).ConfigureAwait(false);
        var result = actor is null || !sessions.IsInWorld(actor) || previous is null
            || !actor.Owns(previous) || view is null
            ? new MarriageChurchEntryResult(-10)
            : await WriteAsync(execution, context, message.Metadata, "EnterMarriageChurch", [actor], (_, token) => venue.EnterChurchAsync(actor, message.Peer, message.ChurchArea, message.Flag, token), ct).ConfigureAwait(false);
        if (result.Succeeded && actor is not null && previous is not null && view is not null)
        {
            var entry = result.Snapshot!;
            var moved = onlineCharacters.CrossInto(actor, entry.Room.TownId, entry.Room.AreaId, entry.Location.X, entry.Location.Y);
            if (!await TownTransferAsync(actor.ConnectionId, actor, view, entry.Room.ChannelId,
                moved, previous, true, ct).ConfigureAwait(false)) result = new(-10);
            if (result.Succeeded)
                await AppearanceAsync(actor, entry, false, ct).ConfigureAwait(false);
        }
        logger.LogInformation("Marriage church entry actor={Actor} peer={Peer} churchArea={ChurchArea} flag={Flag} status={Status} room={Room} area={Town}:{Area}",
            actor?.CharacterId, message.Peer, message.ChurchArea, message.Flag, result.Status, result.Snapshot?.Room.Token, result.Snapshot?.Room.TownId, result.Snapshot?.Room.AreaId);
        await outbox.RegisterCmdAsync(context.Connection.ConnectionId, new WeddingChurchEnterAck(result.Status), ct).ConfigureAwait(false);
    }


    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameWeddingCommentCommand command, CancellationToken ct)
    { await WeddingCommentAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    private async ValueTask WeddingCommentAsync(CommandExecutionContext execution, NetworkDispatchContext c, WeddingCommentRequest m, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(c.Connection.ConnectionId);
        var result = actor is null ? new MarriageGuestbookResult(-1, []) : await WriteAsync(execution, c, m.Metadata, "WeddingComment", [actor], async (_, token) => m.Type switch { 0x03FA => await venue.ReadGuestbookAsync(actor, m.Key, token).ConfigureAwait(false), 0x03F8 => new(await venue.PostGuestbookAsync(actor, m.Key, m.Text, token).ConfigureAwait(false), []), 0x03F9 => new(await venue.DeleteGuestbookAsync(actor, m.Key, m.Id, token).ConfigureAwait(false), []), _ => new MarriageGuestbookResult(-1, []) }, ct).ConfigureAwait(false);
        await outbox.RegisterExplicitAsync(c.Connection.ConnectionId, new([new WeddingCommentAck(m.Type, m.Key, m.Id, result)], []), ct).ConfigureAwait(false);
    }


    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameWeddingLetterCommand command, CancellationToken ct)
    { await WeddingLetterAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    private async ValueTask WeddingLetterAsync(CommandExecutionContext execution, NetworkDispatchContext c, WeddingLetterRequest m, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(c.Connection.ConnectionId);
        var result = actor is null ? new MarriageWeddingLetterResult(-1, 0, [])
            : await WriteAsync(execution, c, m.Metadata, "WeddingLetter", [actor], (_, token) => venue.InviteAsync(actor, m.Mode, m.Target, token), ct).ConfigureAwait(false);
        var messages = new List<IGameServerMessage> { new WeddingLetterAck(result.Status, result.Remaining) };
        if (result.Status == 0) messages.Add(new WeddingInvitedGuestsNotification(result.Invited));
        await outbox.RegisterExplicitAsync(c.Connection.ConnectionId, new(messages,
            result.Letters.Select(x => new GameExplicitMessageTarget(x.Guest.ConnectionId, [new WeddingLetterNotification(x.Church, x.Key)])).ToArray()), ct).ConfigureAwait(false);
    }


    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameWeddingGiftCommand command, CancellationToken ct)
    { await WeddingGiftAsync(execution, command.Dispatch, command.Input, ct).ConfigureAwait(false); return true; }
    private async ValueTask WeddingGiftAsync(CommandExecutionContext execution, NetworkDispatchContext c, WeddingGiftRequest m, CancellationToken ct)
    {
        var actor = sessions.FindByConnection(c.Connection.ConnectionId);
        var result = actor is null ? -1 : await WriteAsync(execution, c, m.Metadata, "WeddingGift", [actor], (_, token) => venue.GiftAsync(actor, m.Key, m.Gold, token), ct).ConfigureAwait(false);
        var messages = new List<IGameServerMessage> { new WeddingGiftAck(result, m.Key) };
        if (result == 0 && actor is not null) messages.Add(GameInventoryDelivery.CarriedGoldNotification(await characters.Get(actor.CharacterId).GoldAsync(ct), clock.GetUtcNow()));
        await outbox.RegisterExplicitAsync(c.Connection.ConnectionId, new(messages, []), ct).ConfigureAwait(false);
    }

}
