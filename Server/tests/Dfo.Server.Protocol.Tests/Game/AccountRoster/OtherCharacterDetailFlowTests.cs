using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game.AccountRoster;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.AccountRoster;

public sealed class OtherCharacterDetailFlowTests
{
    [Fact]
    public async Task ProtocolHandlerDispatchesTheQueryCommandAndPublishesItsExactPlan()
    {
        var state = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
        var target = TargetFor(state, "target", 1, 10011);
        var requester = Identity(101, 50000, "requester", 1, 10011);
        var sessions = new SessionDirectory(requester, target);
        var projection = new OtherCharacterDetailProjection(
            requester, target, state, DateTimeOffset.UnixEpoch);
        var commands = new DetailCommandBus(CharacterDetailQueryResult.Success(projection));
        var notifications = new RecordingNotificationBus();

        await new GetOtherCharacterDetailHandler(
                sessions,
                new SelectedSessionTransitionGate(),
                commands,
                notifications,
                NullLogger<GetOtherCharacterDetailHandler>.Instance)
            .HandleAsync(
                Dispatch(requester),
                new GetOtherCharacterDetailRequest(
                    GameMessageMetadata.Empty, target.WireUserId),
                CancellationToken.None);

        var command = Assert.IsType<QueryOtherCharacterDetailCommand>(
            Assert.Single(commands.Commands));
        Assert.Equal(requester, command.Requester);
        Assert.Equal(target.WireUserId, command.TargetWireUserId);
        var publication = Assert.Single(notifications.Publications);
        Assert.Equal(
            [requester.CharacterId],
            Assert.IsType<NotificationAudience.TheseCharacters>(
                publication.Audience).CharacterIds);
        Assert.Equal(
            projection,
            Assert.IsType<OtherCharacterDetailRequested>(publication.Notification).Projection);
    }

    [Fact]
    public async Task RejectedQueryDoesNotPublishACharacterDetailNotification()
    {
        var state = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
        var target = TargetFor(state, "target", 1, 10011);
        var requester = Identity(101, 50000, "requester", 1, 10011);
        var notifications = new RecordingNotificationBus();

        await new GetOtherCharacterDetailHandler(
                new SessionDirectory(requester, target),
                new SelectedSessionTransitionGate(),
                new DetailCommandBus(CharacterDetailQueryResult.Reject(
                    CharacterDetailQueryFailure.TargetRuntimeUnavailable)),
                notifications,
                NullLogger<GetOtherCharacterDetailHandler>.Instance)
            .HandleAsync(
                Dispatch(requester),
                new GetOtherCharacterDetailRequest(
                    GameMessageMetadata.Empty, target.WireUserId),
                CancellationToken.None);

        Assert.Empty(notifications.Publications);
    }

    [Fact]
    public async Task NotificationRevalidatesBothGenerationsBeforeComposingTheResponse()
    {
        var state = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
        var target = TargetFor(state, "target-old", 1, 10011);
        var requester = Identity(101, 50000, "requester", 1, 10011);
        var sessions = new SessionDirectory(requester, target);
        var notification = new OtherCharacterDetailRequested(
            new OtherCharacterDetailProjection(
                requester, target, state, DateTimeOffset.UnixEpoch));
        var handler = new OtherCharacterDetailRequestedHandler(sessions);
        var recipient = new NotificationRecipient(
            requester.CharacterId, requester.ConnectionId);

        var current = await handler.ComposeAsync(
            recipient, notification, CancellationToken.None);

        Assert.IsType<OtherCharacterDetailResponse>(Assert.Single(current));

        sessions.Select(TargetFor(state, "target-new", 2, 10011));
        Assert.Empty(await handler.ComposeAsync(
            recipient, notification, CancellationToken.None));

        sessions.Select(Identity(101, 50000, "requester-new", 2, 10011));
        Assert.Empty(await handler.ComposeAsync(
            recipient, notification, CancellationToken.None));
    }

    [Fact]
    public void ResponseIsNotiTwoSubtypeThreeWithTheQueryAnswerHeaderByte()
    {
        var state = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();

        var frame = Assert.Single(new OtherCharacterDetailResponseEncoder().Encode(
            new OtherCharacterDetailResponse(state, DateTimeOffset.UnixEpoch)));

        Assert.Equal((byte)0x00, frame[0]);
        Assert.Equal((ushort)0x0002, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal((uint)1, BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(7)));
        Assert.Equal((byte)3, frame[GameServerFrameEncoder.HeaderLength]);
    }

    private static NetworkDispatchContext Dispatch(SelectedSessionIdentity session) =>
        new(new NetworkConnectionContext(
            session.ConnectionId, NetworkEndpointKind.Game, session.ListenerPort));

    private static SelectedSessionIdentity TargetFor(
        SelectCharacterResult state,
        string connection,
        long generation,
        int listenerPort) => Identity(
            202,
            state.Character.Identity.CharacterId.Value,
            connection,
            generation,
            listenerPort);

    private static SelectedSessionIdentity Identity(
        int account,
        int character,
        string connection,
        long generation,
        int listenerPort) => SelectedSessionIdentity.Create(
            new AccountId(account),
            new CharacterId(character),
            new NetworkConnectionId(connection),
            generation,
            listenerPort);

    private sealed class DetailCommandBus(CharacterDetailQueryResult result) : ICommandBus
    {
        public List<ICommand> Commands { get; } = [];

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            Commands.Add(command);
            return ValueTask.FromResult((TResult)(object)result);
        }
    }

    private sealed record Publication(NotificationAudience Audience, object Notification);

    private sealed class RecordingNotificationBus : INotificationBus
    {
        public List<Publication> Publications { get; } = [];

        public ValueTask<NotificationDelivery> PublishAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken)
            where TNotification : class, INotification<TNotification>
        {
            Publications.Add(new Publication(audience, notification));
            return ValueTask.FromResult(new NotificationDelivery([]));
        }
    }

    private sealed class SessionDirectory(params SelectedSessionIdentity[] initial)
        : ISelectedSessionDomainService
    {
        private readonly Dictionary<AccountId, SelectedSessionIdentity> sessions =
            initial.ToDictionary(value => value.AccountId);

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session)
        {
            sessions.TryGetValue(session.AccountId, out var displaced);
            sessions[session.AccountId] = session;
            return displaced;
        }

        public bool ReleaseExact(SelectedSessionIdentity session) =>
            sessions.TryGetValue(session.AccountId, out var current)
            && current == session
            && sessions.Remove(session.AccountId);

        public SelectedSessionIdentity? Find(AccountId accountId) =>
            sessions.GetValueOrDefault(accountId);

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
            sessions.Values.SingleOrDefault(value => value.CharacterId == characterId);

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
            sessions.Values.SingleOrDefault(value => value.WireUserId == wireUserId);

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
            sessions.Values.SingleOrDefault(value => value.ConnectionId == connectionId);
    }
}
