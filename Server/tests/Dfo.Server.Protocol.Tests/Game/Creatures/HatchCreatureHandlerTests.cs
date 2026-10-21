using Dfo.Server.Abstractions.Creatures;
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

namespace Dfo.Server.Protocol.Tests.Game.Creatures;

public sealed class HatchCreatureHandlerTests
{
    private static readonly SelectedSessionIdentity Owner = SelectedSessionIdentity.Create(
        new AccountId(4), new CharacterId(2), new NetworkConnectionId("hatch"), 1, 10011);

    [Fact]
    public async Task Captured_request_dispatches_then_delivers_ack_same_slot_and_same_uid_detail()
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
        Assert.Equal(new BusinessSlot(BusinessInventorySpace.CreatureInventory, 0), bus.Command!.EggSlot);
        Assert.Equal(Owner, bus.Command.Session);
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
    }

    [Theory]
    [InlineData(3, 25)]
    [InlineData(7, -1)]
    [InlineData(7, 140)]
    [InlineData(7, 189)]
    public async Task Wrong_list_or_non_creature_slot_is_refused_without_dispatch(byte list, short slot)
    {
        var bus = new Bus(null); var sender = new Sender(bus);
        await Create(bus, sender).HandleAsync(Context(), new HatchCreatureRequest(list, slot), default);
        Assert.Equal(0, bus.Calls);
        Assert.False(Assert.IsType<HatchCreatureResponse>(Assert.Single(sender.Messages)).Success);
    }

    [Fact]
    public async Task Domain_refusal_delivers_only_failure_without_inventory_refresh()
    {
        var bus = new Bus(null); var sender = new Sender(bus);
        await Create(bus, sender).HandleAsync(Context(), new HatchCreatureRequest(7, 0), default);
        Assert.Equal(1, bus.Calls);
        Assert.False(Assert.IsType<HatchCreatureResponse>(Assert.Single(sender.Messages)).Success);
    }

    [Fact]
    public async Task Commit_failure_never_sends_success_or_inventory_projection()
    {
        var bus = new Bus(null) { Fail = true }; var sender = new Sender(bus);
        await Assert.ThrowsAsync<IOException>(() => Create(bus, sender)
            .HandleAsync(Context(), new HatchCreatureRequest(7, 0), default).AsTask());
        Assert.Empty(sender.Messages);
    }

    private static HatchCreatureHandler Create(Bus bus, Sender sender) => new(
        new Sessions(), new InventorySlotMappingRuleService(), bus, sender, TimeProvider.System);
    private static NetworkDispatchContext Context() => new(
        new NetworkConnectionContext(Owner.ConnectionId, NetworkEndpointKind.Game, 10011));

    private sealed class Bus(CreatureTransformationReceipt? receipt) : ICommandBus
    {
        public bool Fail { get; init; }
        public int Calls { get; private set; }
        public HatchCreatureCommand? Command { get; private set; }
        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command,
            CancellationToken cancellationToken = default) where TResult : notnull
        {
            Calls++; Command = Assert.IsType<HatchCreatureCommand>(command);
            if (Fail) throw new IOException("commit failed");
            return ValueTask.FromResult((TResult)(object)new HatchCreatureResult(receipt));
        }
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

    private sealed class Sender(Bus bus) : IGameMessageSender
    {
        public List<IGameServerMessage> Messages { get; } = [];
        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message,
            CancellationToken cancellationToken) { Messages.Add(message); return ValueTask.CompletedTask; }
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken cancellationToken) { Messages.Add(message); return ValueTask.CompletedTask; }
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery, CancellationToken cancellationToken)
        {
            Assert.Equal(1, bus.Calls);
            Assert.Empty(delivery.BestEffortTargets);
            Messages.AddRange(delivery.RequiredDirectMessages);
            return ValueTask.CompletedTask;
        }
    }
}
