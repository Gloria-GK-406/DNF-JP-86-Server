using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.GuildApplications;
using Dfo.Server.Protocol.Game.GuildCreation;
using Dfo.Server.Protocol.Game.GuildInfo;
using Dfo.Server.Protocol.Game.GuildRoster;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.GuildInvitations;

public sealed record GameGuildInvitationCommand(NetworkDispatchContext Dispatch, GuildInvitationRequest Input)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class GameGuildInvitationCommandHandler(
    ISelectedSessionDomainService sessions, IGuildInvitationSessionDomainService invitations, IGuildMembershipDomainService guilds,
    IGuildMembershipQueryDomainService query, IGuildBenefitDomainService benefits, IGuildOverviewDomainService overviews,
    IGuildAttendanceDomainService attendance, IMailboxQueryDomainService characters,
    ICharacterRelationQueryDomainService relations, ICharacterVisibleSessionDomainService visibility,
    IChannelEntryDomainService channels, ICommandWorkUnits units, IGameCommandOutbox outbox,
    ICommandProjections projections) : ICommandHandler<GameGuildInvitationCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext execution, GameGuildInvitationCommand command, CancellationToken ct)
    {
        var input = command.Input;
        if (GameRequestFreshness.IsStale(input.Metadata))
            return false;
        var connection = command.Dispatch.Connection;
        var reply = (ServerMessageType)(int)input.MessageType;
        if (!sessions.TryGetInWorld(connection.ConnectionId, out var actor))
            return await ReplyAsync(false).ConfigureAwait(false);

        var declaration = new CommandWorkUnitDeclaration("guild-invitation", [new(actor, connection, input.Metadata)]);
        if (input.MessageType == ClientMessageType.GameGuildInvite)
        {
            if (input.TargetName is not { } name)
                return await ReplyAsync(false).ConfigureAwait(false);
            var offer = await units.ReadOnlyAsync(execution, declaration,
                token => FindOfferAsync(actor, name, token), ct).ConfigureAwait(false);
            if (offer is not { Target: { } target, Details: { } details }
                || !invitations.TryOffer(actor, target, offer.Guild, offer.InviteeName))
                return await ReplyAsync(false).ConfigureAwait(false);
            await ReplyAsync(true).ConfigureAwait(false);
            await projections.ProjectAsync(new NotificationAudience.TheseSessions([target]), details, ct).ConfigureAwait(false);
            return true;
        }

        if (input.Accept is not { } accept)
            return await ReplyAsync(false).ConfigureAwait(false);
        if (invitations.Find(actor) is not { } invitation)
            return accept ? await ReplyAsync(false, 0x67).ConfigureAwait(false) : false;

        var joined = accept
            ? await units.WriteAsync(execution, declaration,
                (_, token) => JoinAsync(invitation, actor, token), ct).ConfigureAwait(false)
            : JoinResult.Refused;
        invitations.Remove(actor, invitation);
        // A21 closes locally on decline. A successful CMD reply always displays "joined guild".
        if (accept)
            await ReplyAsync(joined.Accepted).ConfigureAwait(false);
        await projections.ProjectAsync(new NotificationAudience.TheseSessions([invitation.Inviter]),
            new GuildInviteResolved(joined.Accepted, invitation.InviteeName), ct).ConfigureAwait(false);
        if (!joined.Accepted)
            return !accept;

        await projections.ProjectAsync(new NotificationAudience.TheseSessions([actor]),
            new GuildApplicationResolved(actor.CharacterId, joined.Panel.FindMember(actor.CharacterId)), ct).ConfigureAwait(false);
        await projections.ProjectAsync(new NotificationAudience.TheseSessions(visibility.Resolve(actor)),
            new CharacterUserStateRefresh(actor), ct).ConfigureAwait(false);
        var audience = new NotificationAudience.TheseCharacters(joined.Panel.Members.Select(member => member.Character).ToArray());
        await projections.ProjectAsync(audience, joined.Panel, ct).ConfigureAwait(false);
        if (await GuildRosterProjection.BuildAsync(joined.Roster, sessions, channels, ct).ConfigureAwait(false) is { } snapshot)
            await projections.ProjectAsync(audience, new GuildRosterChanged(snapshot), ct).ConfigureAwait(false);
        return true;

        async ValueTask<bool> ReplyAsync(bool success, byte error = 0x22)
        {
            await outbox.RegisterCmdAsync(connection.ConnectionId, new GuildCreationReply(reply, success ? null : error), ct)
                .ConfigureAwait(false);
            return success;
        }
    }

    private async ValueTask<Offer> FindOfferAsync(SelectedSessionIdentity actor, string name, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)
            || await query.FindMembershipAsync(actor.CharacterId, ct).ConfigureAwait(false) is not { Guild: var guild }
            || await characters.FindRecipientAsync(GuildCreationPayloadCodec.EncodeGuildText(name), ct).ConfigureAwait(false) is not { } candidate
            || candidate.CharacterId == actor.CharacterId || !sessions.TryGetInWorld(candidate.CharacterId, out var target)
            || await query.FindMembershipAsync(candidate.CharacterId, ct).ConfigureAwait(false) is not null
            || (await relations.ObserversOfAsync(actor.CharacterId, CharacterRelationKind.Blacklist, ct)
                .ConfigureAwait(false)).Contains(candidate.CharacterId))
            return Offer.Unavailable;
        var roster = await query.ReadRosterEntriesAsync(actor.CharacterId, ct).ConfigureAwait(false);
        var inviter = roster.FirstOrDefault(entry => entry.Membership.Character == actor.CharacterId);
        if (inviter is null)
            return Offer.Unavailable;
        var weeklyVisitors = await attendance.CountWeeklyVisitorsAsync(guild.Id, ct).ConfigureAwait(false);
        var contents = await benefits.ReadContentsAsync(guild.Id, ct).ConfigureAwait(false);
        var details = new GuildInvited(guild.Name, inviter.Name, guild.Promotion, checked((ushort)roster.Count),
            checked((uint)weeklyVisitors), contents.Select(content => content.Kind).ToArray());
        return new Offer(guild.Id, target, candidate.Name, details);
    }

    private async ValueTask<JoinResult> JoinAsync(GuildInvitation invitation, SelectedSessionIdentity invitee, CancellationToken ct)
    {
        if (!ReferenceEquals(invitations.Find(invitee), invitation))
            return JoinResult.Refused;
        var result = await guilds.AcceptInvitationAsync(invitation.Guild, invitation.Inviter.CharacterId, invitee.CharacterId, ct)
            .ConfigureAwait(false);
        if (result.Status != GuildMutationStatus.Success)
            return JoinResult.Refused;
        return new JoinResult(true,
            await GuildInfoPanel.ReadAsync(overviews, invitee.CharacterId, ct).ConfigureAwait(false),
            await query.ReadRosterEntriesAsync(invitee.CharacterId, ct).ConfigureAwait(false));
    }

    private sealed record Offer(GuildId Guild, SelectedSessionIdentity? Target, byte[] InviteeName, GuildInvited? Details)
    {
        public static Offer Unavailable { get; } = new(default, null, [], null);
    }

    private sealed record JoinResult(bool Accepted, GuildInfoPanel Panel, IReadOnlyList<GuildRosterEntry> Roster)
    {
        public static JoinResult Refused { get; } = new(false, GuildInfoPanel.Empty, []);
    }
}
