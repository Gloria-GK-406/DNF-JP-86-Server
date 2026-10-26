using System.Diagnostics.CodeAnalysis;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Abstractions.DomainEvents;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game.AccountRoster;
using Microsoft.Extensions.Logging.Abstractions;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.AccountRoster;

public sealed class OtherCharacterDetailFlowTests
{
    [Fact]
    public async Task ProtocolHandlerDispatchesTheQueryCommandAndPublishesItsExactPlan()
    {
        await using var world = new World();
        await world.DispatchAsync();
        var query = Assert.Single(world.Query.Calls);
        Assert.Equal(world.Requester, query.Requester);
        Assert.Equal(world.Target.WireUserId, query.Wire);
        var publication = Assert.Single(world.Projections.Publications);
        Assert.Equal([world.Requester.CharacterId], Assert.IsType<NotificationAudience.TheseCharacters>(publication.Audience).CharacterIds);
        Assert.Same(world.Projection, Assert.IsType<OtherCharacterDetailRequested>(publication.Notification).Projection);
        var entry = Assert.Single(world.Output.Entries);
        Assert.Equal(world.Requester.ConnectionId, entry.Connection.ConnectionId);
        Assert.IsType<OtherCharacterDetailResponse>(Assert.Single(entry.Messages));
        Assert.Equal((byte)0, entry.Frames.Span[0]);
        Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(entry.Frames.Span.Slice(1)));
        Assert.Equal((uint)1, BinaryPrimitives.ReadUInt32LittleEndian(entry.Frames.Span.Slice(7)));
        Assert.Equal((byte)3, entry.Frames.Span[GameServerFrameEncoder.HeaderLength]);
        Assert.True(world.Query.OnExecutor); Assert.False(world.Query.InTransaction);
        Assert.True(world.Projections.OnExecutor); Assert.False(world.Projections.InTransaction);
    }

    [Fact]
    public async Task RejectedQueryDoesNotPublishACharacterDetailNotification()
    {
        await using var world = new World();
        world.Query.Result = CharacterDetailQueryResult.Reject(CharacterDetailQueryFailure.TargetRuntimeUnavailable);
        await world.DispatchAsync();
        Assert.Single(world.Query.Calls);
        Assert.Empty(world.Projections.Publications);
        Assert.Empty(world.Output.Entries);
    }

    [Fact]
    public async Task Thin_handler_delivers_the_original_request_dispatch_and_metadata_once()
    {
        var commands = new DetailCommandBus();
        var requester = Identity(101, 50000, "thin", 1, 10011);
        var dispatch = Dispatch(requester);
        var request = new GetOtherCharacterDetailRequest(GameMessageMetadata.Empty, 123);
        await new GetOtherCharacterDetailHandler(commands).HandleAsync(dispatch, request, default);
        var command = Assert.IsType<GameOtherCharacterDetailCommand>(Assert.Single(commands.Commands));
        Assert.Same(dispatch, command.Dispatch); Assert.Same(request, command.Request); Assert.Same(request, command.Message);
        Assert.Same(request.Metadata, Assert.IsType<ProtocolFrameMetadata.Game>(command.Frame).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Missing_or_displaced_preflight_sessions_never_query_or_project(int mode)
    {
        await using var world = new World();
        if (mode == 0) world.Sessions.ReleaseExact(world.Requester);
        if (mode == 1) world.Sessions.ReleaseExact(world.Target);
        if (mode == 2) world.Sessions.AfterTargetLookup = _ =>
            world.Sessions.Select(TargetFor(world.Projection.TargetState, "replacement", 2, 10011));
        await world.DispatchAsync();
        Assert.Empty(world.Query.Calls);
        Assert.Empty(world.Projections.Publications);
        Assert.Empty(world.Output.Entries);
        if (mode == 2) Assert.Equal(1, world.Sessions.TargetLookups);
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

    private sealed class DetailCommandBus : ICommandBus
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public List<ICommand> Commands { get; } = [];
        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        { Commands.Add(command); return ValueTask.FromResult((TResult)(object)default(GameOtherCharacterDetailCompleted)); }
    }

    private sealed record Publication(NotificationAudience Audience, object Notification);
    private sealed class World : IAsyncDisposable
    {
        public ServiceProvider Services { get; }
        public SelectedSessionIdentity Requester { get; }
        public SelectedSessionIdentity Target { get; }
        public OtherCharacterDetailProjection Projection { get; }
        public SessionDirectory Sessions { get; }
        public QuerySpy Query { get; }
        public ProjectionSpy Projections { get; private set; } = null!;
        public Output Output { get; } = new();
        public World()
        {
            var state = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
            Target = TargetFor(state, "target", 1, 10011);
            Requester = Identity(101, 50000, "requester", 1, 10011);
            Projection = new(Requester, Target, state, DateTimeOffset.UnixEpoch);
            Sessions = new(Requester, Target);
            var services = new ServiceCollection();
            services.AddNetworkMessageDispatching();
            services.AddNotificationDispatching();
            services.AddEventDispatching();
            services.AddSingleton<ISelectedSessionDomainService>(Sessions);
            Query = new(CharacterDetailQueryResult.Success(Projection));
            services.AddSingleton<IOtherCharacterDetailQueryDomainService>(Query);
            services.AddSingleton<INetworkConnectionContexts>(new Connections(Dispatch(Requester).Connection, Dispatch(Target).Connection));
            services.AddSingleton<IOutboundMessageEncoder, Encoder>();
            services.AddSingleton<IOutboundSender>(Output);
            services.AddNotificationHandler<OtherCharacterDetailRequested, OtherCharacterDetailRequestedHandler>();
            services.AddProtocolCommandHandler<GameOtherCharacterDetailCommand, GameOtherCharacterDetailCompleted, GameOtherCharacterDetailCommandHandler>(
                new ProtocolCommandMessageBinding(ClientMessageType.GameGetOtherCharacterDetail, typeof(GetOtherCharacterDetailRequest)));
            services.RegisterProtocolMessageHandler<GetOtherCharacterDetailHandler, GetOtherCharacterDetailRequest, GameOtherCharacterDetailCommand>(ClientMessageType.GameGetOtherCharacterDetail);
            var projectionDescriptor = services.Single(row => row.ServiceType == typeof(ICommandProjections));
            services.Remove(projectionDescriptor);
            services.AddSingleton<ICommandProjections>(provider => Projections = new(
                (ICommandProjections)projectionDescriptor.ImplementationFactory!(provider), provider.GetRequiredService<IDomainTransactionAccessor>()));
            Services = services.BuildServiceProvider();
            Query.Transactions = Services.GetRequiredService<IDomainTransactionAccessor>();
            Projections = (ProjectionSpy)Services.GetRequiredService<ICommandProjections>();
        }
        public Task DispatchAsync() => Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(
            Dispatch(Requester), new GetOtherCharacterDetailRequest(GameMessageMetadata.Empty, Target.WireUserId), default).AsTask();
        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }
    private sealed class QuerySpy(CharacterDetailQueryResult result) : IOtherCharacterDetailQueryDomainService
    {
        public CharacterDetailQueryResult Result { get; set; } = result;
        public IDomainTransactionAccessor Transactions { get; set; } = null!;
        public List<(SelectedSessionIdentity Requester, ushort Wire)> Calls { get; } = [];
        public bool OnExecutor, InTransaction;
        public ValueTask<CharacterDetailQueryResult> QueryAsync(SelectedSessionIdentity requester, ushort targetWireUserId, CancellationToken ct)
        { Calls.Add((requester, targetWireUserId)); OnExecutor = GameplayJob.IsOnExecutor; InTransaction = Transactions.Current is not null; return ValueTask.FromResult(Result); }
    }
    private sealed class ProjectionSpy(ICommandProjections inner, IDomainTransactionAccessor transactions) : ICommandProjections
    {
        public List<Publication> Publications { get; } = [];
        public bool OnExecutor, InTransaction;
        public ValueTask<NotificationDelivery> ProjectAsync<TNotification>(NotificationAudience audience, TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : class, INotification<TNotification>
        {
            Publications.Add(new(audience, notification)); OnExecutor = GameplayJob.IsOnExecutor; InTransaction = transactions.Current is not null;
            return inner.ProjectAsync(audience, notification, cancellationToken);
        }
    }
    private sealed class Connections(params NetworkConnectionContext[] contexts) : INetworkConnectionContexts
    {
        public bool TryGet(NetworkConnectionId id, [NotNullWhen(true)] out NetworkConnectionContext? connection)
        { connection = contexts.SingleOrDefault(value => value.ConnectionId == id); return connection is not null; }
    }
    private sealed class Encoder : IOutboundMessageEncoder
    {
        public byte[] EncodeMessage(NetworkConnectionContext connection, IServerNetworkMessage message) =>
            new OtherCharacterDetailResponseEncoder().Encode((OtherCharacterDetailResponse)message).SelectMany(frame => frame).ToArray();
        public byte[] EncodeBatch(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages) =>
            messages.SelectMany(message => EncodeMessage(connection, message)).ToArray();
    }
    private sealed class Output : IOutboundSender
    {
        public List<OutboundEntry> Entries { get; } = [];
        public void Submit(IReadOnlyList<OutboundEntry> entries) => Entries.AddRange(entries);
    }

    private sealed class SessionDirectory(params SelectedSessionIdentity[] initial)
        : ISelectedSessionDomainService
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
            if (session is not null) { TargetLookups++; AfterTargetLookup?.Invoke(session); }
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

        public Action<SelectedSessionIdentity>? AfterTargetLookup;
        public int TargetLookups;

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
