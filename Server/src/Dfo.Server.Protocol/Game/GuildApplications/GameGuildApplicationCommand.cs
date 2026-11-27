using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildInfo;
using Dfo.Server.Protocol.Game.GuildRoster;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.GuildApplications;

public sealed record GameGuildApplicationCommand(NetworkDispatchContext Dispatch, GuildApplicationRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildApplicationCommandHandler(
    ISelectedSessionDomainService sessions,
    IGuildMembershipDomainService guilds,
    IGuildMembershipQueryDomainService query,
    IGuildDiscoveryDomainService discovery, IGuildOverviewDomainService overviews,
    IChannelEntryDomainService channels,
    ICharacterVisibleSessionDomainService visibility,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    ICommandProjections projections) : ICommandHandler<GameGuildApplicationCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext context, GameGuildApplicationCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var connection = command.Dispatch.Connection;
        if (!request.Valid || !sessions.TryGetInWorld(connection.ConnectionId, out var session))
        {
            IGameCmdMessage? refusal = request.MessageType switch
            {
                ClientMessageType.GameGuildApply => new GuildApplyReply(),
                ClientMessageType.GameGuildApproveApplication => new GuildApproveReply(request.Id, false),
                ClientMessageType.GameGuildApplicationList => new GuildApplicationListReply(),
                _ => null,
            };
            if (refusal is not null)
                await outbox.RegisterCmdAsync(connection.ConnectionId, refusal, ct).ConfigureAwait(false);
            return false;
        }

        var actor = session.CharacterId;
        var recipient = new SelectedSessionRecipient(session, connection, request.Metadata);
        var declaration = new CommandWorkUnitDeclaration("guild-application", [recipient]);
        switch (request.MessageType)
        {
            case ClientMessageType.GameGuildApply:
                var applied = await units.WriteAsync(context, declaration, async (_, token) =>
                {
                    if (request.GuildName is not { } name || request.Message is not { } note ||
                        await discovery.FindGuildAsync(name, token).ConfigureAwait(false) is not { } target)
                        return new GuildApplyReply();
                    var result = await guilds.ApplyAsync(actor, target.Guild, note, token).ConfigureAwait(false);
                    if (result.Status != GuildMutationStatus.Success)
                        return new GuildApplyReply();
                    return new GuildApplyReply(await query.FindPendingApplicationAsync(actor, token).ConfigureAwait(false));
                }, ct).ConfigureAwait(false);
                await outbox.RegisterCmdAsync(connection.ConnectionId, applied, ct).ConfigureAwait(false);
                return applied.Pending is not null;

            case ClientMessageType.GameGuildApplicationStatus:
                var pending = await units.ReadOnlyAsync(context, declaration, async token =>
                    new GuildApplicationStatusMessage(await query.FindPendingApplicationAsync(actor, token).ConfigureAwait(false)),
                    ct).ConfigureAwait(false);
                await outbox.RegisterNotiAsync(connection.ConnectionId, pending, ct).ConfigureAwait(false);
                return true;

            case ClientMessageType.GameGuildApplicationList:
                var applications = await units.ReadOnlyAsync(context, declaration, async token =>
                    new GuildApplicationListReply(await query.ReadApplicationEntriesAsync(actor, token).ConfigureAwait(false)),
                    ct).ConfigureAwait(false);
                await outbox.RegisterCmdAsync(connection.ConnectionId, applications, ct).ConfigureAwait(false);
                return applications.Entries is not null;

            case ClientMessageType.GameGuildCancelApplication:
                var canceled = await units.WriteAsync(context, declaration,
                    (_, token) => guilds.CancelApplicationAsync(actor, new GuildId(request.Id), token), ct).ConfigureAwait(false);
                // A21 issues a separate status query as soon as it sends the cancellation.
                return canceled.Status == GuildMutationStatus.Success;

            case ClientMessageType.GameGuildApproveApplication:
            case ClientMessageType.GameGuildRejectApplication:
                return await ReviewAsync(context, request, connection, actor, declaration, ct).ConfigureAwait(false);

            default:
                throw new InvalidOperationException($"Unsupported guild application command {request.MessageType}.");
        }
    }

    private async ValueTask<bool> ReviewAsync(CommandExecutionContext context, GuildApplicationRequest request,
        NetworkConnectionContext requester, CharacterId actor, CommandWorkUnitDeclaration declaration, CancellationToken ct)
    {
        var connection = requester.ConnectionId;
        var approve = request.MessageType == ClientMessageType.GameGuildApproveApplication;
        if (request.Id is 0 or > int.MaxValue)
        {
            if (approve)
                await outbox.RegisterCmdAsync(connection, new GuildApproveReply(request.Id, false), ct).ConfigureAwait(false);
            return false;
        }

        var applicant = new CharacterId((int)request.Id);
        var reviewed = await units.WriteAsync(context, declaration, async (_, token) =>
        {
            var result = approve
                ? await guilds.ApproveAsync(actor, applicant, token).ConfigureAwait(false)
                : await guilds.RejectApplicationAsync(actor, applicant, token).ConfigureAwait(false);
            var accepted = result.Status == GuildMutationStatus.Success;
            var entries = await query.ReadApplicationEntriesAsync(actor, token).ConfigureAwait(false);
            if (!approve || !accepted)
                return new ReviewResult(accepted, entries, GuildInfoPanel.Empty, []);

            var panel = await GuildInfoPanel.ReadAsync(overviews, actor, token).ConfigureAwait(false);
            var roster = await query.ReadRosterEntriesAsync(actor, token).ConfigureAwait(false);
            return new ReviewResult(true, entries, panel, roster);
        }, ct).ConfigureAwait(false);

        if (approve)
            await outbox.RegisterCmdAsync(connection, new GuildApproveReply(request.Id, reviewed.Accepted), ct).ConfigureAwait(false);
        if (reviewed.Entries is { } current)
            await outbox.RegisterCmdAsync(connection, new GuildApplicationListReply(current), ct).ConfigureAwait(false);
        if (!reviewed.Accepted)
            return false;

        await projections.ProjectAsync(new NotificationAudience.TheseCharacters([applicant]),
            new GuildApplicationResolved(applicant, reviewed.Panel.FindMember(applicant)), ct).ConfigureAwait(false);
        if (!approve)
            return true;

        if (sessions.TryGetInWorld(applicant, out var online))
            await projections.ProjectAsync(new NotificationAudience.TheseSessions(visibility.Resolve(online)),
                new CharacterUserStateRefresh(online), ct).ConfigureAwait(false);

        var audience = new NotificationAudience.TheseCharacters(
            reviewed.Panel.Members.Select(member => member.Character).ToArray());
        await projections.ProjectAsync(audience, reviewed.Panel, ct).ConfigureAwait(false);
        if (await GuildRosterProjection.BuildAsync(reviewed.Roster, sessions, channels, ct).ConfigureAwait(false) is { } snapshot)
            await projections.ProjectAsync(audience, new GuildRosterChanged(snapshot), ct).ConfigureAwait(false);
        return true;
    }

    private sealed record ReviewResult(bool Accepted, IReadOnlyList<GuildApplicationEntry>? Entries,
        GuildInfoPanel Panel, IReadOnlyList<GuildRosterEntry> Roster);
}
