using System.Reflection;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Creatures;
using Dfo.Server.Protocol.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Creatures;

public sealed class CreatureProjectionDeliveryTests
{
    [Fact]
    public async Task Unequipped_evolution_updates_the_pet_key_without_overwriting_the_worn_slot()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var owner = Session(1, "owner");
            var sender = new Sender();
            var delivery = Create(new Sessions(owner), new Towns(Member(owner, Area, true)), sender, new Closer());
            var before = new CreatureState(500, 63000, 90, 2150, 19, [], 0, 0, 0, 0);
            var after = before with { ItemTemplateId = 63001, Experience = 2151, Level = 20 };
            var transformation = new CreatureTransformationReceipt(before, after,
                new BusinessSlot(BusinessInventorySpace.WornCreature, 0), ItemSnapshot.FromCore(StackableItemCore.Create(5, 63001, 500), false, new ItemIdentity(500),
                    creature: new InventoryCreatureDetailState(90, 0, 2151, 20, 0, 0, [], 0)))
                { CreatureId = 2 };
            await delivery.SendAsync(owner, [new(before, after, true) { Transformation = transformation }], default);
            Assert.Equal(500, Assert.Single(Assert.Single(sender.Notifications.OfType<CreatureListMessage>()).Pets).PetKey);
            Assert.Single(sender.Notifications.OfType<CreatureStateNotification>());
            Assert.DoesNotContain(sender.Notifications, x => x is CreatureExperienceNotification or CreatureEvolutionNotification);
            // Sender.SendExplicitAsync throws: a stale inventory projection would fail this test.
        });
    }
    private static readonly TownAreaKey Area = new(11, 3, 6);
    private static readonly TownAreaKey OtherArea = new(11, 3, 7);

    [Theory]
    [InlineData(true, "owner", "peer")]
    [InlineData(false, "owner")]
    public async Task Town_rename_reaches_only_visible_sessions_once(bool ownerShares,
        params string[] expected)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var owner = Session(1, "owner");
            var peer = Session(2, "peer");
            var hidden = Session(3, "hidden");
            var elsewhere = Session(4, "elsewhere");
            var sessions = new Sessions(owner, peer, hidden, elsewhere);
            var towns = new Towns(Member(owner, Area, ownerShares), Member(peer, Area, true),
                Member(hidden, Area, false), Member(elsewhere, OtherArea, true));
            var sender = new Sender();
            var delivery = Create(sessions, towns, sender, new Closer());

            var success = await delivery.SendVisibleAsync(owner,
                new CreatureRenameNotification(owner.WireUserId, [0x41]), default);

            Assert.True(success);
            Assert.Equal(expected, sender.Sent.Select(x => x.Value));
        });
    }

    [Fact]
    public async Task Peer_send_failure_closes_peer_and_continues_without_closing_owner()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var owner = Session(1, "owner");
            var failed = Session(2, "failed");
            var next = Session(3, "next");
            var sender = new Sender { Failing = failed.ConnectionId };
            var closer = new Closer();
            var delivery = Create(new Sessions(owner, failed, next),
                new Towns(Member(owner, Area, true), Member(failed, Area, true),
                    Member(next, Area, true)), sender, closer);

            var success = await delivery.SendVisibleAsync(owner,
                new CreatureRenameNotification(owner.WireUserId, [0x41]), default);

            Assert.True(success);
            Assert.Equal(["owner", "failed", "next"], sender.Sent.Select(x => x.Value));
            Assert.Equal(["failed"], closer.Closed.Select(x => x.Value));
        });
    }

    [Fact]
    public async Task Owner_send_failure_closes_owner_and_reports_failure()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var owner = Session(1, "owner");
            var sender = new Sender { Failing = owner.ConnectionId };
            var closer = new Closer();
            var delivery = Create(new Sessions(owner), new Towns(Member(owner, Area, true)), sender, closer);

            var success = await delivery.SendVisibleAsync(owner,
                new CreatureRenameNotification(owner.WireUserId, [0x41]), default);

            Assert.False(success);
            Assert.Equal(["owner"], closer.Closed.Select(x => x.Value));
        });
    }

    [Fact]
    public async Task Tower_visibility_reaches_only_current_exact_run_members()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var owner = Session(1, "owner");
            var peer = Session(2, "peer");
            var stale = Session(3, "stale-old");
            var replacement = stale with { ConnectionGeneration = 2, ConnectionId = new("stale-new") };
            var townPeer = Session(4, "town-peer");
            var sessions = new Sessions(owner, peer, replacement, townPeer);
            var tower = new TowerDomainStub(owner, peer, stale);
            var sender = new Sender();
            var delivery = Create(sessions,
                new Towns(Member(owner, Area, true), Member(townPeer, Area, true)),
                sender, new Closer(), tower);

            var success = await delivery.SendVisibleAsync(owner,
                new CreatureRenameNotification(owner.WireUserId, [0x41]), default);

            Assert.True(success);
            Assert.Equal(["owner", "peer"], sender.Sent.Select(value => value.Value));
        });
    }

    [Fact]
    public async Task Tower_visibility_does_not_rebind_a_peer_replaced_after_owner_delivery()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var owner = Session(1, "owner");
            var peer = Session(2, "peer-old");
            var replacement = peer with { ConnectionGeneration = 2, ConnectionId = new("peer-new") };
            var identities = new[] { owner, peer };
            var sessions = new Sessions(identities);
            var sender = new Sender
            {
                AfterSend = connection =>
                {
                    if (connection == owner.ConnectionId) identities[1] = replacement;
                },
            };
            var delivery = Create(sessions, new Towns(), sender, new Closer(),
                new TowerDomainStub(owner, peer));

            var success = await delivery.SendVisibleAsync(owner,
                new CreatureRenameNotification(owner.WireUserId, [0x41]), default);

            Assert.True(success);
            Assert.Equal(["owner"], sender.Sent.Select(value => value.Value));
        });
    }

    private static CreatureProjectionDelivery Create(Sessions sessions, Towns towns, Sender sender,
        Closer closer, TowerDomainStub? tower = null) => new(sessions, DispatchProxy.Create<IDungeonRunDomainService, NullProxy>(),
            towns, sender, DispatchProxy.Create<ICreatureRuntimeDomainService, NullProxy>(), closer,
            NullLogger<CreatureProjectionDelivery>.Instance, towers: tower);

    private static SelectedSessionIdentity Session(int id, string connection) =>
        SelectedSessionIdentity.Create(new AccountId(id), new CharacterId(id),
            new NetworkConnectionId(connection), id, 10011);

    private static TownAreaMember Member(SelectedSessionIdentity session, TownAreaKey area,
        bool shares) => new(session.CharacterId, session.TownAreaLease(), area,
            new CharacterPositionState(area.TownId, area.AreaId, 100, 200, 1), shares);

    public class NullProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.ReturnType == typeof(bool) ? false : null;
    }

    private sealed class Sessions(params SelectedSessionIdentity[] identities) : ISelectedSessionDomainService
    {
        // Test double: the current selection counts as being in the world, and
        // remembers which sessions were fenced.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            Dfo.Server.Data.World.SelectedSessionIdentity, bool> testDoubleFences = new();
        void Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.EnterWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) { }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.Fence(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
            && testDoubleFences.TryAdd(session, true) | true;
        Dfo.Server.Data.World.SelectedSessionStanding? Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.StandingOf(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
                ? new(Dfo.Server.Data.World.SelectedSessionPhase.InWorld, testDoubleFences.ContainsKey(session), false)
                : null;
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsInWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session)
                is { Phase: Dfo.Server.Data.World.SelectedSessionPhase.InWorld };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsSelected(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session) is { ConnectionRevoked: false };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Characters.CharacterId characterId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByCharacter(characterId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            ushort wireUserId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByWireUserId(wireUserId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Networking.NetworkConnectionId connectionId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByConnection(connectionId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => session;
        public bool ReleaseExact(SelectedSessionIdentity session) => true;
        public SelectedSessionIdentity? Find(AccountId accountId) =>
            identities.FirstOrDefault(x => x.AccountId == accountId);
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
            identities.FirstOrDefault(x => x.CharacterId == characterId);
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
            identities.FirstOrDefault(x => x.WireUserId == wireUserId);
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            identities.FirstOrDefault(x => x.ConnectionId == connectionId);
    }

    private sealed class Towns(params TownAreaMember[] members) : ITownAreaDomainService
    {
        public TownAreaMember? Find(CharacterId characterId) =>
            members.FirstOrDefault(x => x.CharacterId == characterId);
        public TownAreaSnapshot? Find(TownAreaKey key) => new(key, 1, 1,
            members.Where(x => x.Area == key).ToArray());
        public IReadOnlyList<TownAreaSnapshot> Query(TownAreaFilter filter) => [];
        public TownAreaMembershipChange Enter(TownAreaMember member) => throw new NotSupportedException();
        public TownAreaMembershipChange? LeaveExact(CharacterId characterId,
            TownAreaMembershipLease lease, TownAreaDepartureReason reason) =>
            throw new NotSupportedException();
        public TownAreaMembershipChange? TransferExact(CharacterId characterId,
            TownAreaMembershipLease lease, TownAreaKey destination,
            CharacterPositionState position, bool sharesTownPresence) =>
            throw new NotSupportedException();
        public TownAreaMovement? MoveExact(CharacterId characterId,
            TownAreaMembershipLease lease, CharacterPositionState position) =>
            throw new NotSupportedException();
    }

    private sealed class Sender : IGameMessageSender
    {
        public NetworkConnectionId? Failing { get; init; }
        public Action<NetworkConnectionId>? AfterSend { get; init; }
        public List<NetworkConnectionId> Sent { get; } = [];
        public List<IGameNotiMessage> Notifications { get; } = [];
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId,
            IGameNotiMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(connectionId);
            Notifications.Add(message);
            AfterSend?.Invoke(connectionId);
            return connectionId == Failing
                ? ValueTask.FromException(new IOException("send failed"))
                : ValueTask.CompletedTask;
        }
        public ValueTask SendCmdAsync(NetworkConnectionId connectionId,
            IGameCmdMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Closer : INetworkConnectionCloser
    {
        public List<NetworkConnectionId> Closed { get; } = [];
        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken ct) => RequestCloseAsync(expected.ConnectionId, ct);
        public ValueTask RequestCloseAsync(NetworkConnectionId connectionId, CancellationToken cancellationToken)
        {
            Closed.Add(connectionId);
            return ValueTask.CompletedTask;
        }
    }
}
