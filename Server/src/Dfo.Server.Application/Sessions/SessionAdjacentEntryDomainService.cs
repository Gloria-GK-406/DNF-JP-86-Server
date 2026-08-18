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
    ISessionPresenceRegistry presence,
    IExpertStoreRegistry expertStores,
    IPvpLobbyRegistry lobby,
    IDungeonRejoinRegistry rejoin,
    TimeProvider timeProvider) : ISessionAdjacentEntryDomainService
{
    /// <summary>
    /// The largest party identifier the protocol can carry.
    /// </summary>
    /// <remarks>
    /// A recorded run whose party falls outside this cannot be announced, which the legacy
    /// registry treats as its own failure rather than as an absent offer.
    /// </remarks>
    private const int MaximumAddressablePartyId = ushort.MaxValue;

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
            ResolveLobby(subject),
            ResolveRejoin(subject)));
    }

    /// <summary>
    /// Classifies the interrupted run this character could return to, without touching it.
    /// </summary>
    /// <remarks>
    /// Every branch is a distinct answer. Collapsing the anomalies into "no offer" would
    /// make a registry holding the wrong owner, an unusable party or a stuck attachment
    /// indistinguishable from an ordinary character who simply was not in a dungeon.
    /// <para>
    /// The legacy owner check also compares a wire participant identifier. This layer has
    /// no such identifier yet, so ownership is checked on account and character only — a
    /// subset of the legacy check, recorded rather than silently widened.
    /// </para>
    /// <para>
    /// One classification differs from the legacy steady state, as a consequence of not
    /// mutating. The legacy lookup retires a lapsed record as it reports it, so it answers
    /// <see cref="DungeonRejoinOutcome.Expired"/> at most once and everything afterwards
    /// reads as an unexpected attachment state. This path reports it as expired every time,
    /// which is the more truthful answer to the question asked; the retirement that would
    /// change it belongs to the lifecycle.
    /// </para>
    /// </remarks>
    private DungeonRejoinEntryState ResolveRejoin(SessionAdjacentSubject subject)
    {
        var attachment = rejoin.Find(subject.CharacterId);
        if (attachment is null)
            return DungeonRejoinEntryState.NoOffer;

        if (attachment.AccountId != subject.AccountId)
            return Refused(DungeonRejoinOutcome.IdentityMismatch);

        if (!attachment.IsDetached)
            return Refused(DungeonRejoinOutcome.NotDetached);

        if (attachment.PartyId <= 0 || attachment.PartyId > MaximumAddressablePartyId)
            return Refused(DungeonRejoinOutcome.PartyUnavailable);

        // Noticing that a deadline has passed is a reading. Retiring the record because of
        // it is a lifecycle transition, and is deliberately left undone here.
        return attachment.ExpiresAt <= timeProvider.GetUtcNow()
            ? Refused(DungeonRejoinOutcome.Expired)
            : new DungeonRejoinEntryState(
                DungeonRejoinOutcome.Available,
                attachment.PartyId,
                attachment.ParticipantUserIds);
    }

    /// <summary>
    /// An outcome the character cannot act on, carrying nothing about the run.
    /// </summary>
    /// <remarks>
    /// The legacy registry produces a snapshot only on success. Attaching the party and its
    /// members to a refusal would hand them out on the very path that says this run is not
    /// yours — an identity mismatch would answer with the other account's party.
    /// </remarks>
    private static DungeonRejoinEntryState Refused(DungeonRejoinOutcome outcome) =>
        new(outcome, PartyId: 0, Array.Empty<ushort>());

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
