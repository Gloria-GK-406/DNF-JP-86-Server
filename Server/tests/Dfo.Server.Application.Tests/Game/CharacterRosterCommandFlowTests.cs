using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.AccountRoster;
using Dfo.Server.Protocol.Game.CharacterName;
using Dfo.Server.Protocol.Game.CreateCharacter;
using Dfo.Server.Protocol.Game.DeleteCharacter;
using Dfo.Server.Protocol.Game.HiddenCharacterInfo;
using Dfo.Server.Protocol.Game.HonorLevel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Game;

public sealed class CharacterRosterCommandFlowTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Thin_handlers_deliver_the_original_request_and_connection_once(int kind)
    {
        var bus = new CaptureBus();
        var dispatch = new NetworkDispatchContext(new(new("roster-thin"), NetworkEndpointKind.Game, 10011));
        var request = Request(kind);
        IClientMessageHandler handler = kind switch
        {
            0 => new GetAccountCharacterRosterHandler(bus),
            1 => new CheckCharacterNameHandler(bus),
            2 => new CreateCharacterHandler(bus),
            _ => new DeleteCharacterHandler(bus),
        };
        await handler.HandleAsync(dispatch, request, default);
        var command = Assert.IsAssignableFrom<IProtocolCommand>(Assert.Single(bus.Commands));
        Assert.Same(dispatch, command.Dispatch);
        Assert.Same(request, command.Message);
        var metadata = request switch
        {
            GetAccountCharacterRosterRequest value => value.Metadata,
            CheckCharacterNameRequest value => value.Metadata,
            CreateCharacterRequest value => value.Metadata,
            DeleteCharacterRequest value => value.Metadata,
            _ => throw new InvalidOperationException(),
        };
        Assert.Same(metadata, Assert.IsType<ProtocolFrameMetadata.Game>(command.Frame).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Unauthenticated_requests_do_no_business_or_message_registration(int kind)
    {
        await using var world = new World();
        world.Users.Authenticated = false;
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => world.DispatchAsync(kind));
        Assert.Equal($"Game connection '{world.Connection.ConnectionId}' is not authenticated.", failure.Message);
        Assert.Equal(["flush"], world.Trace);
        Assert.Empty(world.Output.Entries.SelectMany(entry => entry.Messages));
        Assert.True(Assert.Single(world.Output.Entries).IsClose);
        Assert.Equal(0, world.Characters.WriteCommits);
    }

    [Fact]
    public async Task Roster_registers_the_original_query_batch_without_a_transaction()
    {
        await using var world = new World();
        await world.DispatchAsync(0);
        var messages = world.Output.Entries.SelectMany(entry => entry.Messages).ToArray();
        Assert.Equal(["roster:False", "register:AccountCharacterRosterResponse:False", "register:HiddenCharacterInfoResponse:False", "register:HonorLevelInfoNotice:False", "flush"], world.Trace);
        var roster = Assert.IsType<AccountCharacterRosterResponse>(messages[0]);
        Assert.True(roster.AnswersRosterQuery);
        Assert.Same(world.Characters.Roster, roster.Roster);
        Assert.IsType<HiddenCharacterInfoResponse>(messages[1]);
        Assert.IsType<HonorLevelInfoNotice>(messages[2]);
        Assert.All(world.Output.Entries, entry => Assert.Same(world.Connection, entry.Connection));
        Assert.Equal(0, world.Characters.WriteCommits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Name_check_preserves_status_and_malformed_never_reaches_the_domain(bool malformed)
    {
        await using var world = new World();
        var bytes = malformed ? null : new byte[] { 0xFF, 0x00, 0xFE };
        world.Characters.NameStatus = CharacterNameStatus.AlreadyTaken;
        var status = await world.Bus.SendAsync(new GameCheckCharacterNameCommand(new(world.Connection), new(GameMessageMetadata.Empty, bytes)));
        Assert.Equal(malformed ? CharacterNameStatus.Malformed : CharacterNameStatus.AlreadyTaken, status);
        Assert.Equal(malformed ? 0 : 1, world.Characters.NameCalls);
        if (!malformed) Assert.Same(bytes, world.Characters.LastName);
        Assert.Equal(status, Assert.IsType<CheckCharacterNameResponse>(Assert.Single(world.Output.Entries.SelectMany(entry => entry.Messages))).Status);
        Assert.Equal(0, world.Characters.WriteCommits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Creation_commits_before_ACK_then_reads_the_roster_only_after_success(int mode)
    {
        await using var world = new World();
        world.Characters.Creation = new(mode == 2 ? CharacterCreationStatus.NameTaken : CharacterCreationStatus.Created, new(71));
        var raw = mode == 0 ? null : new byte[] { 1, 2, 3 };
        var result = await world.Bus.SendAsync(new GameCreateCharacterCommand(new(world.Connection), new(GameMessageMetadata.Empty, 4, raw)));
        var expected = mode == 0 ? CharacterCreationStatus.MalformedRequest : world.Characters.Creation.Status;
        Assert.Equal(expected, result.Status);
        var messages = world.Output.Entries.SelectMany(entry => entry.Messages).ToArray();
        Assert.Equal(expected, Assert.IsType<CreateCharacterResponse>(messages[0]).Status);
        Assert.Equal(mode == 1 ? 2 : 1, messages.Length);
        Assert.Equal(mode == 0 ? 0 : 1, world.Characters.WriteCommits);
        Assert.Equal(mode == 1 ? 1 : 0, world.Characters.RosterCalls);
        if (mode != 0)
        {
            Assert.Same(raw, world.Characters.LastName);
            Assert.Equal((byte)4, world.Characters.LastJob);
        }
        if (mode == 1)
        {
            Assert.False(Assert.IsType<AccountCharacterRosterResponse>(messages[1]).AnswersRosterQuery);
            Assert.Equal(["create:True", "commit", "register:CreateCharacterResponse:False", "roster:False", "register:AccountCharacterRosterResponse:False", "flush"], world.Trace);
        }
        else if (mode == 2)
            Assert.Equal(["create:True", "commit", "register:CreateCharacterResponse:False", "flush"], world.Trace);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deletion_has_one_write_then_only_ACK_with_the_original_ushort_index(bool malformed)
    {
        await using var world = new World();
        const ushort index = 65530;
        var name = malformed ? null : new byte[] { 7, 8, 9 };
        var result = await world.Bus.SendAsync(new GameDeleteCharacterCommand(new(world.Connection), new(GameMessageMetadata.Empty, index, name)));
        var response = Assert.IsType<DeleteCharacterResponse>(Assert.Single(world.Output.Entries.SelectMany(entry => entry.Messages)));
        Assert.Equal(malformed ? CharacterDeletionStatus.MalformedRequest : CharacterDeletionStatus.Deleted, result.Status);
        Assert.Equal(result.Status, response.Status);
        Assert.Equal(index, response.RosterIndex);
        Assert.Equal(0, world.Characters.RosterCalls);
        Assert.Equal(malformed ? 0 : 1, world.Characters.WriteCommits);
        if (!malformed)
        {
            Assert.Same(name, world.Characters.LastName);
            Assert.Equal(index, world.Characters.LastIndex);
            Assert.Equal(["delete:True", "commit", "register:DeleteCharacterResponse:False", "flush"], world.Trace);
        }
    }

    [Theory]
    [InlineData("roster-flow")]
    [InlineData("displaced-connection")]
    public async Task Deletion_is_refused_without_a_write_while_the_account_still_has_a_selection(string selectedOn)
    {
        await using var world = new World();
        world.Selected = SelectedSessionIdentity.Create(new(19), new(71), new(selectedOn), 1, 10011);
        var result = await world.Bus.SendAsync(new GameDeleteCharacterCommand(new(world.Connection), new(GameMessageMetadata.Empty, 2, [7, 8, 9])));
        var response = Assert.IsType<DeleteCharacterResponse>(Assert.Single(world.Output.Entries.SelectMany(entry => entry.Messages)));
        Assert.Equal(CharacterDeletionStatus.CharacterSelected, result.Status);
        Assert.Equal(result.Status, response.Status);
        Assert.Equal(0, world.Characters.WriteCommits);
        Assert.Null(world.Characters.LastName);
    }

    [Fact]
    public async Task A_roster_read_failure_after_creation_preserves_the_ACK_and_committed_unit_then_closes_source()
    {
        await using var world = new World();
        world.Characters.FailRoster = true;
        var error = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => world.DispatchAsync(2));
        Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure, error.Reason);
        Assert.Same(world.Characters.Failure, error.InnerException);
        Assert.Equal(1, world.Characters.WriteCommits);
        Assert.Equal(0, world.Characters.Rollbacks);
        Assert.IsType<CreateCharacterResponse>(Assert.Single(world.Output.Entries.SelectMany(entry => entry.Messages)));
        Assert.False(world.Output.Entries[0].IsClose);
        Assert.True(world.Output.Entries[^1].IsClose);
        Assert.Equal(["create:True", "commit", "register:CreateCharacterResponse:False", "roster:False", "flush"], world.Trace);
    }

    private static IClientNetworkMessage Request(int kind) => kind switch
    {
        0 => new GetAccountCharacterRosterRequest(GameMessageMetadata.Empty),
        1 => new CheckCharacterNameRequest(GameMessageMetadata.Empty, [1, 2, 3]),
        2 => new CreateCharacterRequest(GameMessageMetadata.Empty, 0, [1, 2, 3]),
        _ => new DeleteCharacterRequest(GameMessageMetadata.Empty, 6, [1, 2, 3]),
    };

    private sealed class CaptureBus : ICommandBus
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public List<object> Commands { get; } = [];
        public ValueTask<T> SendAsync<T>(ICommand<T> command, CancellationToken ct = default) where T : notnull
        { Commands.Add(command); return ValueTask.FromResult(default(T)!); }
    }

    private sealed class World : IAsyncDisposable
    {
        public NetworkConnectionContext Connection { get; } = new(new("roster-flow"), NetworkEndpointKind.Game, 10011);
        public ServiceProvider Services { get; }
        public List<string> Trace { get; } = [];
        public Users Users { get; }
        public Characters Characters { get; private set; } = null!;
        public Output Output { get; }
        public SelectedSessionIdentity? Selected { get; set; }
        public ICommandBus Bus => Services.GetRequiredService<ICommandBus>();
        public World()
        {
            var services = new ServiceCollection();
            services.AddNetworkMessageDispatching();
            Users = new(Connection);
            services.AddSingleton<IOnlineUserQueryDomainService>(Users);
            var sessions = DispatchProxy.Create<ISelectedSessionDomainService, Sessions>();
            ((Sessions)(object)sessions).Find = account => Selected is { } held && held.AccountId == account ? held : null;
            services.AddSingleton(sessions);
            services.AddSingleton<ICharacterDomainService>(p => Characters = new(p.GetRequiredService<IDomainTransactionAccessor>(), Trace));
            services.AddSingleton<INetworkConnectionContexts>(new Connections(Connection));
            services.AddSingleton<IOutboundMessageEncoder>(p => new Encoder(p.GetRequiredService<IDomainTransactionAccessor>(), Trace));
            Output = new(Trace);
            services.AddSingleton<IOutboundSender>(Output);
            // Copy the production descriptor without constructing another service provider.
            // The internal typed adapter then resolves this world's actual message outbox.
            var protocol = new ServiceCollection();
            new ProtocolModule().ConfigureServices(protocol, new ConfigurationBuilder().Build());
            ((IServiceCollection)services).Add(protocol.Single(row => row.ServiceType == typeof(IGameCommandOutbox)));
            services.AddProtocolCommandHandler<GameAccountRosterCommand, AccountCharacterRoster, GameAccountRosterCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameGetAccountCharacterRoster, typeof(GetAccountCharacterRosterRequest)));
            services.AddProtocolCommandHandler<GameCheckCharacterNameCommand, CharacterNameStatus, GameCheckCharacterNameCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameCheckCharacterName, typeof(CheckCharacterNameRequest)));
            services.AddProtocolCommandHandler<GameCreateCharacterCommand, CharacterCreationResult, GameCreateCharacterCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameCreateCharacter, typeof(CreateCharacterRequest)));
            services.AddProtocolCommandHandler<GameDeleteCharacterCommand, CharacterDeletionResult, GameDeleteCharacterCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameDeleteCharacter, typeof(DeleteCharacterRequest)));
            services.RegisterProtocolMessageHandler<GetAccountCharacterRosterHandler, GetAccountCharacterRosterRequest, GameAccountRosterCommand>(ClientMessageType.GameGetAccountCharacterRoster);
            services.RegisterProtocolMessageHandler<CheckCharacterNameHandler, CheckCharacterNameRequest, GameCheckCharacterNameCommand>(ClientMessageType.GameCheckCharacterName);
            services.RegisterProtocolMessageHandler<CreateCharacterHandler, CreateCharacterRequest, GameCreateCharacterCommand>(ClientMessageType.GameCreateCharacter);
            services.RegisterProtocolMessageHandler<DeleteCharacterHandler, DeleteCharacterRequest, GameDeleteCharacterCommand>(ClientMessageType.GameDeleteCharacter);
            Services = services.BuildServiceProvider();
            Characters = (Characters)Services.GetRequiredService<ICharacterDomainService>();
        }
        public Task DispatchAsync(int kind) => Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(Connection), Request(kind), default).AsTask();
        public ValueTask DisposeAsync() => Services.DisposeAsync();
    }

    /// <summary>Answers only which selection the account holds; anything else is out of this test's reach.</summary>
    public class Sessions : DispatchProxy
    {
        internal Func<AccountId, SelectedSessionIdentity?> Find = _ => null;
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == nameof(ISelectedSessionDomainService.Find) && args is [AccountId account]
                ? Find(account)
                : throw new NotSupportedException(method?.Name);
    }

    private sealed class Users(NetworkConnectionContext connection) : IOnlineUserQueryDomainService
    {
        public bool Authenticated { get; set; } = true;
        public OnlineUserSnapshot GetRequired(OnlineUserId id) => new(id, connection, Authenticated ? new AccountId(19) : null, null, OnlineUserState.Authenticated, 1);
        public bool TryGetByConnection(NetworkConnectionId id, out OnlineUserSnapshot? user)
        { user = id == connection.ConnectionId ? GetRequired(new(Guid.Empty)) : null; return user is not null; }
    }

    private sealed class Characters(IDomainTransactionAccessor transactions, List<string> trace) : ICharacterDomainService
    {
        public AccountCharacterRoster Roster { get; } = new(new(19), 12, []);
        public CharacterCreationResult Creation { get; set; } = new(CharacterCreationStatus.Created, new(71));
        public CharacterNameStatus NameStatus { get; set; }
        public int NameCalls, RosterCalls, WriteCommits, Rollbacks;
        public byte[]? LastName;
        public byte LastJob;
        public ushort LastIndex;
        public bool FailRoster;
        public IOException Failure { get; } = new("roster appearance failure after creation");
        public ValueTask<AccountCharacterRoster> GetAccountRosterWithAppearanceAsync(AccountId account, CancellationToken ct)
        {
            RosterCalls++; trace.Add("roster:" + (transactions.Current is not null));
            if (FailRoster) throw Failure;
            return ValueTask.FromResult(Roster);
        }
        public ValueTask<CharacterNameStatus> CheckNameAsync(byte[] name, CancellationToken ct)
        { NameCalls++; LastName = name; return ValueTask.FromResult(NameStatus); }
        private void Write(string operation)
        {
            trace.Add(operation + ":" + (transactions.Current is not null));
            (transactions.Current ?? throw new InvalidOperationException("Missing real work unit")).Enlist(this, () => new Participant(this, trace));
        }
        public ValueTask<CharacterCreationResult> CreateCharacterAsync(AccountId account, byte job, byte[] name, CancellationToken ct)
        { Write("create"); LastName = name; LastJob = job; return ValueTask.FromResult(Creation); }
        public ValueTask<CharacterDeletionResult> DeleteCharacterAsync(AccountId account, ushort index, byte[] name, CancellationToken ct)
        { Write("delete"); LastName = name; LastIndex = index; return ValueTask.FromResult(new CharacterDeletionResult(CharacterDeletionStatus.Deleted, new(71), 2)); }
        public ValueTask<AccountCharacterRoster> GetAccountRosterAsync(AccountId account, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ActiveCharacterState> LoadForSelectionAsync(AccountId account, ushort slot, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<ActiveCharacterState> LoadByRosterPositionAsync(AccountId account, ushort index, CancellationToken ct) => throw new NotSupportedException();
        public ICharacter Get(CharacterId id) => throw new NotSupportedException();
        private sealed class Participant(Characters owner, List<string> trace) : ITransactionParticipant
        {
            public bool CommitCanFail => false;
            public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
            public ValueTask CommitAsync(CancellationToken ct) { owner.WriteCommits++; trace.Add("commit"); return ValueTask.CompletedTask; }
            public ValueTask RollbackAsync(CancellationToken ct) { owner.Rollbacks++; return ValueTask.CompletedTask; }
        }
    }
    private sealed class Output(List<string> trace) : IOutboundSender
    {
        public List<OutboundEntry> Entries { get; } = [];
        public void Submit(IReadOnlyList<OutboundEntry> entries) { trace.Add("flush"); Entries.AddRange(entries); }
    }
    private sealed class Connections(NetworkConnectionContext source) : INetworkConnectionContexts
    {
        public bool TryGet(NetworkConnectionId id, [NotNullWhen(true)] out NetworkConnectionContext? connection)
        { connection = id == source.ConnectionId ? source : null; return connection is not null; }
    }
    private sealed class Encoder(IDomainTransactionAccessor transactions, List<string> trace) : IOutboundMessageEncoder
    {
        public byte[] EncodeMessage(NetworkConnectionContext connection, IServerNetworkMessage message)
        { trace.Add("register:" + message.GetType().Name + ":" + (transactions.Current is not null)); return [1, 2, 3]; }
        public byte[] EncodeBatch(NetworkConnectionContext connection, IReadOnlyList<IServerNetworkMessage> messages) => [1, 2, 3];
    }
}
