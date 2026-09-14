using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Parties;
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
    ICharacterPositionDomainService positions,
    IPartyService parties,
    IDungeonRunDomainService dungeonRuns,
    IEnumerable<IDungeonRunParticipantDepartureSink> dungeonDepartures) : ISelectedSessionCleanupDomainService
{
    public async ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(
        SelectedSessionIdentity session, bool includeOwner, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var currentPresence = presence.Find(session.CharacterId);
        var audience = new List<SelectedSessionIdentity>();
        var peerNotices = new List<SelectedSessionCleanupNotice>();
        var required = new List<SelectedSessionCleanupNotice>();
        var partyNotifications = PartyNotificationPlan.Empty;
        var partyRecipients = PartyNotificationRecipientPlan.Empty;
        var ownsSelectedCharacter = sessions.FindByCharacter(session.CharacterId) == session;
        if (currentPresence?.Session == session)
        {
            // Preserve the exact peer generations this area snapshot was built from. Reducing
            // them to character IDs would let a replacement on another listener receive this
            // departure after the old presence was sampled.
            //
            // Asked here rather than at the moment of sending, because the presence record this
            // departure is leaving is withdrawn a few statements below: resolving the area later
            // would be asking about a different moment.
            foreach (var peer in presence.Query(SessionPresenceScope.VisibleTownArea(
                         currentPresence.ListenerPort,
                         currentPresence.TownId,
                         currentPresence.AreaId,
                         session.CharacterId)))
            {
                if (peer.Session is { } exact
                    && sessions.FindByCharacter(peer.CharacterId) == exact)
                {
                    audience.Add(exact);
                }
            }

            // One set for all of them, because they never differ; saying it once is what makes that
            // true rather than coincidental.
            if (audience.Count > 0)
            {
                if (expertStores.HasOpenStore(session.CharacterId))
                    peerNotices.Add(new SelectedSessionCleanupNotice(
                        SelectedSessionCleanupNoticeKind.ExpertStoreClosed, session.WireUserId));
                peerNotices.Add(new SelectedSessionCleanupNotice(
                    SelectedSessionCleanupNoticeKind.TownDeparture, session.WireUserId));
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
        if (ownsSelectedCharacter)
        {
            var run = dungeonRuns.Find(session.CharacterId);
            if (run is not null)
            {
                var ended = false;
                Run(() => ended = dungeonRuns.End(session.CharacterId, run.RunId));
                if (ended)
                {
                    foreach (var departure in dungeonDepartures)
                    {
                        Run(() =>
                        {
                            departure.ParticipantDeparted(run.RunId, session.CharacterId);
                            return true;
                        });
                    }
                }
            }

            Run(() =>
            {
                var partyCleanup = parties.CleanupCharacter(session.CharacterId);
                if (partyCleanup.Succeeded)
                {
                    partyNotifications = partyCleanup.Notifications;
                    partyRecipients = CapturePartyRecipients(partyNotifications);
                }
                return partyCleanup.Succeeded;
            });
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
            session.ConnectionId,
            required,
            audience,
            peerNotices,
            partyNotifications,
            partyRecipients);
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

    private PartyNotificationRecipientPlan CapturePartyRecipients(PartyNotificationPlan plan)
    {
        var characterIds = new HashSet<CharacterId>(plan.ClearAudience);
        if (plan.Snapshot is { } snapshot)
            characterIds.UnionWith(snapshot.Members.Select(member => member.CharacterId));
        if (plan.Invitation is { } invitation)
            characterIds.Add(invitation.TargetCharacterId);
        if (plan.Acceptance is { } acceptance)
        {
            characterIds.Add(acceptance.InviterCharacterId);
            characterIds.Add(acceptance.InviteeCharacterId);
        }

        var characterRecipients = characterIds
            .Select(sessions.FindByCharacter)
            .Where(candidate => candidate is not null)
            .Cast<SelectedSessionIdentity>()
            .Distinct()
            .ToArray();
        var scopes = new[]
            {
                plan.DirectoryChange?.Previous,
                plan.DirectoryChange?.Current,
            }
            .Where(scope => scope is not null)
            .Cast<PartyDirectoryScope>()
            .Distinct()
            .Select(scope => new PartyDirectoryRecipientGroup(
                scope,
                presence.Query(SessionPresenceScope.VisiblePartyArea(
                        scope.ChannelId,
                        scope.TownId,
                        scope.AreaId))
                    .Where(item => item.Session is { } exact
                        && sessions.FindByCharacter(item.CharacterId) == exact)
                    .Select(item => item.Session!)
                    .Distinct()
                    .ToArray()))
            .ToArray();
        return new PartyNotificationRecipientPlan(characterRecipients, scopes);
    }
}

internal sealed class SelectCharacterSessionEntryService(
    ISelectedSessionDomainService sessions,
    ISelectedSessionRuntimeDirectory runtimes,
    ISessionPresenceDomainService presence,
    IPvpLobbyDomainService pvp,
    ICharacterPositionDomainService positions) : ISelectCharacterSessionEntryDomainService
{
    public TownParticipantPublicationPlan? PlanTownPublication(
        SelectedSessionIdentity session, SelectCharacterResult result)
    {
        var position = positions.Find(session.CharacterId)
            ?? throw new InvalidDataException("The published town position is unavailable.");
        var occupants = presence.Query(SessionPresenceScope.VisibleTownArea(
            session.ListenerPort,
            position.TownId,
            position.AreaId,
            session.CharacterId));
        if (occupants.Count == 0)
            return null;

        var existing = new List<TownParticipantEntryState>(occupants.Count);
        var audience = new List<SelectedSessionIdentity>(occupants.Count);
        foreach (var occupant in occupants)
        {
            if (occupant.Session is not { } owner
                || sessions.FindByCharacter(occupant.CharacterId) != owner
                || !runtimes.TryGet(owner, out var otherResult, out _)
                || otherResult is null
                || positions.Find(occupant.CharacterId) is not { } otherPosition)
            {
                continue;
            }

            existing.Add(CreateTownParticipant(otherResult, otherPosition));
            audience.Add(owner);
        }

        if (existing.Count == 0)
            return null;

        return new TownParticipantPublicationPlan(
            audience,
            new TownPresenceSynchronizationState(
                position.TownId,
                position.AreaId,
                CreateTownParticipant(result, position),
                existing));
    }

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

    /// <summary>Which characters should hear that this one has joined them.</summary>
    /// <remarks>
    /// It says who, and stops. Working out which connection each of them is on used to happen
    /// here, and it is the reason this service was ever handed the session directory: a service
    /// about lobbies had to know what a socket was in order to name three people. That question
    /// now has one place to live, and it is not this one.
    /// </remarks>
    public PvpParticipantPublicationPlan PlanPvpPublication(
        SelectedSessionIdentity session, SelectCharacterResult result) =>
        new(
            [.. pvp.ReadyOn(session.ListenerPort, session.CharacterId)
                .Select(value => value.CharacterId)],
            CreatePvpParticipant(result));

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
            session.CharacterId,
            session.ListenerPort,
            spawn.TownId,
            spawn.AreaId,
            StandingInTown: true,
            Session: session,
            SharesTownPresence: spawn.SharesTownPresence,
            ChannelId: result.World.Channel.ChannelId));
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

    private static TownParticipantEntryState CreateTownParticipant(
        SelectCharacterResult result,
        CharacterPositionState position)
    {
        var equipment = result.CharacterInventory.Containers
            .SingleOrDefault(value =>
                value.ContainerType == Dfo.Server.Data.Inventory.InventoryContainerType.Equipment)
            ?.Items ?? [];
        var spawn = result.World.Channel.Spawn with
        {
            TownId = position.TownId,
            AreaId = position.AreaId,
            X = position.X,
            Y = position.Y,
            Direction = position.Direction,
        };
        return new TownParticipantEntryState(
            result.Character,
            result.CharacterEntry.Public,
            equipment,
            result.CharacterEntry.Features.Pets,
            spawn);
    }
}
