using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Sessions;

/// <summary>
/// Answers what is going on around a character as they enter.
/// </summary>
/// <remarks>
/// Every answer here is a read. Registering the entering character, and telling anyone
/// about their arrival, belong to the entry lifecycle.
/// </remarks>
internal sealed class SessionAdjacentEntryDomainService(
    ISessionPresenceDomainService presence,
    IExpertStoreDomainService expertStores,
    IPvpLobbyDomainService lobby) : ISessionAdjacentEntryDomainService
{
    public ValueTask<SessionAdjacentEntryState> LoadAsync(
        SessionAdjacentSubject subject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        cancellationToken.ThrowIfCancellationRequested();

        // No listener scope: the legacy town-presence path spans listeners, because the
        // caller that announces arrivals and departures passes none to its directory.
        var occupants = presence.InArea(
            subject.TownId,
            subject.AreaId,
            subject.CharacterId);

        var others = new List<PresentCharacterState>(occupants.Count);
        foreach (var occupant in occupants)
            others.Add(new PresentCharacterState(occupant.CharacterId, occupant.TownId, occupant.AreaId));

        return ValueTask.FromResult(new SessionAdjacentEntryState(
            new TownPresenceEntryState(subject.TownId, subject.AreaId, others),
            new ExpertStoreEntryState(expertStores.HasOpenStore(subject.CharacterId)),
            ResolveLobby(subject)));
    }

    /// <summary>
    /// The lobby as it stands on this listener, or null when duelling does not apply.
    /// </summary>
    /// <remarks>
    /// Null and empty are deliberately different answers. Null says there is no lobby to
    /// speak of on an ordinary channel; an empty lobby says duelling applies and nobody is
    /// waiting yet.
    /// </remarks>
    private PvpLobbyEntryState? ResolveLobby(SessionAdjacentSubject subject)
    {
        if (!subject.Channel.IsFreeDuel)
            return null;

        var listenerPort = subject.Channel.ListenerPort;
        var ready = lobby.ReadyOn(listenerPort, subject.CharacterId);
        var participants = new List<PvpLobbyParticipantState>(ready.Count);
        foreach (var participant in ready)
            participants.Add(new PvpLobbyParticipantState(participant.CharacterId));

        var offered = lobby.OfferedOn(listenerPort);
        var rooms = new List<PvpRoomState>(offered.Count);
        foreach (var room in offered)
        {
            rooms.Add(new PvpRoomState(
                room.RoomId,
                room.OwnerCharacterId,
                room.NameType,
                room.MapIndex,
                room.HasPassword,
                room.BattleMode));
        }

        return new PvpLobbyEntryState(participants, rooms);
    }
}
