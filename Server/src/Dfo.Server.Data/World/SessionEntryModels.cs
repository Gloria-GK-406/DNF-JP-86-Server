using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Data.World;

/// <summary>Another character who is present somewhere the entering character can see.</summary>
/// <remarks>
/// Identified by character, not by the wire identifier the client uses. Assigning and
/// encoding that identifier is the projection's job, not this view's.
/// </remarks>
public sealed record PresentCharacterState(CharacterId CharacterId, byte TownId, byte AreaId);

/// <summary>
/// Migration matrix X-18: who else is standing where the entering character arrives.
/// </summary>
/// <remarks>
/// An empty list means the area was consulted and nobody else is in it, which is an
/// ordinary state on a quiet server.
/// </remarks>
public sealed record TownPresenceEntryState(
    byte TownId,
    byte AreaId,
    IReadOnlyList<PresentCharacterState> Others)
{
    public static TownPresenceEntryState Empty { get; } = new(0, 0, Array.Empty<PresentCharacterState>());
}

/// <summary>Everything the town client needs to draw one nearby character at their live position.</summary>
public sealed record TownParticipantEntryState(
    ActiveCharacterState Character,
    CharacterPublicEntryState Public,
    IReadOnlyList<InventoryItemState> Equipment,
    IReadOnlyList<CharacterPetRuntimeState> Pets,
    EntrySpawnState Spawn);

/// <summary>The stable town view captured when one character is published.</summary>
public sealed record TownPresenceSynchronizationState(
    byte TownId,
    byte AreaId,
    TownParticipantEntryState Newcomer,
    IReadOnlyList<TownParticipantEntryState> ExistingCharacters);

/// <summary>The view sent to the entering character and the exact existing sessions told about it.</summary>
public sealed record TownParticipantPublicationPlan(
    IReadOnlyList<SelectedSessionIdentity> Audience,
    TownPresenceSynchronizationState State);

/// <summary>
/// Migration matrix X-17: the expert-store situation where the character arrives.
/// </summary>
/// <param name="OwnsOpenStore">
/// Whether the entering character has a store of their own standing open. It is the only
/// thing that decides whether a close is announced at all, and whether it reaches the
/// owner as well as the onlookers.
/// </param>
/// <remarks>
/// Who would see the announcement is not restated here. The legacy handler broadcasts to
/// the other sessions in the store area, and the store is where its owner is, so that
/// audience is exactly <see cref="TownPresenceEntryState.Others"/>. Carrying it twice
/// would create two lists that can disagree.
/// </remarks>
public sealed record ExpertStoreEntryState(bool OwnsOpenStore)
{
    public static ExpertStoreEntryState None { get; } = new(false);
}

/// <summary>A duel room currently offered in the lobby.</summary>
/// <param name="NameType">
/// Which of the client's preset room names is shown. Rooms are named by selection rather
/// than free text.
/// </param>
public sealed record PvpRoomState(
    ushort RoomId,
    CharacterId OwnerCharacterId,
    byte NameType,
    short MapIndex,
    bool HasPassword,
    byte BattleMode);

/// <summary>Someone waiting in the duelling lobby.</summary>
/// <remarks>
/// Carries no location. A lobby participant is in the lobby, not standing anywhere, and
/// reusing the presence record here would have forced a town and area to be invented for
/// anyone the presence registry did not also hold.
/// </remarks>
public sealed record PvpLobbyParticipantState(CharacterId CharacterId);

/// <summary>
/// Migration matrix X-09 and X-10: the duelling lobby as the entering character finds it.
/// </summary>
/// <remarks>
/// A null lobby on the composed state means duelling does not apply to this entry at all,
/// which is different from a lobby that applies and happens to be empty. Only the second
/// is a lobby the character could join.
/// </remarks>
public sealed record PvpLobbyEntryState(
    IReadOnlyList<PvpLobbyParticipantState> Participants,
    IReadOnlyList<PvpRoomState> Rooms)
{
    public static PvpLobbyEntryState Empty { get; } = new(
        Array.Empty<PvpLobbyParticipantState>(),
        Array.Empty<PvpRoomState>());
}

/// <summary>
/// The runtime state adjacent to a character's entry: who is nearby and what they are
/// doing, as of the moment the entry was composed.
/// </summary>
/// <param name="PvpLobby">
/// The duelling lobby, or null when duelling does not apply to this entry.
/// </param>
/// <remarks>
/// Migration matrix X-15 and X-16 -- the interrupted run a character may return to -- were
/// carried here and are gone again. This game has no rejoin: a character who drops in a
/// dungeon is in town and weakened on their next login, so there is no run to be offered
/// back. See Docs/领域结构重构设计.md section 7.
/// </remarks>
public sealed record SessionAdjacentEntryState(
    TownPresenceEntryState TownPresence,
    ExpertStoreEntryState ExpertStore,
    PvpLobbyEntryState? PvpLobby)
{
    public static SessionAdjacentEntryState Empty { get; } = new(
        TownPresenceEntryState.Empty,
        ExpertStoreEntryState.None,
        PvpLobby: null);
}

public enum SelectedSessionCleanupNoticeKind
{
    ExpertStoreClosed = 1,
    TownDeparture = 2,
}

public sealed record SelectedSessionCleanupNotice(
    SelectedSessionCleanupNoticeKind Kind,
    ushort UserId);

public sealed record PartyDirectoryRecipientGroup(
    PartyDirectoryScope Scope,
    IReadOnlyList<SelectedSessionIdentity> Sessions);

/// <summary>
/// Exact transport recipients captured outside the Party domain while lifecycle gates are held.
/// </summary>
public sealed record PartyNotificationRecipientPlan(
    IReadOnlyList<SelectedSessionIdentity> CharacterRecipients,
    IReadOnlyList<PartyDirectoryRecipientGroup> DirectoryRecipients)
{
    public static PartyNotificationRecipientPlan Empty { get; } = new([], []);
}

/// <remarks>
/// Three projections that are not the same kind of thing. The owner's notices must reach one
/// connection this code already has in hand -- it is the connection the departure is happening on.
/// Town peers are one exact-session fan-out tied to the presence snapshot, and the independent
/// party plan names its own clear/refresh audiences. Keeping the selected-session identity prevents
/// an old town event from following the same character onto a replacement connection or channel.
/// <para>
/// One list of town notices for all town peers rather than one per peer, because they never differ.
/// </para>
/// </remarks>
public sealed record SelectedSessionCleanupNotificationPlan(
    NetworkConnectionId OwnerConnectionId,
    IReadOnlyList<SelectedSessionCleanupNotice> RequiredOwnerNotices,
    IReadOnlyList<SelectedSessionIdentity> PeerAudience,
    IReadOnlyList<SelectedSessionCleanupNotice> PeerNotices,
    PartyNotificationPlan PartyNotifications,
    PartyNotificationRecipientPlan PartyRecipients)
{
    public SelectedSessionCleanupNotificationPlan(
        NetworkConnectionId ownerConnectionId,
        IReadOnlyList<SelectedSessionCleanupNotice> requiredOwnerNotices,
        IReadOnlyList<SelectedSessionIdentity> peerAudience,
        IReadOnlyList<SelectedSessionCleanupNotice> peerNotices,
        PartyNotificationPlan partyNotifications)
        : this(
            ownerConnectionId,
            requiredOwnerNotices,
            peerAudience,
            peerNotices,
            partyNotifications,
            PartyNotificationRecipientPlan.Empty)
    {
    }

    public SelectedSessionCleanupNotificationPlan(
        NetworkConnectionId ownerConnectionId,
        IReadOnlyList<SelectedSessionCleanupNotice> requiredOwnerNotices,
        IReadOnlyList<SelectedSessionIdentity> peerAudience,
        IReadOnlyList<SelectedSessionCleanupNotice> peerNotices)
        : this(
            ownerConnectionId,
            requiredOwnerNotices,
            peerAudience,
            peerNotices,
            PartyNotificationPlan.Empty,
            PartyNotificationRecipientPlan.Empty)
    {
    }

    public static SelectedSessionCleanupNotificationPlan Empty(SelectedSessionIdentity session) =>
        new(
            session.ConnectionId,
            [],
            [],
            [],
            PartyNotificationPlan.Empty,
            PartyNotificationRecipientPlan.Empty);
}
