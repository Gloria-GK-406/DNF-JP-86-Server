using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Networking;

namespace Dfo.Server.Application.Sessions;

public interface ISelectedSessionCleanupService
{
    ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(
        SelectedSessionIdentity session, bool includeOwner, CancellationToken cancellationToken);
}

public enum SelectedSessionCleanupNoticeKind
{
    ExpertStoreClosed = 1,
    TownDeparture = 2,
}

public sealed record SelectedSessionCleanupNotice(
    SelectedSessionCleanupNoticeKind Kind,
    ushort UserId);

public sealed record SelectedSessionCleanupNotificationTarget(
    NetworkConnectionId ConnectionId,
    IReadOnlyList<SelectedSessionCleanupNotice> OrderedNotices);

public sealed record SelectedSessionCleanupNotificationPlan(
    NetworkConnectionId OwnerConnectionId,
    IReadOnlyList<SelectedSessionCleanupNotice> RequiredOwnerNotices,
    IReadOnlyList<SelectedSessionCleanupNotificationTarget> BestEffortTargets)
{
    public static SelectedSessionCleanupNotificationPlan Empty(SelectedSessionIdentity session) =>
        new(session.ConnectionId, [], []);
}

/// <summary>
/// Exact cleanup completed with one or more failures after its notification targets were resolved.
/// The plan remains deliverable so a cleanup failure does not silently suppress peer state changes.
/// </summary>
public sealed class SelectedSessionCleanupFailure(
    SelectedSessionCleanupNotificationPlan notificationPlan,
    IReadOnlyCollection<Exception> failures)
    : AggregateException("Selected-session cleanup completed with failures.", failures)
{
    public SelectedSessionCleanupNotificationPlan NotificationPlan { get; } = notificationPlan;
}

public interface ISelectCharacterSessionEntryService
{
    PvpLobbySynchronizationState? PlanPvp(SelectedSessionIdentity session, SelectCharacterResult result);
    IReadOnlyList<PvpParticipantPublicationTarget> PlanPvpPublication(
        SelectedSessionIdentity session, SelectCharacterResult result);
    void Publish(SelectedSessionIdentity session, SelectCharacterResult result);
    void ReplaceRuntimeExact(SelectedSessionIdentity session, SelectCharacterResult result);
    void MarkPvpReady(SelectedSessionIdentity session, SelectCharacterResult result);
    DungeonRejoinSynchronizationState? PlanRejoin(SelectedSessionIdentity session, SelectCharacterResult result);
}

public sealed record PvpParticipantPublicationTarget(
    NetworkConnectionId ConnectionId,
    PvpParticipantEntryState Participant);

internal sealed class SelectedSessionCleanupService(
    ISelectedSessionDirectory sessions,
    ISelectedSessionRuntimeDirectory runtimes,
    IEntryInventoryLeaseRegistry inventoryLeases,
    IActiveCharacterService activeCharacters,
    ISessionPresenceRegistry presence,
    IExpertStoreRegistry expertStores,
    IPvpLobbyRegistry pvp,
    IDungeonRejoinRegistry rejoin,
    IEnumerable<ISelectedSessionPositionPersistence> positionPersistence) : ISelectedSessionCleanupService
{
    public async ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(
        SelectedSessionIdentity session, bool includeOwner, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var currentPresence = presence.Find(session.CharacterId);
        var targets = new List<SelectedSessionCleanupNotificationTarget>();
        var required = new List<SelectedSessionCleanupNotice>();
        if (currentPresence?.Session == session)
        {
            foreach (var peer in presence.InArea(
                         currentPresence.TownId, currentPresence.AreaId, session.CharacterId,
                         currentPresence.ListenerPort))
            {
                var peerSession = peer.Session ?? sessions.FindByCharacter(peer.CharacterId);
                if (peerSession is null) continue;
                var values = new List<SelectedSessionCleanupNotice>();
                if (expertStores.HasOpenStore(session.CharacterId))
                    values.Add(new SelectedSessionCleanupNotice(
                        SelectedSessionCleanupNoticeKind.ExpertStoreClosed, session.WireUserId));
                values.Add(new SelectedSessionCleanupNotice(
                    SelectedSessionCleanupNoticeKind.TownDeparture, session.WireUserId));
                targets.Add(new SelectedSessionCleanupNotificationTarget(
                    peerSession.ConnectionId, values));
            }
            if (includeOwner && expertStores.HasOpenStore(session.CharacterId))
                required.Add(new SelectedSessionCleanupNotice(
                    SelectedSessionCleanupNoticeKind.ExpertStoreClosed, session.WireUserId));
        }

        // Every mutation is exact and isolated; a stale session can attempt all safely.
        if (runtimes.TryGet(session, out var runtime, out var shouldPersistPosition)
            && runtime is not null && shouldPersistPosition)
        {
            var persistence = positionPersistence.SingleOrDefault();
            if (persistence is null)
                failures.Add(new InvalidOperationException(
                    "Selected-session position persistence is unavailable."));
            else
                await RunAsync(() => persistence.PersistAsync(
                    session.AccountId, session.CharacterId, runtime.Character.Position,
                    CancellationToken.None));
        }
        Run(() => expertStores.CloseExact(session));
        Run(() => presence.WithdrawExact(session));
        Run(() => pvp.ClearReadyExact(session));
        Run(() => rejoin.EndSessionExact(session));
        Run(() => inventoryLeases.ReleaseExact(session));
        Run(() => runtimes.RemoveExact(session));
        Run(() => activeCharacters.ClearExact(session));
        Run(() => sessions.TryRemoveExact(session));
        var notificationPlan = new SelectedSessionCleanupNotificationPlan(
            session.ConnectionId, required, targets);
        if (failures.Count != 0)
            throw new SelectedSessionCleanupFailure(notificationPlan, failures);
        return notificationPlan;

        void Run(Func<bool> action)
        {
            try { _ = action(); }
            catch (Exception error) { failures.Add(error); }
        }


        async ValueTask RunAsync(Func<ValueTask> action)
        {
            try { await action().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
        }
    }
}

internal sealed class SelectCharacterSessionEntryService(
    ISelectedSessionDirectory sessions,
    ISelectedSessionRuntimeDirectory runtimes,
    ISessionPresenceRegistry presence,
    IPvpLobbyRegistry pvp,
    IDungeonRejoinRegistry rejoin,
    TimeProvider timeProvider) : ISelectCharacterSessionEntryService
{
    public PvpLobbySynchronizationState? PlanPvp(
        SelectedSessionIdentity session, SelectCharacterResult result)
    {
        var lobby = result.Session.Adjacent.PvpLobby;
        if (lobby is null) return null;
        var identities = pvp.ReadyOn(session.ListenerPort, session.CharacterId).Select(value =>
        {
            var owner = value.Session ?? sessions.FindByCharacter(value.CharacterId)
                ?? throw new InvalidDataException("PvP participant session is unavailable.");
            return CreatePvpParticipant(runtimes.GetRequired(owner));
        }).ToArray();
        var offeredRooms = pvp.OfferedOn(session.ListenerPort).ToDictionary(value => value.RoomId);
        var rooms = lobby.Rooms.Select(room =>
        {
            if (!offeredRooms.TryGetValue(room.RoomId, out var source))
                throw new InvalidDataException($"PvP room {room.RoomId} is unavailable.");
            return new PvpRoomSnapshotState(
            room.RoomId, room.NameType,
            (source.Name
                ?? throw new InvalidDataException("PvP room name is unavailable.")),
            source.RoomState,
            source.ManagerSeat,
            room.MapIndex, room.BattleMode,
            source.Seats is { Count: 8 } seats
                ? seats : throw new InvalidDataException("PvP room seats are unavailable."),
            room.HasPassword,
            source.MatchingType);
        }).ToArray();
        return new PvpLobbySynchronizationState(
            identities, rooms, CreatePvpParticipant(result));
    }

    public IReadOnlyList<PvpParticipantPublicationTarget> PlanPvpPublication(
        SelectedSessionIdentity session, SelectCharacterResult result)
    {
        var newcomer = CreatePvpParticipant(result);
        return pvp.ReadyOn(session.ListenerPort, session.CharacterId)
            .Select(value => value.Session ?? sessions.FindByCharacter(value.CharacterId))
            .Where(value => value is not null)
            .Select(value => new PvpParticipantPublicationTarget(value!.ConnectionId, newcomer))
            .ToArray();
    }

    public void Publish(SelectedSessionIdentity session, SelectCharacterResult result)
    {
        runtimes.Publish(session, result, result.World.Channel.PersistsPosition);
        var spawn = result.World.Channel.Spawn;
        presence.Admit(new SessionPresence(
            session.CharacterId, session.ListenerPort, spawn.TownId, spawn.AreaId, true, session));
    }

    public void ReplaceRuntimeExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
        runtimes.ReplaceExact(session, result);

    public void MarkPvpReady(SelectedSessionIdentity session, SelectCharacterResult result)
    {
        if (result.Session.Adjacent.PvpLobby is null)
            throw new InvalidOperationException("PvP readiness does not apply to this session.");
        pvp.MarkReady(new PvpLobbyParticipant(session.CharacterId, session.ListenerPort, session));
    }

    public DungeonRejoinSynchronizationState? PlanRejoin(
        SelectedSessionIdentity session, SelectCharacterResult result)
    {
        var attachment = rejoin.Find(session.CharacterId);
        if (attachment is null) return null;
        if (attachment.AccountId != session.AccountId || !attachment.IsDetached
            || attachment.PartyId is <= 0 or > ushort.MaxValue
            || attachment.ExpiresAt <= timeProvider.GetUtcNow())
        {
            rejoin.ReleaseAttachmentExact(attachment);
            return null;
        }
        var rebound = attachment with { Session = session };
        if (!rejoin.TryReplaceExact(attachment, rebound))
            return null;
        return new DungeonRejoinSynchronizationState(
            rebound.PartyId, rebound.ParticipantUserIds);
    }

    private static PvpParticipantEntryState CreatePvpParticipant(SelectCharacterResult result)
    {
        var equipment = result.CharacterInventory.Containers
            .SingleOrDefault(value =>
                value.ContainerType == Dfo.Server.Data.Inventory.InventoryContainerType.Equipment)
            ?.Items ?? [];
        return new PvpParticipantEntryState(
            result.Character,
            result.CharacterEntry.Public,
            equipment,
            result.CharacterEntry.Features.Pets);
    }
}
