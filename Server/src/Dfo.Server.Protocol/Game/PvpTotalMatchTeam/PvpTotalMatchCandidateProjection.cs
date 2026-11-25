using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.PvpTotalMatchTeam;

internal sealed class PvpTotalMatchCandidateProjection(
    IPvpRoomQueryDomainService rooms,
    IPvpTotalMatchCandidateQueryDomainService candidates,
    IGameCommandOutbox outbox,
    TimeProvider clock)
{
    public async ValueTask ProjectRoomChangeAsync(PvpRoomOutcome.Changed change, CancellationToken cancellationToken)
    {
        if (change.After is not { Mode: PvpBattleMode.TotalMatch, Phase: PvpRoomPhase.Waiting } room
            || change.Kind is not (PvpRoomChangeKind.Created or PvpRoomChangeKind.ModeChanged
                or PvpRoomChangeKind.Entered or PvpRoomChangeKind.InviteAccepted))
            return;

        foreach (var (_, member) in room.Members)
        {
            if (change.Kind is PvpRoomChangeKind.Entered or PvpRoomChangeKind.InviteAccepted
                && change.Before?.SeatOf(member.Session.CharacterId) is >= 0)
                continue;
            await ProjectAsync(member.Session, lobby: false, cancellationToken).ConfigureAwait(false);
        }
    }

    public ValueTask ProjectLobbyAsync(SelectedSessionIdentity session, ChannelEntryIdentity channel,
        CancellationToken cancellationToken) => channel.AllowsTotalMatchmaking
        ? ProjectAsync(session, lobby: true, cancellationToken)
        : ValueTask.CompletedTask;

    public async ValueTask ProjectAfterTeamSavedAsync(SelectedSessionIdentity session,
        IChannelEntryDomainService channels, CancellationToken cancellationToken)
    {
        if (rooms.FindSeated(session.CharacterId) is not null)
        {
            await ProjectAsync(session, lobby: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        var channel = await channels.ResolveAsync(session.ListenerPort, cancellationToken).ConfigureAwait(false);
        await ProjectLobbyAsync(session, channel, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ProjectAsync(SelectedSessionIdentity session, bool lobby, CancellationToken cancellationToken)
    {
        if (!CanLoadCandidates(session, lobby)) return;
        var preparedAt = clock.GetUtcNow();
        var lineup = await candidates.ReadAsync(session, preparedAt, cancellationToken).ConfigureAwait(false);
        if (lineup is not null && CanLoadCandidates(session, lobby))
            await outbox.RegisterNotiAsync(session.ConnectionId,
                new PvpTotalMatchCharacterInfo(session.WireUserId, lineup.Candidates, preparedAt), cancellationToken)
                .ConfigureAwait(false);
    }

    private bool CanLoadCandidates(SelectedSessionIdentity session, bool lobby)
    {
        var room = rooms.FindSeated(session.CharacterId);
        return lobby ? room is null
            : room is { Mode: PvpBattleMode.TotalMatch, Phase: PvpRoomPhase.Waiting }
            && room.ListenerPort == session.ListenerPort
            && room.Members.Any(entry => entry.Member.Session == session);
    }
}
