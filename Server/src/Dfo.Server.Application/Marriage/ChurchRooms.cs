using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

/// <summary>Church rooms: allocation, admission, where people stand relative to a room, and the 03D4/03CF snapshot.</summary>
internal sealed class ChurchRooms(IMarriageChurchDomainStore store, IMarriageChurchRuleService rules, IMarriageProposalRuleService genders,
    ISelectedSessionDomainService sessions, ISelectedSessionSnapshotQuery online, ITownAreaDomainService areas,
    ICharacterDomainService characters, IOnlineCharacterRuntimeDomainService onlineCharacters, ISelectedSessionPositionPersistence persistence,
    TimeProvider clock)
{
    public ValueTask<bool> IsChurchAsync(byte townId, byte areaId, CancellationToken ct) => rules.IsChurchAsync(townId, areaId, ct);

    public ValueTask<MarriageChurchRoom?> RoomAsync(Guid ceremony, CancellationToken ct) => store.ReadAsync(ceremony, ct);

    public ValueTask<bool> IsAdmittedAsync(Guid ceremony, CharacterId character, CancellationToken ct) =>
        store.IsAdmittedAsync(ceremony, character, ct);

    public ValueTask ReleaseAsync(Guid ceremony, CancellationToken ct) => store.ReleaseAsync(ceremony, ct);

    /// <summary>The actor's town area membership, when the actor's session owns it.</summary>
    public TownAreaMember? Standing(SelectedSessionIdentity actor) =>
        onlineCharacters.StandingOf(actor) is { } member ? member : null;

    /// <summary>Whether the character's membership, owned or not, is this room.</summary>
    public bool IsIn(CharacterId character, MarriageChurchRoom room) => areas.Find(character)?.Area == Key(room);

    public int? ChannelOf(CharacterId character) => areas.Find(character)?.Area.ChannelId;

    public SelectedSessionIdentity[] Occupants(MarriageChurchRoom room) =>
        online.Snapshot().Where(x => onlineCharacters.StandingOf(x) is { } a && a.Area == Key(room)).ToArray();

    public bool IsEmpty(MarriageChurchRoom room) =>
        !online.Snapshot().Any(x => onlineCharacters.StandingOf(x) is { } a && a.Area == Key(room));

    /// <summary>The character is online on its original account and standing in the room.</summary>
    public bool IsPresent(SelectedSessionIdentity original, MarriageChurchRoom room)
    {
        return sessions.TryGetInWorld(original.CharacterId, out var current) && current.AccountId == original.AccountId
            && onlineCharacters.StandingOf(current) is { } member
            && member.Area == Key(room);
    }

    /// <summary>The reserved pair's room, allocated in the actor's town when it does not exist yet.</summary>
    public async ValueTask<MarriageChurchSnapshot?> PrepareAsync(SelectedSessionIdentity actor, MarriageCeremony ceremony, CancellationToken ct)
    {
        if (Standing(actor) is not { } member) return null;
        var room = await store.ReadAsync(ceremony.Id, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (room is null)
        {
            foreach (var location in await rules.LocationsAsync(member.Area.TownId, ct).ConfigureAwait(false))
            {
                room = await store.TryCreateAsync(ceremony.Id, member.Area.ChannelId, location,
                    now, now + rules.PreparationTime(ceremony.WaitIndex), ct).ConfigureAwait(false);
                if (room is not null) break;
            }
        }
        if (room is null || room.ChannelId != member.Area.ChannelId) return null;
        if (room.ReadyUntil <= now) return null;
        return await SnapshotForAsync(actor, ceremony, room, ct).ConfigureAwait(false);
    }

    public async ValueTask<MarriageChurchEntryResult> EnterNewlywedAsync(SelectedSessionIdentity actor, MarriageCeremony ceremony,
        uint peer, uint churchArea, CancellationToken ct)
    {
        if (Standing(actor) is not { } member) return new(-10);
        var expectedPeer = actor.CharacterId == ceremony.Applicant.CharacterId ? ceremony.Partner.CharacterId : ceremony.Applicant.CharacterId;
        var room = await store.ReadAsync(ceremony.Id, ct).ConfigureAwait(false);
        if (room is null || peer != expectedPeer.Value || churchArea != room.AreaId || room.ChannelId != member.Area.ChannelId) return new(-10);
        if (room.ReadyUntil <= clock.GetUtcNow()) return new(-7);
        var snapshot = await SnapshotForAsync(actor, ceremony, room, ct).ConfigureAwait(false);
        if (snapshot is null || onlineCharacters.PositionOf(actor) is not { } standing) return new(-10);
        await persistence.PersistAsync(actor.AccountId, actor.CharacterId, standing with
        {
            TownId = room.TownId, AreaId = room.AreaId, X = snapshot.Location.X, Y = snapshot.Location.Y,
        }, ct).ConfigureAwait(false);
        await store.AdmitAsync(ceremony.Id, actor.CharacterId, ct).ConfigureAwait(false);
        return new(0, snapshot);
    }

    /// <summary>A guest whose invitation and gift have already been checked.</summary>
    public async ValueTask<MarriageChurchEntryResult> EnterGuestAsync(SelectedSessionIdentity actor, MarriageCeremony ceremony,
        MarriageChurchRoom room, uint key, CancellationToken ct)
    {
        if (onlineCharacters.PositionOf(actor) is not { } standing) return new(-10);
        var church = await SnapshotAsync(ceremony, room, key, ct).ConfigureAwait(false);
        await persistence.PersistAsync(actor.AccountId, actor.CharacterId, standing with
        { TownId = room.TownId, AreaId = room.AreaId, X = church.Location.X, Y = church.Location.Y }, ct).ConfigureAwait(false);
        await store.AdmitAsync(ceremony.Id, actor.CharacterId, ct).ConfigureAwait(false);
        return new(0, church);
    }

    public async ValueTask<bool> CanCrossOwnAsync(SelectedSessionIdentity actor, Guid ceremony, int channelId,
        byte townId, byte areaId, CancellationToken ct)
    {
        var room = await store.ReadAsync(ceremony, ct).ConfigureAwait(false);
        if (room is null || room.ChannelId != channelId || room.TownId != townId || room.AreaId != areaId) return false;
        var standing = areas.Find(actor.CharacterId);
        var alreadyInside = standing is not null && standing.Session == actor
            && standing.Area == new TownAreaKey(channelId, townId, areaId);
        return (room.ReadyUntil > clock.GetUtcNow() || alreadyInside)
            && await store.IsAdmittedAsync(room.CeremonyId, actor.CharacterId, ct).ConfigureAwait(false);
    }

    /// <returns>Null when this wedding's room is not the area being crossed into; otherwise the verdict.</returns>
    public async ValueTask<bool?> CanCrossAsGuestAsync(SelectedSessionIdentity actor, Guid ceremony, MarriageWeddingPhase phase,
        int channelId, byte townId, byte areaId, CancellationToken ct)
    {
        var guestRoom = await store.ReadAsync(ceremony, ct).ConfigureAwait(false);
        if (guestRoom is null || guestRoom.ChannelId != channelId || guestRoom.TownId != townId || guestRoom.AreaId != areaId) return null;
        var member = areas.Find(actor.CharacterId);
        var inside = member is not null && member.Session == actor && member.Area == new TownAreaKey(channelId, townId, areaId);
        if (phase == MarriageWeddingPhase.Completed && !inside) return false;
        return (guestRoom.ReadyUntil > clock.GetUtcNow() || inside)
            && await store.IsAdmittedAsync(ceremony, actor.CharacterId, ct).ConfigureAwait(false);
    }

    /// <summary>The wedding's room when the actor, standing at <paramref name="member"/>, attends it.</summary>
    public async ValueTask<MarriageChurchRoom?> AttendedRoomAsync(SelectedSessionIdentity actor, TownAreaMember member, Guid ceremony,
        MarriageWeddingPhase phase, CancellationToken ct)
    {
        var room = await store.ReadAsync(ceremony, ct).ConfigureAwait(false);
        if (room is null || member.Area.ChannelId != room.ChannelId
            || phase != MarriageWeddingPhase.Preparing && member.Area != Key(room)
            || !await store.IsAdmittedAsync(ceremony, actor.CharacterId, ct).ConfigureAwait(false)) return null;
        return room;
    }

    /// <exception cref="MarriageWeddingUnavailableException">Either partner's identity cannot be read.</exception>
    public async ValueTask<MarriageChurchSnapshot> SnapshotAsync(MarriageCeremony ceremony, MarriageChurchRoom room, uint key, CancellationToken ct)
    {
        var location = (await rules.LocationsAsync(room.TownId, ct).ConfigureAwait(false)).Single(x => x.AreaId == room.AreaId);
        var first = await characters.Get(ceremony.Applicant.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        var second = await characters.Get(ceremony.Partner.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        if (first is null || second is null) throw new MarriageWeddingUnavailableException();
        return new(room, new(checked((int)key)), genders.IsFemale(first.Job) ? first.Name : second.Name, genders.IsFemale(first.Job) ? second.Name : first.Name, location);
    }

    private async ValueTask<MarriageChurchSnapshot?> SnapshotForAsync(SelectedSessionIdentity actor,
        MarriageCeremony ceremony, MarriageChurchRoom room, CancellationToken ct)
    {
        var location = (await rules.LocationsAsync(room.TownId, ct).ConfigureAwait(false)).SingleOrDefault(x => x.AreaId == room.AreaId);
        var first = await characters.Get(ceremony.Applicant.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        var second = await characters.Get(ceremony.Partner.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        if (location is null || first is null || second is null) return null;
        var peer = actor.CharacterId == ceremony.Applicant.CharacterId ? ceremony.Partner.CharacterId : ceremony.Applicant.CharacterId;
        // A21 cinema 008884A0: first is bride, second is groom, regardless of payer.
        return new(room, peer, genders.IsFemale(first.Job) ? first.Name : second.Name, genders.IsFemale(first.Job) ? second.Name : first.Name, location);
    }

    private static TownAreaKey Key(MarriageChurchRoom room) => new(room.ChannelId, room.TownId, room.AreaId);
}
