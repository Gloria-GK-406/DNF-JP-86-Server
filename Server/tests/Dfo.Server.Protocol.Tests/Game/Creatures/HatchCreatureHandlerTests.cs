using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Creatures;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Creatures;

public sealed class HatchCreatureHandlerTests
{
    private static readonly SelectedSessionIdentity Owner = SelectedSessionIdentity.Create(
        new AccountId(4), new CharacterId(2), new NetworkConnectionId("hatch"), 1, 10011);

    [Fact]
    public async Task Captured_request_dispatches_then_delivers_ack_same_slot_and_same_uid_detail()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var before = new CreatureState(959, 63022, 100, 0, 1, [], 0, 0, 0, 0);
            var after = before with { ItemTemplateId = 63023 };
            var core = ItemSnapshot.FromCore(StackableItemCore.Create(5, 63023, 959), false, new ItemIdentity(959),
                creature: new InventoryCreatureDetailState(100, 0, 0, 1, 0, 0, [], 0));
            var bus = new Bus(new(before, after, new(BusinessInventorySpace.CreatureInventory, 0), core));
            var sender = new Sender(bus);
            var handler = Create(bus, sender);
            await handler.HandleAsync(Context(), CreaturePayloadCodec.DecodeHatchRequest([7, 0, 0]), default);

            Assert.Equal(1, bus.Calls);
            Assert.Equal(new BusinessSlot(BusinessInventorySpace.CreatureInventory, 0), bus.EggSlot);
            Assert.Equal(Owner, bus.Session);
            Assert.Collection(sender.Messages,
                message => Assert.True(Assert.IsType<HatchCreatureResponse>(message).Success),
                message =>
                {
                    var body = Assert.IsType<InventoryDeltaMessage>(message).EncodeBody();
                    Assert.Equal((byte)7, body[0]);
                    Assert.Equal(63023, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(5)));
                    Assert.Equal(959, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(9)));
                },
                message =>
                {
                    var pet = Assert.Single(Assert.IsType<CreatureListMessage>(message).Pets);
                    Assert.Equal(959, pet.PetKey);
                });
        });
    }

    [Theory]
    [InlineData(3, 25)]
    [InlineData(7, -1)]
    [InlineData(7, 140)]
    [InlineData(7, 189)]
    public async Task Wrong_list_or_non_creature_slot_is_refused_without_dispatch(byte list, short slot)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var bus = new Bus(null); var sender = new Sender(bus);
            await Create(bus, sender).HandleAsync(Context(), new HatchCreatureRequest(list, slot), default);
            Assert.Equal(0, bus.Calls);
            Assert.False(Assert.IsType<HatchCreatureResponse>(Assert.Single(sender.Messages)).Success);
        });
    }

    [Fact]
    public async Task Domain_refusal_delivers_only_failure_without_inventory_refresh()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var bus = new Bus(null); var sender = new Sender(bus);
            await Create(bus, sender).HandleAsync(Context(), new HatchCreatureRequest(7, 0), default);
            Assert.Equal(1, bus.Calls);
            Assert.False(Assert.IsType<HatchCreatureResponse>(Assert.Single(sender.Messages)).Success);
        });
    }

    [Fact]
    public async Task Commit_failure_never_sends_success_or_inventory_projection()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var bus = new Bus(null) { Fail = true }; var sender = new Sender(bus);
            await Assert.ThrowsAsync<IOException>(() => Create(bus, sender)
                .HandleAsync(Context(), new HatchCreatureRequest(7, 0), default).AsTask());
            Assert.Empty(sender.Messages);
        });
    }

    private static HatchCreatureHandler Create(Bus bus, Sender sender) => new(new WholeDriver(new(new Sessions(), bus,
            new Units(), new Outbox(sender), TimeProvider.System)));
    private static NetworkDispatchContext Context() => new(
        new NetworkConnectionContext(Owner.ConnectionId, NetworkEndpointKind.Game, 10011));

    private sealed class Bus(CreatureTransformationReceipt? receipt) : ICreatureRuntimeDomainService
    {
        public bool Fail { get; init; }
        public int Calls { get; private set; }
        public BusinessSlot EggSlot { get; private set; }
        public SelectedSessionIdentity? Session { get; private set; }
        public ValueTask<CreatureTransformationReceipt?> HatchAsync(SelectedSessionIdentity session, BusinessSlot egg, CancellationToken ct)
        {
            Calls++; Session = session; EggSlot = egg;
            if (Fail) throw new IOException("commit failed");
            return ValueTask.FromResult(receipt);
        }
        public void Enter(SelectedSessionIdentity s) => throw new NotSupportedException();
        public void DiscardExact(SelectedSessionIdentity s) => throw new NotSupportedException();
        public ValueTask LeaveAsync(SelectedSessionIdentity s, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<CreatureRuntimeChange?> GainRoomExperienceAsync(SelectedSessionIdentity s, DungeonRunId r, MazeCell c, int n, CancellationToken ct) => throw new NotSupportedException();
        public void Invalidate(CharacterId id) => throw new NotSupportedException();
        public void ChangeCombat(CharacterId id, bool combat) => throw new NotSupportedException();
        public bool IsEquipped(SelectedSessionIdentity s, int uid) => throw new NotSupportedException();
        public IReadOnlyList<SelectedSessionIdentity> DueSessions() => throw new NotSupportedException();
        public IReadOnlyList<CreaturePendingProjection> DrainProjections() => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<CreatureRuntimeChange>> AdvanceAsync(SelectedSessionIdentity s, CancellationToken ct, bool? combat = null) => throw new NotSupportedException();
        public ValueTask<CreatureRuntimeChange?> GainExperienceAsync(SelectedSessionIdentity s, int n, CancellationToken ct) => throw new NotSupportedException();
        public void QueueRoomExperience(SelectedSessionIdentity s, DungeonRunId r, MazeCell c, int n) => throw new NotSupportedException();
    }
    private sealed class WholeDriver(GameHatchCreatureCommandHandler handler) : ICommandBus
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public async ValueTask<T> SendAsync<T>(ICommand<T> command, CancellationToken ct = default) where T : notnull =>
            (T)(object)await handler.HandleAsync(new(Guid.NewGuid(), typeof(GameHatchCreatureCommand), typeof(GameHatchCreatureCommandHandler)), Assert.IsType<GameHatchCreatureCommand>(command), ct);
    }
    // Local projection test driver; the Application tests exercise production work units and SQLite commits.
    private sealed class Units : ICommandWorkUnits
    {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull
        { Assert.Equal(CommandWorkUnitKind.Ordinary, d.Kind); Assert.Empty(d.Participants); return action(null!, ct); }
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
    }
    private sealed class Outbox(Sender sender) : IGameCommandOutbox
    {
        public ValueTask RegisterCmdAsync(NetworkConnectionId id, IGameCmdMessage m, CancellationToken ct) => sender.SendCmdAsync(id, m, ct);
        public ValueTask RegisterNotiAsync(NetworkConnectionId id, IGameNotiMessage m, CancellationToken ct) => sender.SendNotiAsync(id, m, ct);
        public ValueTask RegisterBatchAsync(NetworkConnectionContext c, IReadOnlyList<IGameServerMessage> m, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery d, CancellationToken ct) => sender.SendExplicitAsync(id, d, ct);
    }

    private sealed class Sessions : ISelectedSessionDomainService
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
        public SelectedSessionIdentity? Find(AccountId accountId) => Owner;
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => Owner;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Owner;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => Owner;
    }

    /// <summary>
    /// What a real registration adapter registered, read from its outbox; every call goes to the real protocol sender
    /// over it, and an explicit delivery is checked at the registration point (TASK-25). An explicit delivery counts
    /// with its direct messages, as before.
    /// </summary>
    private sealed class Sender(Bus bus) : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();
        public List<IGameServerMessage> Messages => [.. outbound.CallMessages.Cast<IGameServerMessage>()];
        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message,
            CancellationToken cancellationToken) => outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken cancellationToken) => outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery, CancellationToken cancellationToken)
        {
            Assert.Equal(1, bus.Calls);
            Assert.Empty(delivery.BestEffortTargets);
            return outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
        }
    }
}
