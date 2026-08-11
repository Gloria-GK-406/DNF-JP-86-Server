using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Sessions;

/// <summary>Where a character currently is, and what they currently have open.</summary>
public sealed record SessionPresence(
    CharacterId CharacterId,
    int ListenerPort,
    byte TownId,
    byte AreaId,
    bool HasOpenExpertStore);

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

    /// <summary>The presence recorded for a character, or null when none is.</summary>
    SessionPresence? Find(CharacterId characterId);

    /// <summary>
    /// Everyone else on the same listener and in the same area.
    /// </summary>
    /// <remarks>
    /// Scoped by listener as well as area because two channels can both have a town 1
    /// area 0, and their occupants cannot see each other.
    /// </remarks>
    IReadOnlyList<SessionPresence> InArea(
        int listenerPort,
        byte townId,
        byte areaId,
        CharacterId excluding);
}

/// <summary>A character who has signalled they are ready in the duelling lobby.</summary>
public sealed record PvpLobbyParticipant(CharacterId CharacterId, int ListenerPort);

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
    bool Published);

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
/// <param name="ExpiresAt">When the chance to return lapses.</param>
public sealed record DungeonAttachment(
    CharacterId CharacterId,
    AccountId AccountId,
    int PartyId,
    IReadOnlyList<ushort> ParticipantUserIds,
    bool IsDetached,
    DateTimeOffset ExpiresAt);

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
public interface ISessionAdjacentEntryService
{
    ValueTask<SessionAdjacentEntryState> LoadAsync(
        SessionAdjacentSubject subject,
        CancellationToken cancellationToken);
}
