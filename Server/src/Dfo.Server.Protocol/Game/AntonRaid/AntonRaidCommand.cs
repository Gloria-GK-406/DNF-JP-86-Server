using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Raids;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.AntonRaid;

internal sealed record GameAntonRaidCommand(NetworkDispatchContext Dispatch, AntonRaidRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class AntonRaidMessageHandler(ICommandBus commands) : ClientMessageHandler<AntonRaidRequest>,
    IProtocolCommandMessageHandler<GameAntonRaidCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, AntonRaidRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameAntonRaidCommand(context, message), ct).ConfigureAwait(false);
}

internal sealed class GameAntonRaidCommandHandler(
    ISelectedSessionDomainService sessions,
    IAntonRaidDomainService raids,
    IChannelEntryDomainService channels,
    ICharacterIdentityQueryDomainService identities,
    ISelectedCharacterCurrentViewDomainService views,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    ICommandProjections projections,
    ITownAreaDomainService towns,
    IPartyProjectionStateDomainService projectionState,
    IPartyService parties,
    IOutboundOrdering ordering,
    AntonRaidSquadProjection squads,
    TimeProvider clock) : ICommandHandler<GameAntonRaidCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameAntonRaidCommand command, CancellationToken ct)
    {
        execution.Request!.DemandCurrent();
        var input = command.Input;
        var connection = command.Dispatch.Connection.ConnectionId;
        if (!sessions.TryGetInWorld(connection, out var session))
            return false;
        var entry = await channels.ResolveAsync(session.ListenerPort, ct).ConfigureAwait(false);
        execution.Request.DemandCurrent();
        if (entry.Kind != ChannelEntryKind.Raid)
            return false;
        var channel = raids.RecruitmentChannel(session);
        switch (input.MessageType)
        {
            case ClientMessageType.GameCreateAntonRaid:
                var result = AntonRaidRecruitmentResult.Refused;
                if (channel is not null && input.Title is { } title)
                {
                    var lookup = await units.ReadOnlyAsync(execution, new("AntonRaidLeaderIdentity", []),
                        token => identities.ReadAsync([session.CharacterId], token), ct).ConfigureAwait(false);
                    execution.Request.DemandCurrent();
                    if (lookup.TryGetValue(session.CharacterId, out var identity))
                        result = raids.Create(new(session, identity.Name, checked((byte)identity.Job), checked((byte)identity.GrowType), 0), title);
                }
                await DeliverAsync(result).ConfigureAwait(false);
                await SendReplyAsync(ServerMessageType.GameCreateAntonRaidReply, result.Succeeded, result.Number).ConfigureAwait(false);
                return result.Succeeded;

            case ClientMessageType.GameCheckAntonRaidUser:
                var number = channel == input.Channel && sessions.TryGetInWorld((ushort)input.Target, out var target)
                    ? raids.CheckApplicantTarget(session, target) : null;
                await SendReplyAsync(ServerMessageType.GameCheckAntonRaidUserReply, number.HasValue, number.GetValueOrDefault()).ConfigureAwait(false);
                return number.HasValue;

            case ClientMessageType.GameRequestAntonRaidPeer:
                return await RequestPeerAsync().ConfigureAwait(false);

            case ClientMessageType.GameRespondAntonRaidPeer:
                if (channel is null || !sessions.TryGetInWorld((ushort)input.Target, out var sender))
                    return false;
                var approval = raids.Respond(session, sender, input.Value == 0);
                await DeliverAsync(approval).ConfigureAwait(false);
                return approval.Succeeded;

            case ClientMessageType.GameLeaveAntonRaid:
                var departure = raids.Leave(session, (ushort)input.Target);
                if (!departure.Succeeded || input.Target == session.WireUserId)
                    await SendReplyAsync(ServerMessageType.GameLeaveAntonRaidReply, departure.Succeeded).ConfigureAwait(false);
                else if (sessions.TryGetInWorld((ushort)input.Target, out var removed))
                    await outbox.RegisterCmdAsync(removed.ConnectionId, new AntonRaidReply(ServerMessageType.GameLeaveAntonRaidReply, true, 1), ct).ConfigureAwait(false);
                await DeliverAsync(departure).ConfigureAwait(false);
                return departure.Succeeded;

            case ClientMessageType.GameStartAntonRaid:
                var start = await units.WriteAsync(execution, new("AntonRaidStart", []),
                    (_, token) => raids.StartAsync(session, token), ct).ConfigureAwait(false);
                await SendReplyAsync(ServerMessageType.GameStartAntonRaidReply, start.Started).ConfigureAwait(false);
                if (start is { Change: { Raid: { } starting } startingChange, Deadline: { } until })
                {
                    await AntonRaidRecruitmentProjection.DeliverAsync(projections, startingChange, ct).ConfigureAwait(false);
                    IGameNotiMessage[] notices = [.. AntonRaidRuntimeNotice.Timer(until, clock.GetUtcNow()),
                        new AntonRaidRuntimeNotice(ServerMessageType.GameAntonRaidPrepare, ReadOnlyMemory<byte>.Empty)];
                    await AntonRaidRuntimeProjection.SendAsync(projections,
                        starting.Members.Select(member => member.Session).ToArray(), notices, ct).ConfigureAwait(false);
                }
                else if (start.Tickets is { } status)
                {
                    await AntonRaidRuntimeProjection.SendAsync(projections, [session], [AntonRaidRuntimeNotice.Tickets(status)], ct)
                        .ConfigureAwait(false);
                }
                return start.Started;

            case ClientMessageType.GameRequestAntonRaidTickets when raids.IsRecruitingMember(session):
                var tickets = await units.WriteAsync(execution, new("AntonRaidTickets", []),
                    (_, token) => raids.TicketsAsync(session, token), ct).ConfigureAwait(false);
                await AntonRaidRuntimeProjection.SendAsync(projections, [session], [AntonRaidRuntimeNotice.Tickets(tickets)], ct)
                    .ConfigureAwait(false);
                return true;

            case ClientMessageType.GameSetAntonRaidSymbol:
                return await units.WriteAsync(execution, new("anton-raid.symbol", []),
                    (_, token) => raids.ChangeShieldAsync(session, input.Target, input.Value, (byte)input.Operation, token),
                    ct).ConfigureAwait(false);

            case ClientMessageType.GameActivateAntonRaidBuff:
                return await units.WriteAsync(execution, new("anton-raid.buff", []),
                    (_, token) => raids.UseCombatBuffAsync(session, (byte)input.Value, (ushort)input.Target, input.Values!, token),
                    ct).ConfigureAwait(false);

            case ClientMessageType.GameReportAntonRaidMonster when input.Values is { Count: 0 }
                && raids.CombatSnapshot(session) is { } snapshot:
                await AntonRaidRuntimeProjection.SendAsync(projections, [session],
                    AntonRaidCombatFrames.Snapshot(snapshot, clock.GetUtcNow()), ct).ConfigureAwait(false);
                return true;

            case ClientMessageType.GameReportAntonRaidMonster when input.Values is { Count: > 0 }:
                return await units.WriteAsync(execution, new("anton-raid.monster-status", []),
                    (_, token) => raids.ReportMonsterStatusAsync(session, input.Values, token), ct).ConfigureAwait(false);

            case ClientMessageType.GameManageAntonRaid when input.Operation == 0 && input.Target <= ushort.MaxValue:
                var assignment = raids.Assign(session, (ushort)input.Target, input.Value);
                await DeliverAsync(assignment).ConfigureAwait(false);
                return assignment.Succeeded;

            case ClientMessageType.GameRequestAntonRaidMembers when channel is { } own && raids.Find(own, input.Value) is { } raid:
                var profiles = await AntonRaidRecruitmentProjection.LoadMembersAsync(views, raid.Members.Where(member => member.Session != session), ct)
                    .ConfigureAwait(false);
                execution.Request.DemandCurrent();
                await outbox.RegisterExplicitAsync(connection, new([.. profiles, new AntonRaidChangedNotice(raid.Number, raid)], []), ct).ConfigureAwait(false);
                return true;

            case ClientMessageType.GameRequestAntonRaidDirectory when channel is { } directoryChannel && input.Value == 0:
                var directory = await AntonRaidRecruitmentProjection.InitialAsync(views, raids.Directory(directoryChannel), session, ct).ConfigureAwait(false);
                execution.Request.DemandCurrent();
                await outbox.RegisterExplicitAsync(connection, new(directory, []), ct).ConfigureAwait(false);
                return true;

            default:
                return false;
        }

        async ValueTask<bool> RequestPeerAsync()
        {
            if (channel is null || !sessions.TryGetInWorld((ushort)input.Target, out var recipient)
                || raids.RecruitmentChannel(recipient) != channel)
                return false;

            // The selected views open their own inventory snapshot. Its item-lifecycle
            // participant cannot enlist in a command read-only work unit.
            var sender = await views.TryLoadAsync(session, ct).ConfigureAwait(false);
            if (sender is null)
                return false;

            var other = await views.TryLoadAsync(recipient, ct).ConfigureAwait(false);
            execution.Request.DemandCurrent();
            if (other is null || raids.Request(Member(session, sender), Member(recipient, other)) is null)
                return false;

            var notice = new AntonRaidApprovalIssued(
                AntonRaidRecruitmentProjection.ProfileOf(sender),
                new AntonRaidApprovalNotice(session.WireUserId, unchecked((int)input.Value)));
            await projections.ProjectAsync(new NotificationAudience.TheseSessions([recipient]), notice, ct).ConfigureAwait(false);
            return true;
        }

        ValueTask SendReplyAsync(ServerMessageType type, bool accepted, uint value = 0) =>
            outbox.RegisterCmdAsync(connection, new AntonRaidReply(type, accepted, value), ct);

        async ValueTask DeliverAsync(AntonRaidRecruitmentResult change)
        {
            if (!change.Succeeded)
                return;
            var recipients = PartyNotificationRecipientCapture.Capture(change.Party, sessions, towns);
            await PartyNotificationStages.DeliverPartyNotificationsAsync(projections, projectionState, parties, ordering,
                change.Party, recipients, ct).ConfigureAwait(false);
            foreach (var delta in change.Changes)
            {
                await squads.DeliverAsync(delta.Squads, ct).ConfigureAwait(false);
                await AntonRaidRecruitmentProjection.DeliverAsync(projections, delta, ct).ConfigureAwait(false);
            }
        }
    }

    private static AntonRaidMember Member(SelectedSessionIdentity session, SelectCharacterResult view) =>
        new(session, view.Character.Identity.Name, view.Character.Identity.Job, view.Character.Identity.GrowType, 0);

}
