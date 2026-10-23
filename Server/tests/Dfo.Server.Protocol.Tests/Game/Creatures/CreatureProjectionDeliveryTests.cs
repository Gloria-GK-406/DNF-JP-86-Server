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
    /// <summary>The real registration under the delivery; read back through the old doubles' members (TASK-25).</summary>
    private readonly RecordedOutbound recorded = new();

    [Fact]
    public async Task Unequipped_evolution_updates_the_pet_key_without_overwriting_the_worn_slot()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var owner = Session(1, "owner");
            var sender = new Sender(recorded);
            var delivery = Create(new Sessions(owner), new Towns(Member(owner, Area, true)), sender, new Closer(recorded));
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
            var sender = new Sender(recorded);
            var delivery = Create(sessions, towns, sender, new Closer(recorded));

            var success = await delivery.SendVisibleAsync(owner,
                new CreatureRenameNotification(owner.WireUserId, [0x41]), default);

            Assert.True(success);
            Assert.Equal(expected, sender.Sent.Select(x => x.Value));
        });
    }

    /// <summary>
    /// DR-9, the kept per-recipient branch (TASK-24 R9): a peer's notice cannot be registered (its encoding fails at
    /// the registration point); he alone is closed and the later peer is still told.
    /// </summary>
    [Fact]
    public async Task Peer_send_failure_closes_peer_and_continues_without_closing_owner()
    {
        var owner = Session(1, "owner");
        var failed = Session(2, "failed");
        var next = Session(3, "next");
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        outbound.Connect(owner, failed, next);
        outbound.Encoder.Fail<CreatureRenameNotification>(failed.ConnectionId, () => new IOException("send failed"));

        var success = await outbound.RunAsync(async () =>
        {
            var delivery = Create(new Sessions(owner, failed, next),
                new Towns(Member(owner, Area, true), Member(failed, Area, true),
                    Member(next, Area, true)), outbound.Game, outbound.Closer);
            return await delivery.SendVisibleAsync(owner,
                new CreatureRenameNotification(owner.WireUserId, [0x41]), default);
        });
        await outbound.SettledAsync();

        Assert.True(success);
        Assert.Equal(["owner", "next"], outbound.Recorder.Entries.Where(entry => !entry.IsClose)
            .Select(entry => entry.Connection.ConnectionId.Value));
        Assert.Equal(["failed"], outbound.Recorder.Entries.Where(entry => entry.IsClose)
            .Select(entry => entry.Connection.ConnectionId.Value));
    }

    /// <summary>
    /// DR-9 (TASK-24 T12): the rename notice and its acknowledgement are registered in that order on the owner's
    /// connection; when the owner's delivery fails in transport the connection is closed and the acknowledgement,
    /// queued behind the notice, is never delivered.
    /// </summary>
    [Fact]
    public async Task An_owner_whose_rename_notice_fails_in_transport_never_gets_the_acknowledgement_first()
    {
        var owner = Session(1, "owner");
        await using var outbound = new Dfo.Server.Protocol.Tests.Outbox.XoDelivery();
        outbound.Connect(owner);
        outbound.Transport.Fail(owner.ConnectionId);
        var creature = new CreatureState(500, 63000, 90, 2150, 19, [], 0, 0, 0, 0);

        await outbound.RunFromAsync(owner.ConnectionId, async () =>
        {
            var sessions = new Sessions(owner);
            var delivery = Create(sessions, new Towns(Member(owner, Area, true)), outbound.Game, outbound.Closer);
            var handler = new RenameCreatureHandler(sessions, new Dfo.Server.Abstractions.Inventory.InventorySlotMappingRuleService(),
                new RenameCommands(new CreatureRenameReceipt(creature, default, null)), outbound.Game, delivery, TimeProvider.System);
            await handler.HandleAsync(new NetworkDispatchContext(outbound.Context(owner.ConnectionId)),
                new RenameCreatureRequest(189, 7, [0x41]), default);
        });
        await outbound.SettledAsync();

        var entries = outbound.Recorder.Entries.ToList();
        var notice = entries.FindIndex(entry => entry.Messages.Any(message => message is CreatureRenameNotification));
        var acknowledgement = entries.FindIndex(entry => entry.Messages.Any(message => message is RenameCreatureResponse));
        Assert.True(notice >= 0 && notice < acknowledgement, "the acknowledgement is registered behind the notice");
        Assert.Equal(Dfo.Server.Abstractions.Execution.OutboundOutcome.Failed, outbound.Observer.OutcomeOf(entries[notice]));
        Assert.NotEqual(Dfo.Server.Abstractions.Execution.OutboundOutcome.Delivered, outbound.Observer.OutcomeOf(entries[acknowledgement]));
        Assert.True(await outbound.ClosedAsync(owner.ConnectionId));
        Assert.Empty(outbound.Transport.Delivered);
    }

    private sealed class RenameCommands(CreatureRenameReceipt receipt) : Dfo.Server.Application.Commands.ICommandBus
    {
        public ValueTask<TResult> SendAsync<TResult>(Dfo.Server.Application.Commands.ICommand<TResult> command,
            CancellationToken cancellationToken = default) where TResult : notnull =>
            command is RenameCreatureCommand
                ? ValueTask.FromResult((TResult)(object)new RenameCreatureResult(receipt))
                : throw new NotSupportedException(command.GetType().Name);
    }

    [Fact]
    public async Task Owner_send_failure_closes_owner_and_reports_failure()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var owner = Session(1, "owner");
            var sender = new Sender(recorded) { Failing = owner.ConnectionId };
            var closer = new Closer(recorded);
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
            var sender = new Sender(recorded);
            var delivery = Create(sessions,
                new Towns(Member(owner, Area, true), Member(townPeer, Area, true)),
                sender, new Closer(recorded), tower);

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
            var sender = new Sender(recorded)
            {
                AfterSend = connection =>
                {
                    if (connection == owner.ConnectionId) identities[1] = replacement;
                },
            };
            var delivery = Create(sessions, new Towns(), sender, new Closer(recorded),
                new TowerDomainStub(owner, peer));

            var success = await delivery.SendVisibleAsync(owner,
                new CreatureRenameNotification(owner.WireUserId, [0x41]), default);

            Assert.True(success);
            Assert.Equal(["owner"], sender.Sent.Select(value => value.Value));
        });
    }

    private static CreatureProjectionDelivery Create(Sessions sessions, Towns towns, IGameMessageSender sender,
        INetworkConnectionCloser closer, TowerDomainStub? tower = null) => new(sessions, DispatchProxy.Create<IDungeonRunDomainService, NullProxy>(),
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

    /// <summary>
    /// What the delivery registered, read from the outbox; every call goes to the real protocol sender over the
    /// registration adapter (TASK-25). <see cref="Failing"/> makes every message for that connection fail to encode
    /// where it is registered, with the exception the old double threw; an explicit delivery fails the same way, as
    /// the old double refused one, so a stale inventory projection still fails a test that expects none.
    /// </summary>
    private sealed class Sender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound;
        private readonly NetworkConnectionId? failing;

        public Sender(RecordedOutbound outbound)
        {
            this.outbound = outbound;
            outbound.FailAny(_ => outbound.ExplicitInFlight is not null,
                () => new NotSupportedException("This delivery registers no explicit inventory projection here."));
        }

        public NetworkConnectionId? Failing
        {
            get => failing;
            init
            {
                failing = value;
                if (value is { } connection)
                    outbound.Fail(connection, _ => true, () => new IOException("send failed"));
            }
        }

        public Action<NetworkConnectionId>? AfterSend { get; init; }
        public List<NetworkConnectionId> Sent =>
            [.. outbound.Entries.Where(entry => !entry.IsClose).SelectMany(entry => entry.Messages.Select(_ => entry.Connection.ConnectionId))];
        public List<IGameNotiMessage> Notifications =>
            [.. outbound.Entries.Where(entry => !entry.IsClose).SelectMany(entry => entry.Messages).OfType<IGameNotiMessage>()];
        public async ValueTask SendNotiAsync(NetworkConnectionId connectionId,
            IGameNotiMessage message, CancellationToken cancellationToken)
        {
            await outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
            AfterSend?.Invoke(connectionId);
        }
        public ValueTask SendCmdAsync(NetworkConnectionId connectionId,
            IGameCmdMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
            outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
    }

    /// <summary>The closes the delivery registered, read from the outbox; the real closer registers them.</summary>
    private sealed class Closer(RecordedOutbound outbound) : INetworkConnectionCloser
    {
        public List<NetworkConnectionId> Closed => [.. outbound.Closes];
        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken ct) =>
            outbound.Closer.RequestCloseExactAsync(expected, ct);
        public ValueTask RequestCloseAsync(NetworkConnectionId connectionId, CancellationToken cancellationToken) =>
            outbound.Closer.RequestCloseAsync(connectionId, cancellationToken);
    }
}
