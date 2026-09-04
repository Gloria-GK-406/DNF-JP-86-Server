using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Sessions;

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

internal sealed class SelectedSessionCleanupService(
    ISelectedSessionDomainService sessions,
    ISelectedSessionRuntimeDirectory runtimes,
    IEntryInventoryLeaseRegistry inventoryLeases,
    ICharacterContainerDomainService containers,
    ICharacterItemCooldownDomainService cooldowns,
    IActiveCharacterDomainService activeCharacters,
    ISessionPresenceDomainService presence,
    IExpertStoreDomainService expertStores,
    IPvpLobbyDomainService pvp,
    IEnumerable<ISelectedSessionPositionPersistence> positionPersistence,
    ICharacterPositionDomainService positions) : ISelectedSessionCleanupDomainService
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
        //
        // Where the character is standing is asked of the service that owns it rather than read
        // off the runtime snapshot. The two are put there and taken away together -- publication
        // records both in one method and the two removals below are in one run -- so the added
        // conjunct answers for exactly the sessions the first two already admitted. What is not
        // moved is when this happens: it stays here, inside whatever use case is running, for the
        // reason the persistence seam states.
        if (runtimes.TryGet(session, out var runtime, out var shouldPersistPosition)
            && runtime is not null && shouldPersistPosition
            && positions.Find(session.CharacterId) is { } standing)
        {
            var persistence = positionPersistence.SingleOrDefault();
            if (persistence is null)
                failures.Add(new InvalidOperationException(
                    "Selected-session position persistence is unavailable."));
            else
                await RunAsync(() => persistence.PersistAsync(
                    session.AccountId, session.CharacterId, standing,
                    CancellationToken.None));
        }
        Run(() => expertStores.CloseExact(session));
        Run(() => presence.WithdrawExact(session));
        Run(() => pvp.ClearReadyExact(session));
        Run(() => inventoryLeases.ReleaseExact(session));
        Run(() => positions.ForgetExact(session));
        Run(() => runtimes.RemoveExact(session));
        Run(() => activeCharacters.ClearExact(session));
        Run(() => sessions.ReleaseExact(session));

        // After the directory entry is gone: a character that already re-entered on another
        // connection owns the registration now, and dropping it would unbind a live session.
        Run(() =>
        {
            if (sessions.FindByCharacter(session.CharacterId) is null)
            {
                containers.Clear(session.CharacterId);
                cooldowns.Clear(session.CharacterId);
            }

            return true;
        });
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
    ISelectedSessionDomainService sessions,
    ISelectedSessionRuntimeDirectory runtimes,
    ISessionPresenceDomainService presence,
    IPvpLobbyDomainService pvp,
    ICharacterPositionDomainService positions) : ISelectCharacterSessionEntryDomainService
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

    /// <remarks>
    /// The position is handed to its own service in the same statement run that publishes the
    /// runtime state and admits the presence record, so the three are there for exactly the same
    /// span. What is handed over is the position entry settled on -- placement resolved it and the
    /// entry write already stored it -- rather than anything read back out of the runtime
    /// directory; from here on it is the service that answers, and the copy in the snapshot is not
    /// read again.
    /// </remarks>
    public void Publish(SelectedSessionIdentity session, SelectCharacterResult result)
    {
        runtimes.Publish(session, result, result.World.Channel.PersistsPosition);
        positions.Place(session, result.Character.Position);
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
