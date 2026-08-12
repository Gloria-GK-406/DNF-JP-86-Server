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
    ValueTask CleanupAsync(
        SelectedSessionIdentity session, bool includeOwner, CancellationToken cancellationToken);
}

public interface ISelectCharacterSessionEntryService
{
    SelectCharacterPvpLobbyProjection? PlanPvp(SelectedSessionIdentity session, SelectCharacterResult result);
    IReadOnlyList<ExplicitProjectionTarget> PlanPvpPublication(SelectedSessionIdentity session, SelectCharacterResult result);
    void Publish(SelectedSessionIdentity session, SelectCharacterResult result);
    void ReplaceRuntimeExact(SelectedSessionIdentity session, SelectCharacterResult result);
    void MarkPvpReady(SelectedSessionIdentity session, SelectCharacterResult result);
    SelectCharacterDungeonRejoinProjection? PlanRejoin(SelectedSessionIdentity session, SelectCharacterResult result);
}

internal sealed class SelectedSessionCleanupService(
    ISelectedSessionDirectory sessions,
    ISelectedSessionRuntimeDirectory runtimes,
    IEntryInventoryLeaseRegistry inventoryLeases,
    IActiveCharacterService activeCharacters,
    ISessionPresenceRegistry presence,
    IExpertStoreRegistry expertStores,
    IPvpLobbyRegistry pvp,
    IDungeonRejoinRegistry rejoin,
    IEnumerable<ISelectedSessionPositionPersistence> positionPersistence,
    INetworkMessageSender sender) : ISelectedSessionCleanupService
{
    public async ValueTask CleanupAsync(
        SelectedSessionIdentity session, bool includeOwner, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var currentPresence = presence.Find(session.CharacterId);
        var targets = new List<ExplicitProjectionTarget>();
        var required = new List<ISelectCharacterSupplementalProjection>();
        if (currentPresence?.Session == session)
        {
            foreach (var peer in presence.InArea(
                         currentPresence.TownId, currentPresence.AreaId, session.CharacterId,
                         currentPresence.ListenerPort))
            {
                var peerSession = peer.Session ?? sessions.FindByCharacter(peer.CharacterId);
                if (peerSession is null) continue;
                var values = new List<ISelectCharacterSupplementalProjection>();
                if (expertStores.HasOpenStore(session.CharacterId))
                    values.Add(new SelectCharacterExpertStoreCloseProjection(session.WireUserId));
                values.Add(new SelectCharacterTownLeaveProjection(session.WireUserId));
                targets.Add(new ExplicitProjectionTarget(peerSession.ConnectionId, values));
            }
            if (includeOwner && expertStores.HasOpenStore(session.CharacterId))
                required.Add(new SelectCharacterExpertStoreCloseProjection(session.WireUserId));
        }
        if (targets.Count > 0 || required.Count > 0)
        {
            try
            {
                await sender.SendProjectionAsync(session.ConnectionId,
                    new SelectCharacterProjectionDelivery(required, targets), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) { failures.Add(error); }
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
        if (failures.Count != 0)
            throw new AggregateException("Selected-session cleanup completed with failures.", failures);

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
    public SelectCharacterPvpLobbyProjection? PlanPvp(
        SelectedSessionIdentity session, SelectCharacterResult result)
    {
        var lobby = result.Session.Adjacent.PvpLobby;
        if (lobby is null) return null;
        var identities = pvp.ReadyOn(session.ListenerPort, session.CharacterId).Select(value =>
        {
            var owner = value.Session ?? sessions.FindByCharacter(value.CharacterId)
                ?? throw new InvalidDataException("PvP participant session is unavailable.");
            return new SelectCharacterPvpIdentityProjection(owner.WireUserId, runtimes.GetRequired(owner));
        }).ToArray();
        var offeredRooms = pvp.OfferedOn(session.ListenerPort).ToDictionary(value => value.RoomId);
        var rooms = lobby.Rooms.Select(room =>
        {
            if (!offeredRooms.TryGetValue(room.RoomId, out var source))
                throw new InvalidDataException($"PvP room {room.RoomId} is unavailable.");
            return new SelectCharacterPvpRoomProjection(
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
        return new SelectCharacterPvpLobbyProjection(
            identities, rooms, new SelectCharacterPvpIdentityProjection(session.WireUserId, result));
    }

    public IReadOnlyList<ExplicitProjectionTarget> PlanPvpPublication(
        SelectedSessionIdentity session, SelectCharacterResult result)
    {
        var newcomer = new SelectCharacterPvpIdentityProjection(session.WireUserId, result);
        return pvp.ReadyOn(session.ListenerPort, session.CharacterId)
            .Select(value => value.Session ?? sessions.FindByCharacter(value.CharacterId))
            .Where(value => value is not null)
            .Select(value => new ExplicitProjectionTarget(value!.ConnectionId, [newcomer]))
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

    public SelectCharacterDungeonRejoinProjection? PlanRejoin(
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
        return new SelectCharacterDungeonRejoinProjection(
            rebound.PartyId, rebound.ParticipantUserIds);
    }
}
