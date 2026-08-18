using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Sessions;

/// <summary>Where a character currently is.</summary>
/// <param name="StandingInTown">
/// Whether the character is actually standing where this record says.
/// </param>
/// <remarks>
/// The distinction in <paramref name="StandingInTown"/> matters more than it looks. A
/// character who walks into a dungeon keeps their last town coordinates, and the legacy
/// directory excludes them for exactly that reason — its own comment calls those fields
/// leftovers from before they entered. It covers the legacy conjunction: the character has
/// finished arriving, is not inside a dungeon, carries no blocking state, and their
/// connection is still up. The lifecycle that admits them owns keeping it true.
/// </remarks>
public sealed record SessionPresence(
    CharacterId CharacterId,
    int ListenerPort,
    byte TownId,
    byte AreaId,
    bool StandingInTown,
    SelectedSessionIdentity? Session = null);

/// <summary>
/// The server's record of who is present and where.
/// </summary>
/// <remarks>
/// This registry exists for the server's lifetime, so consulting it always answers. An
/// empty answer means nobody is there, which is different from there being nowhere to ask.
/// <para>
/// Admitting and withdrawing belong to the entry lifecycle, which is not migrated yet.
/// Until it is, nothing calls them in production and every query legitimately answers
/// empty.
/// </para>
/// </remarks>
public interface ISessionPresenceRegistry
{
    /// <summary>Records a character as present, replacing any earlier record of them.</summary>
    void Admit(SessionPresence presence);

    /// <summary>Removes a character's presence. Returns false when they were not recorded.</summary>
    bool Withdraw(CharacterId characterId);

    bool WithdrawExact(SelectedSessionIdentity session);

    /// <summary>The presence recorded for a character, or null when none is.</summary>
    SessionPresence? Find(CharacterId characterId);

    /// <summary>
    /// Everyone else standing in the same area.
    /// </summary>
    /// <param name="listenerPort">
    /// Restricts the answer to one listener. Null spans every listener, which is what the
    /// legacy town-presence path does — its directory takes the port as an optional filter
    /// and the caller that announces arrivals and departures supplies none.
    /// </param>
    IReadOnlyList<SessionPresence> InArea(
        byte townId,
        byte areaId,
        CharacterId excluding,
        int? listenerPort = null);
}

/// <summary>Generation-safe directory of currently selected sessions.</summary>
public interface ISelectedSessionDirectory
{
    SelectedSessionIdentity? RegisterReplacing(SelectedSessionIdentity session);
    bool TryRemoveExact(SelectedSessionIdentity session);
    SelectedSessionIdentity? Find(AccountId accountId);
    SelectedSessionIdentity? FindByCharacter(CharacterId characterId);
    SelectedSessionIdentity? FindByWireUserId(ushort wireUserId);
    SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId);
    IReadOnlyList<SelectedSessionIdentity> Current();
}

public interface ISelectedSessionRuntimeDirectory
{
    void Publish(SelectedSessionIdentity session, SelectCharacterResult result, bool shouldPersistPosition);
    void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result);
    SelectCharacterResult GetRequired(SelectedSessionIdentity session);
    bool TryGet(
        SelectedSessionIdentity session,
        out SelectCharacterResult? result,
        out bool shouldPersistPosition);
    bool RemoveExact(SelectedSessionIdentity session);
}

/// <summary>Narrow peripheral seam used to save the last exact runtime position during cleanup.</summary>
public interface ISelectedSessionPositionPersistence
{
    ValueTask PersistAsync(
        AccountId accountId,
        CharacterId characterId,
        Dfo.Server.Data.Characters.CharacterPositionState position,
        CancellationToken cancellationToken);
}

/// <summary>
/// The server's record of which characters have an expert store standing open.
/// </summary>
/// <remarks>
/// Deliberately separate from presence. A store outlives the moment its owner is standing
/// somewhere, and the legacy runtime keeps it in its own registry keyed by owner; reading
/// it off a presence record would answer "no store" for anyone the presence registry has
/// not yet admitted — including, at composition time, the character entering.
/// </remarks>
public interface IExpertStoreRegistry
{
    /// <summary>Records that a character has a store open.</summary>
    void Open(CharacterId ownerCharacterId);

    void Open(SelectedSessionIdentity session);

    /// <summary>Records that a character's store is closed. False when none was open.</summary>
    bool Close(CharacterId ownerCharacterId);

    bool CloseExact(SelectedSessionIdentity session);

    /// <summary>Whether this character has a store standing open.</summary>
    bool HasOpenStore(CharacterId ownerCharacterId);
}

/// <summary>A character who has signalled they are ready in the duelling lobby.</summary>
public sealed record PvpLobbyParticipant(
    CharacterId CharacterId,
    int ListenerPort,
    SelectedSessionIdentity? Session = null);

/// <summary>
/// A duel room the lobby knows about.
/// </summary>
/// <param name="Published">
/// Whether the room has finished being announced. A room still mid-announcement is not
/// shown, because a character who joined it now would race the announcement.
/// </param>
public sealed record PvpRoom(
    ushort RoomId,
    CharacterId OwnerCharacterId,
    int ListenerPort,
    byte NameType,
    short MapIndex,
    bool HasPassword,
    byte BattleMode,
    bool Published,
    byte[]? Name = null,
    byte RoomState = 0,
    byte ManagerSeat = 0,
    IReadOnlyList<PvpRoomSeatState>? Seats = null,
    int MatchingType = 0);

/// <summary>
/// The server's record of who is waiting to duel and which rooms are on offer.
/// </summary>
/// <remarks>
/// Like the presence registry, this exists for the server's lifetime and always answers.
/// Its writers belong to the duelling flow, which is not migrated.
/// </remarks>
public interface IPvpLobbyRegistry
{
    /// <summary>Records a character as ready, replacing any earlier record of them.</summary>
    void MarkReady(PvpLobbyParticipant participant);

    /// <summary>Removes a character's readiness. Returns false when they were not recorded.</summary>
    bool ClearReady(CharacterId characterId);

    bool ClearReadyExact(SelectedSessionIdentity session);

    /// <summary>Records a room, replacing any earlier record with the same identifier.</summary>
    void Offer(PvpRoom room);

    /// <summary>Removes a room. Returns false when it was not recorded.</summary>
    bool Withdraw(ushort roomId);

    /// <summary>Everyone else ready on the same listener.</summary>
    IReadOnlyList<PvpLobbyParticipant> ReadyOn(int listenerPort, CharacterId excluding);

    /// <summary>The rooms on the same listener that have finished being announced.</summary>
    IReadOnlyList<PvpRoom> OfferedOn(int listenerPort);
}

/// <summary>
/// A dungeon run a character was detached from and may return to.
/// </summary>
/// <param name="IsDetached">
/// Whether the run is currently running without them. A run they are still attached to is
/// not something to return to.
/// </param>
/// <param name="IdleExpiresAt">
/// When the chance to return lapses through inactivity. The legacy registry pushes this
/// forward each time the run is touched.
/// </param>
/// <param name="HardExpiresAt">
/// When the chance to return lapses outright, however active the run has been. The legacy
/// registry keeps both and treats whichever comes first as the deadline; carrying only the
/// idle one would let a run that has long outlived its hard limit still read as available.
/// </param>
public sealed record DungeonAttachment(
    CharacterId CharacterId,
    AccountId AccountId,
    int PartyId,
    IReadOnlyList<ushort> ParticipantUserIds,
    bool IsDetached,
    DateTimeOffset IdleExpiresAt,
    DateTimeOffset HardExpiresAt,
    SelectedSessionIdentity? Session = null)
{
    /// <summary>The moment the run stops being returnable, whichever limit arrives first.</summary>
    public DateTimeOffset ExpiresAt => IdleExpiresAt <= HardExpiresAt ? IdleExpiresAt : HardExpiresAt;
}

/// <summary>
/// The server's record of interrupted dungeon runs.
/// </summary>
/// <remarks>
/// Reading it changes nothing. The legacy lookup renews an idle deadline and can retire a
/// lapsed record as a side effect of being asked; those are lifecycle transitions and do
/// not belong to a question.
/// </remarks>
public interface IDungeonRejoinRegistry
{
    /// <summary>Records a run, replacing any earlier record for the same character.</summary>
    void Attach(DungeonAttachment attachment);

    /// <summary>Removes a character's record. Returns false when none was held.</summary>
    bool Release(CharacterId characterId);

    bool ReleaseExact(SelectedSessionIdentity session);

    /// <summary>Ends/detaches only the dungeon runtime owned by this exact selected session.</summary>
    bool EndSessionExact(SelectedSessionIdentity session);

    bool ReleaseAttachmentExact(DungeonAttachment attachment);

    bool TryReplaceExact(DungeonAttachment expected, DungeonAttachment replacement);

    /// <summary>The record held for a character, or null when none is.</summary>
    DungeonAttachment? Find(CharacterId characterId);
}

/// <summary>What the session-adjacent views are being asked about.</summary>
public sealed record SessionAdjacentSubject(
    CharacterId CharacterId,
    AccountId AccountId,
    ChannelEntryIdentity Channel,
    byte TownId,
    byte AreaId);

/// <summary>Projects runtime presence into the views a character's entry carries.</summary>
public interface ISessionAdjacentEntryDomainService
{
    ValueTask<SessionAdjacentEntryState> LoadAsync(
        SessionAdjacentSubject subject,
        CancellationToken cancellationToken);
}
