using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.TitleBook;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

public sealed class TitleBookMutationHandlerTests
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    public async Task Worn_requests_reply_with_the_native_equipment_coordinates_and_complete_ack_before_projection(bool store, bool success, bool replaceMirror)
    {
        var connection = new NetworkConnectionContext(new("title-ack"), NetworkEndpointKind.Game, 10011);
        var session = SelectedSessionIdentity.Create(new(1), new(1), connection.ConnectionId, 1, 10011);
        var gate = new AchievementTitleProjectionGate();
        var commands = new Commands(new(success, success ? null : "InvalidItem", 4, 10,
            new(BusinessInventorySpace.WornEquipment, 1)));
        var sender = new Sender();
        var handler = new TitleBookMutationHandler(new Sessions(session),
            new Users(new(OnlineUserId.New(), connection, session.AccountId, session.CharacterId,
                OnlineUserState.CharacterActive, 1)), new InventorySlotMappingRuleService(), commands, sender,
            gate, NullLogger<TitleBookMutationHandler>.Instance);
        // The request is one CMD: its ACK is sent inside it, and no projection can run until it ends.
        sender.Complete.TrySetResult();
        await TestGameplay.RunAsync(async () => await handler.HandleAsync(new(connection), new TitleBookMutationRequest(new(1, store ? (ushort)0x019C : (ushort)0x019D, 0, 1),
            store, replaceMirror ? 0x314D4254 : 3, replaceMirror ? 9 : 13, 26596, 4, 10, true), default));
        var response = Assert.IsType<TitleBookMutationResponse>(Assert.Single(sender.Sent));
        Assert.Equal(store, response.Store);
        Assert.Equal(3, response.Space); Assert.Equal(13, response.Slot);
        Assert.Equal(4, response.Category); Assert.Equal(10, response.Index);
        Assert.Equal(success ? null : (byte?)0x0A, response.Error);
        Assert.Equal(store, commands.Seen is StoreTitleCommand);
        if (replaceMirror) {
            var command = Assert.IsType<EquipCarriedTitleReplacingMirrorCommand>(commands.Seen);
            Assert.Equal(new BusinessSlot(BusinessInventorySpace.CarriedEquipment, 0), command.Source);
        }
        // The handler released the projection gate: a later job takes it without waiting.
        await TestGameplay.RunAsync(async () =>
        {
            using var held = await gate.EnterAsync(session.CharacterId, default);
        });
        Assert.Single(sender.Sent);
    }
    private sealed class Commands(TitleBookMutationResult result) : ICommandBus
    {
        public object? Seen { get; private set; }
        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default) where TResult : notnull
        { Seen = command; return ValueTask.FromResult((TResult)(object)result); }
    }
    private sealed class Sessions(SelectedSessionIdentity session) : ISelectedSessionDomainService
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

        public SelectedSessionIdentity? Select(SelectedSessionIdentity value) => throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity value) => throw new NotSupportedException();
        public SelectedSessionIdentity? Find(AccountId id) => id == session.AccountId ? session : null;
        public SelectedSessionIdentity? FindByCharacter(CharacterId id) => id == session.CharacterId ? session : null;
        public SelectedSessionIdentity? FindByWireUserId(ushort id) => id == session.WireUserId ? session : null;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId id) => id == session.ConnectionId ? session : null;
    }
    private sealed class Users(OnlineUserSnapshot user) : IOnlineUserQueryDomainService
    {
        public OnlineUserSnapshot GetRequired(OnlineUserId id) => user;
        public bool TryGetByConnection(NetworkConnectionId id, out OnlineUserSnapshot? found)
        { found = id == user.Connection.ConnectionId ? user : null; return found is not null; }
    }
    private sealed class Sender : IGameMessageSender
    {
        public List<IGameServerMessage> Sent { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask SendCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken ct)
        { Sent.Add(message); Started.TrySetResult(); await Complete.Task.WaitAsync(ct); }
        public ValueTask SendNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken ct) => throw new InvalidOperationException("No NOTI before native ACK.");
        public ValueTask SendExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken ct) => throw new InvalidOperationException("No state replacement before native ACK.");
    }
}
