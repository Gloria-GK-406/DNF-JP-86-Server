using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
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
    ITownAreaDomainService townAreas,
    ISelectedSessionDomainService sessions,
    IExpertStoreDomainService expertStores,
    IPvpLobbyDomainService lobby) : ISessionAdjacentEntryDomainService
{
    public ValueTask<SessionAdjacentEntryState> LoadAsync(
        SessionAdjacentSubject subject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        cancellationToken.ThrowIfCancellationRequested();

        var occupants = townAreas.Find(new TownAreaKey(
                subject.Channel.ChannelId,
                subject.TownId,
                subject.AreaId))
            ?.Members
            .Where(member => member.SharesTownPresence
                && member.CharacterId != subject.CharacterId)
            .ToArray() ?? [];

        var others = new List<PresentCharacterState>(occupants.Length);
        foreach (var occupant in occupants)
        {
            if (sessions.TryGetInWorld(occupant.CharacterId, out var exact)
                && occupant.Session == exact)
            {
                others.Add(new PresentCharacterState(
                    occupant.CharacterId,
                    occupant.Area.TownId,
                    occupant.Area.AreaId));
            }
        }

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
        if (!subject.Channel.IsPvp)
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
