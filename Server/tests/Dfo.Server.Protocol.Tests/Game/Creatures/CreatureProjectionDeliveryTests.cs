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
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.Creatures;

public sealed class CreatureProjectionDeliveryTests
{
    [Fact]
    public async Task Unequipped_evolution_updates_the_pet_key_without_overwriting_the_worn_slot()
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
    }
    private static readonly TownAreaKey Area = new(11, 3, 6);
    private static readonly TownAreaKey OtherArea = new(11, 3, 7);

    [Theory]
    [InlineData(true, "owner", "peer")]
    [InlineData(false, "owner")]
    public async Task Town_rename_reaches_only_visible_sessions_once(bool ownerShares,
        params string[] expected)
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
    }

    [Fact]
    public async Task Peer_send_failure_closes_peer_and_continues_without_closing_owner()
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
    }

    [Fact]
    public async Task Owner_send_failure_closes_owner_and_reports_failure()
    {
        var owner = Session(1, "owner");
        var sender = new Sender { Failing = owner.ConnectionId };
        var closer = new Closer();
        var delivery = Create(new Sessions(owner), new Towns(Member(owner, Area, true)), sender, closer);

        var success = await delivery.SendVisibleAsync(owner,
            new CreatureRenameNotification(owner.WireUserId, [0x41]), default);

        Assert.False(success);
        Assert.Equal(["owner"], closer.Closed.Select(x => x.Value));
    }

    private static CreatureProjectionDelivery Create(Sessions sessions, Towns towns, Sender sender,
        Closer closer) => new(sessions, DispatchProxy.Create<IDungeonRunDomainService, NullProxy>(),
            towns, sender, DispatchProxy.Create<ICreatureRuntimeDomainService, NullProxy>(), closer,
            NullLogger<CreatureProjectionDelivery>.Instance);

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
        public List<NetworkConnectionId> Sent { get; } = [];
        public List<IGameNotiMessage> Notifications { get; } = [];
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId,
            IGameNotiMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(connectionId);
            Notifications.Add(message);
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
