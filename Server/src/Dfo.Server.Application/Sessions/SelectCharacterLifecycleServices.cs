using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.IllusionTower;
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
    ICharacterContainerDomainService containers,
    ITownAreaDomainService townAreas,
    IExpertStoreDomainService expertStores,
    IPvpLobbyDomainService pvp,
    IEnumerable<ISelectedSessionPositionPersistence> positionPersistence,
    IOnlineCharacterRuntimeDomainService runtime,
    IChannelEntryDomainService channels,
    IPartyService parties,
    IDungeonRunDomainService dungeonRuns,
    IEnumerable<IDungeonRunParticipantDepartureSink> dungeonDepartures,
    ICreatureRuntimeDomainService? creatures = null,
    IIllusionTowerDomainService? illusionTower = null,
    IEnumerable<IIllusionTowerDepartureSink>? illusionTowerDepartures = null,
    Dfo.Server.Abstractions.Marriage.IMarriageHousingSessionDomainService? housing = null,
    Dfo.Server.Abstractions.World.BossTower.IBossTowerDomainService? bossTower = null,
    IEnumerable<Dfo.Server.Abstractions.World.BossTower.IBossTowerDepartureSink>? bossTowerDepartures = null) : ISelectedSessionCleanupDomainService
{
    public async ValueTask<SelectedSessionCleanupNotificationPlan> CleanupAsync(
        SelectedSessionIdentity session, bool includeOwner, IEffectFootprintRecorder footprint, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var currentMember = townAreas.Find(session.CharacterId);
        var audience = new List<SelectedSessionIdentity>();
        var peerNotices = new List<SelectedSessionCleanupNotice>();
        var required = new List<SelectedSessionCleanupNotice>();
        var partyNotifications = PartyNotificationPlan.Empty;
        var partyRecipients = PartyNotificationRecipientPlan.Empty;
        var ownsSelectedCharacter = sessions.StandingOf(session) is not null;
        if (currentMember is not null && currentMember.Session == session)
        {
            // Preserve the exact peer generations this area snapshot was built from. Reducing
            // them to character IDs would let a replacement on another listener receive this
            // departure after the old presence was sampled.
            //
            // Asked here rather than at the moment of sending, because the presence record this
            // departure is leaving is withdrawn a few statements below: resolving the area later
            // would be asking about a different moment.
            foreach (var peer in townAreas.Find(currentMember.Area)?.Members
                         .Where(member => member.SharesTownPresence
                             && member.CharacterId != session.CharacterId)
                     ?? [])
            {
                if (sessions.TryGetInWorld(peer.CharacterId, out var exact)
                    && peer.Session == exact)
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
        // Only a session that entered the world has a position of its own to store, and whether
        // its channel keeps positions is the channel's answer, not something kept beside the
        // session. Neither may be borrowed from a replaced session.
        if (runtime.PositionOf(session) is { } standing)
        {
            var persists = false;
            await RunAsync(async () => persists = !(await channels.ResolveAsync(
                session.ListenerPort, CancellationToken.None).ConfigureAwait(false)).IsTransient);
            if (persists)
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
        }
        // Captured before the party cleanup below: the run is reached through the party.
        DungeonRunState? run = null;
        if (ownsSelectedCharacter)
        {
            if (creatures is not null)
                await RunAsync(() => creatures.LeaveAsync(session, CancellationToken.None));
            run = dungeonRuns.Find(session.CharacterId);
        }

        // Tower membership belongs to the exact selected-session generation. It must be released
        // even after that generation has already lost the selected-session directory race.
        if (illusionTower?.Find(session) is { } towerRun)
        {
            TowerActionResult? towerDeparture = null;
            await RunAsync(async () =>
            {
                towerDeparture = await illusionTower.LeaveAsync(
                    new TowerActionContext(session, towerRun.RunId, towerRun.Room.Version),
                    disconnected: true, footprint, CancellationToken.None).ConfigureAwait(false);
            });
            if (towerDeparture is { Changed: true } departed)
            {
                foreach (var sink in illusionTowerDepartures ?? [])
                    await RunAsync(() => sink.DepartedAsync(departed, CancellationToken.None));
            }
        }

        if (bossTower?.Find(session) is { } bossRun)
        {
            Dfo.Server.Abstractions.World.BossTower.BossTowerActionResult? bossDeparture = null;
            await RunAsync(async () => bossDeparture = await bossTower.LeaveAsync(
                new(session, bossRun.RunId, bossRun.Room.Version), true, footprint, CancellationToken.None).ConfigureAwait(false));
            if (bossDeparture is { Changed: true } departed)
                foreach (var sink in bossTowerDepartures ?? [])
                    await RunAsync(() => sink.DepartedAsync(departed, CancellationToken.None));
        }

        if (ownsSelectedCharacter)
        {
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
            // The run's members are its party's: having left the party, the character's part of
            // the run ends, or the whole run when nobody else is left in it.
            if (run is not null)
            {
                var ended = false;
                await RunAsync(async () => { ended = await dungeonRuns.EndAsync(session.CharacterId, run.RunId).ConfigureAwait(false); });
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
        }
        Run(() => expertStores.CloseExact(session));
        Run(() => { housing?.CloseExact(session); return true; });
        Run(() => pvp.ClearReadyExact(session));
        // Position and its place in a town area, vitals, cooldowns and the spirit potion go together,
        // and only if this session still owns them: a successor that has already begun keeps its own.
        Run(() => runtime.EndExact(session));
        Run(() => sessions.ReleaseExact(session));

        // After the selection is gone: a character that already re-entered on another
        // connection owns the registration now, and dropping it would unbind a live session.
        Run(() =>
        {
            if (sessions.FindByCharacter(session.CharacterId) is null)
                containers.Clear(session.CharacterId);
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
            .Select(id => sessions.TryGetInWorld(id, out var inWorld) ? inWorld : null)
            .OfType<SelectedSessionIdentity>()
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
                (townAreas.Find(new TownAreaKey(
                         scope.ChannelId,
                         scope.TownId,
                         scope.AreaId, scope.Instance))?.Members ?? [])
                    .Select(item => item.SharesTownPresence
                        && sessions.TryGetInWorld(item.CharacterId, out var exact)
                        && item.Session == exact ? exact : null)
                    .OfType<SelectedSessionIdentity>()
                    .Distinct()
                    .ToArray()))
            .ToArray();
        return new PartyNotificationRecipientPlan(characterRecipients, scopes);
    }
}

internal sealed class SelectCharacterSessionEntryService(
    ISelectedSessionDomainService sessions,
    ISelectedCharacterCurrentViewDomainService currentViews,
    ITownAreaDomainService townAreas,
    IPvpLobbyDomainService pvp,
    IOnlineCharacterRuntimeDomainService runtime,
    ICreatureRuntimeDomainService? creatures = null) : ISelectCharacterSessionEntryDomainService
{
    public async ValueTask<TownParticipantPublicationPlan?> PlanTownPublicationAsync(
        SelectedSessionIdentity session, SelectCharacterResult result,
        CancellationToken cancellationToken)
    {
        var position = runtime.PositionOf(session)
            ?? throw new InvalidDataException("The published town position is unavailable.");
        var current = townAreas.Find(session.CharacterId);
        if (current is null || current.Session != session || !current.SharesTownPresence)
            return null;
        var occupants = townAreas.Find(current.Area)?.Members
            .Where(member => member.SharesTownPresence
                && member.CharacterId != session.CharacterId)
            .ToArray() ?? [];
        if (occupants.Length == 0)
            return null;

        var existing = new List<TownParticipantEntryState>(occupants.Length);
        var audience = new List<SelectedSessionIdentity>(occupants.Length);
        foreach (var occupant in occupants)
        {
            if (!sessions.TryGetInWorld(occupant.CharacterId, out var owner))
                continue;
            var otherResult = await currentViews.TryLoadAsync(owner, cancellationToken).ConfigureAwait(false);
            if (occupant.Session != owner || otherResult is null)
            {
                continue;
            }

            existing.Add(CreateTownParticipant(otherResult, occupant.Position));
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

    public async ValueTask<PvpLobbySynchronizationState?> PlanPvpAsync(
        SelectedSessionIdentity session, SelectCharacterResult result,
        CancellationToken cancellationToken)
    {
        var lobby = result.Session.Adjacent.PvpLobby;
        if (lobby is null) return null;
        var identities = new List<PvpParticipantEntryState>();
        foreach (var value in pvp.ReadyOn(session.ListenerPort, session.CharacterId))
        {
            // Someone else's connection going away is not a reason for this entry to fail: a
            // participant who is no longer in the world is left out of the lobby this one sees.
            SelectedSessionIdentity? owner = value.Session is { } exact
                ? sessions.IsInWorld(exact) ? exact : null
                : sessions.TryGetInWorld(value.CharacterId, out var found) ? found : null;
            if (owner is null
                || await currentViews.TryLoadAsync(owner, cancellationToken).ConfigureAwait(false) is not { } current)
                continue;
            identities.Add(CreatePvpParticipant(current));
        }
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
    /// The selection has already entered the world; this begins what the character is like while
    /// it is there. The entry result remains a one-shot message-building value.
    /// </remarks>
    public void Publish(SelectedSessionIdentity session, SelectCharacterResult result)
    {
        if (!sessions.IsSelected(session))
            throw new InvalidOperationException("Cannot publish a replaced selection.");
        creatures?.Enter(session);
        var position = result.Character.Position;
        runtime.Begin(
            session,
            new TownAreaKey(result.World.Channel.ChannelId, position.TownId, position.AreaId),
            position,
            result.World.Channel.Spawn.SharesTownPresence,
            checked((byte)Math.Clamp(result.CharacterEntry.Public.DerivedStats.CurrentHpMpPercent, 0, 100)));
    }

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
