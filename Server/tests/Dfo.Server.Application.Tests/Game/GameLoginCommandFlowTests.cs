using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Authentication;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Infrastructure;
using Dfo.Server.Infrastructure.Configuration;
using Dfo.Server.Persistence;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Login;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Game;

public sealed class GameLoginCommandFlowTests
{
    [Fact]
    public async Task Thin_handler_delivers_original_request_and_dispatch_once()
    {
        var bus=new CaptureBus(); var handler=new GameLoginHandler(bus);
        var dispatch=new NetworkDispatchContext(new(new("thin"),NetworkEndpointKind.Game,10011));
        var request=new GameLoginRequest(GameMessageMetadata.Empty,"mid","ticket");
        await handler.HandleAsync(dispatch,request,default);
        var command=Assert.IsType<GameLoginCommand>(Assert.Single(bus.Commands));
        Assert.Same(dispatch,command.Dispatch); Assert.Same(request,command.Request);
        Assert.Same(request,command.Message); Assert.IsType<ProtocolFrameMetadata.Game>(command.Frame);
    }

    [Fact]
    public async Task Anonymous_login_commits_before_settings_and_registers_seven_messages_with_activation_between_hotkeys_and_auction()
    {
        await using var world=await World.CreateAsync();
        await world.LoginAsync();
        Assert.Empty(world.Closes.Connections);
        var observation=Assert.Single(world.Observations);
        Assert.Equal(OnlineUserState.Authenticated,observation.State); Assert.NotNull(observation.Account); Assert.False(observation.HasTransaction);
        Assert.Equal(7,world.Outbox.Messages.Count);
        Assert.Equal(1,world.AuthenticationCalls);
        Assert.IsType<GameLoginResponse>(world.Outbox.Messages[0]);
        Assert.Equal(["settings","GameLoginResponse","AccountGameOptionsMessage","LoginHotkeyStateMessage",
            "activate","AuctionServiceStateNotice","AuctionServiceStateNotice","HonorLevelInfoNotice","InformNoticeNotice"],world.Events);
        Assert.Equal(new ClientSettingsOrigin(world.Authenticated.SessionIdentity,world.Account,
            world.Authenticated.Generation,null),world.Profiles.Origin);
        Assert.Equal((byte)0,world.Profiles.KeyType);
        Assert.Equal(["GameLogin:Environment","GameLogin:ConnectionPort","Game:EndpointHost",
            "GameLogin:EndpointPortA","GameLogin:EndpointPortB"],world.Configuration.Reads);

    }

    [Theory]
    [InlineData("missing_online_registration")]
    [InlineData("ticket_rejected")]
    [InlineData("identity_not_published")]
    [InlineData("delivery_failed")]
    [InlineData("delivery_cancelled")]
    public async Task Rejected_login_preserves_category_full_source_and_never_reads_deployment_before_settings(string category)
    {
        await using var world=await World.CreateAsync(category);
        await world.LoginAsync(category=="ticket_rejected"?"secret-invalid-ticket":null);
        Assert.Empty(world.Configuration.Reads); Assert.Empty(world.Outbox.Messages);
        Assert.Same(world.Connection,Assert.Single(world.Closes.Connections));
        Assert.Equal(CancellationToken.None,world.Closes.Token);
        Assert.False(TestGameplay.Run(world.Services,()=>world.Users.TryGetByConnection(world.Connection.ConnectionId,out _)));
        Assert.Contains(category,world.Log.Text);
        Assert.DoesNotContain("secret-invalid-ticket",world.Log.Text);
        Assert.DoesNotContain(world.MId,world.Log.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_or_configuration_failure_preserves_committed_binding_and_original_ticket_reconnect(bool configurationFailure)
    {
        await using var world=await World.CreateAsync(configurationFailure?"bad_configuration":"delivery_failed");
        await world.LoginAsync();
        Assert.Single(world.Closes.Connections);
        if(configurationFailure)
            Assert.Equal(["GameLogin:Environment","GameLogin:ConnectionPort"],world.Configuration.Reads);
        else Assert.Empty(world.Configuration.Reads);
        Assert.Empty(world.Outbox.Messages);
        var sessions=world.Services.GetRequiredService<LoginSessionRegistry>();
        Assert.NotNull(sessions.CaptureAuthority(world.Connection.ConnectionId)); // committed binding survives delivery failure
        TestGameplay.Run(world.Services,()=>sessions.Disconnected(world.Connection.ConnectionId)); // actual observer stage
        var first=Assert.Single(world.Observations);
        Assert.Equal(OnlineUserState.Authenticated,first.State); Assert.NotNull(first.Account); Assert.False(first.HasTransaction);
        var second=TestGameplay.Run(world.Services,()=>world.Users.RegisterConnection(new(new("second"),NetworkEndpointKind.Game,10011)));
        world.CurrentConnection=second.Connection;
        await world.Services.GetRequiredService<ICommandBus>().SendAsync(new GameLoginCommand(
            new(second.Connection),new(GameMessageMetadata.Empty,world.MId,world.Ticket)));
        Assert.Equal(7,world.Outbox.Messages.Count); // original ticket reconnects after actual teardown
        Assert.Single(world.Closes.Connections); Assert.Equal(2,world.AuthenticationCalls);
        var reconnected=Assert.Single(world.Observations.Skip(1));
        Assert.Equal(first.Account,reconnected.Account); Assert.Equal(OnlineUserState.Authenticated,reconnected.State);
        Assert.False(reconnected.HasTransaction);

    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_authentication_fault_preserves_the_cause_category_close_and_unbound_ticket(bool cancelled)
    {
        await using var world=await World.CreateAsync(cancelled?"redemption_cancelled":"redemption_failed");
        var error=await Record.ExceptionAsync(()=>world.LoginAsync());
        Assert.Same(world.AuthenticationFailure,error);
        Assert.Equal(1,world.AuthenticationCalls); Assert.Empty(world.Observations); Assert.Empty(world.Configuration.Reads);
        Assert.Empty(world.Outbox.Messages); Assert.Same(world.Connection,Assert.Single(world.Closes.Connections));
        Assert.Contains(cancelled?"redemption_cancelled":"redemption_failed",world.Log.Text);
        Assert.Null(world.Services.GetRequiredService<LoginSessionRegistry>().CaptureAuthority(world.Connection.ConnectionId));
    }

    private sealed class World : IAsyncDisposable
    {
        private readonly string directory;
        private readonly string database;
        public ServiceProvider Services {get;}
        public NetworkConnectionContext Connection {get;}=new(new("real-login"),NetworkEndpointKind.Game,10011);
        public NetworkConnectionContext CurrentConnection {get;set;}
        public List<SettingsObservation> Observations {get;}=[];
        public int AuthenticationCalls {get;set;}
        public Exception? AuthenticationFailure {get;}
        public IOnlineUserDomainService Users=>Services.GetRequiredService<IOnlineUserDomainService>();
        public AccountId Account => Authenticated.AccountId!.Value;
        public string MId {get;private set;}=""; public string Ticket {get;private set;}="";
        public List<string> Events {get;}=[];
        public RecordingOutbox Outbox {get;}
        public Closures Closes {get;}=new(); public RecordingLog Log {get;}=new();
        public RecordingConfiguration Configuration {get;}
        public Profiles Profiles {get;}
        public OnlineUserSnapshot Authenticated=>TestGameplay.Run(Services,()=>
            { Assert.True(Users.TryGetByConnection(Connection.ConnectionId,out var user));return user!; });
        private World(string? mode)
        {
            CurrentConnection=Connection;
            AuthenticationFailure=mode switch { "redemption_cancelled"=>new OperationCanceledException("one-shot actual-auth fault"),
                "redemption_failed"=>new IOException("one-shot actual-auth fault"),_=>null };
            directory=Path.Combine(Path.GetTempPath(),"task06-login-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            database=Path.Combine(directory,"login.db");
            var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["Database:Path"]=database,["GameLogin:Environment"]="1",["GameLogin:ConnectionPort"]="10128",
                ["Game:EndpointHost"]="192.0.2.20",["GameLogin:EndpointPortA"]="17200",["GameLogin:EndpointPortB"]="17201",
            }).Build();
            Configuration=new(new ServerConfigService(configuration),mode=="bad_configuration");Outbox=new(Events);Profiles=new(Events);
            var services=new ServiceCollection();services.AddLogging();
            services.AddSingleton<IServerConfigService>(Configuration);
            new InfrastructureModule().ConfigureServices(services,configuration);
            new ApplicationModule().ConfigureServices(services,configuration);
            new PersistenceModule().ConfigureServices(services,configuration);
            new ProtocolModule().ConfigureServices(services,configuration);
            services.RemoveAll<IAccountEntryStateDomainService>();services.AddSingleton<IAccountEntryStateDomainService>(provider=>new Settings(this,provider,mode switch { "delivery_failed"=>new IOException("one-shot settings fault"),
                "delivery_cancelled"=>new OperationCanceledException("one-shot settings cancellation"),_=>null }));
            services.RemoveAll<IGameCommandOutbox>();services.AddSingleton<IGameCommandOutbox>(Outbox);
            services.RemoveAll<ICommandConnectionClosures>();services.AddSingleton<ICommandConnectionClosures>(Closes);
            services.RemoveAll<IClientSettingsDomainService>();services.AddSingleton<IClientSettingsDomainService>(Profiles);
            services.AddSingleton<ILogger<GameLoginCommandHandler>>(Log);
            var original=services.Last(d=>d.ServiceType==typeof(IGameLoginAuthenticationDomainService));
            services.Remove(original);
            services.AddSingleton<IGameLoginAuthenticationDomainService>(provider=>new CountingAuthentication(
                (IGameLoginAuthenticationDomainService)ActivatorUtilities.CreateInstance(provider,original.ImplementationType!),this));
            if(mode=="identity_not_published")
            { services.RemoveAll<IGameLoginAuthenticationDomainService>();services.AddSingleton<IGameLoginAuthenticationDomainService>(new NoPublication()); }
            Services=services.BuildServiceProvider();
            if(mode!="missing_online_registration")TestGameplay.Run(Services,()=>Users.RegisterConnection(Connection));
        }
        public static async Task<World> CreateAsync(string? mode=null)
        {
            var world=new World(mode);
            var registered=await TestCredentials.RegisterAsync(world.Services,"flow-player","test password");
            var issued=await TestCredentials.LoginAsync(world.Services,"flow-player","test password");
            world.MId=issued.MId!;world.Ticket=issued.Ticket!;
            return world;
        }
        public async Task LoginAsync(string? ticket=null)=>await new GameLoginHandler(Services.GetRequiredService<ICommandBus>())
            .HandleAsync(new(Connection),new GameLoginRequest(GameMessageMetadata.Empty,MId,ticket??Ticket),default);
        public async ValueTask DisposeAsync()
        { await Services.DisposeAsync();SqliteTestPools.ClearOwned(database);Directory.Delete(directory,true); }
    }
    private sealed class CaptureBus:ICommandBus
    {
        public List<ICommand> Commands {get;}=[];
        public ValueTask<T> SendAsync<T>(ICommand<T> command,CancellationToken ct=default) where T:notnull
        { Commands.Add(command);return ValueTask.FromResult((T)(object)default(GameLoginCompleted)); }
    }
    private sealed class CountingAuthentication(IGameLoginAuthenticationDomainService inner,World world):IGameLoginAuthenticationDomainService
    {
        public async ValueTask<AccountId?> AuthenticateSessionAsync(OnlineUserId id,string m,string t,CancellationToken ct)
        {
            world.AuthenticationCalls++;
            var account=await inner.AuthenticateSessionAsync(id,m,t,ct);
            if(world.AuthenticationFailure is { } failure)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            return account;
        }
    }
    private sealed class NoPublication:IGameLoginAuthenticationDomainService
    {
        public ValueTask<AccountId?> AuthenticateSessionAsync(OnlineUserId id,string m,string t,CancellationToken ct)=>ValueTask.FromResult<AccountId?>(new(123));
    }
    private sealed record SettingsObservation(OnlineUserState? State,AccountId? Account,bool HasTransaction);
    private sealed class Settings(World world,IServiceProvider services,Exception? failure):IAccountEntryStateDomainService
    {
        private Exception? failNext=failure;
        public ValueTask<AccountEntryState> LoadAsync(AccountId id,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<AccountSettingsEntryState> LoadSettingsAsync(AccountId id,CancellationToken ct)
        {
            services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(world.CurrentConnection.ConnectionId,out var user);
            world.Observations.Add(new(user?.State,user?.AccountId,services.GetRequiredService<IDomainTransactionAccessor>().Current is not null));
            world.Events.Add("settings");
            if(failNext is { } error) { failNext=null;System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw(); }
            return ValueTask.FromResult(new AccountSettingsEntryState([1],[2],[3],1,0x42));
        }
    }
    private sealed class RecordingConfiguration(IServerConfigService inner,bool failOnce):IServerConfigService
    {
        private bool failNext=failOnce;
        public List<string> Reads {get;}=[];
        public T GetConfig<T>(string key)
        {
            if(key.StartsWith("GameLogin:",StringComparison.Ordinal)||key=="Game:EndpointHost") Reads.Add(key);
            if(key=="GameLogin:ConnectionPort"&&failNext) { failNext=false;throw new InvalidOperationException("one-shot deployment read fault"); }
            return inner.GetConfig<T>(key);
        }
    }
    private sealed class RecordingOutbox(List<string> events):IGameCommandOutbox
    {
        public List<IGameServerMessage> Messages {get;}=[];
        public ValueTask RegisterCmdAsync(NetworkConnectionId id,IGameCmdMessage message,CancellationToken ct)=>Add(message);
        public ValueTask RegisterNotiAsync(NetworkConnectionId id,IGameNotiMessage message,CancellationToken ct)=>Add(message);
        private ValueTask Add(IGameServerMessage message)
        { Messages.Add(message);events.Add(message.GetType().Name);return ValueTask.CompletedTask; }
        public ValueTask RegisterBatchAsync(NetworkConnectionContext c,IReadOnlyList<IGameServerMessage> m,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask RegisterExplicitAsync(NetworkConnectionId id,GameExplicitMessageDelivery d,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Closures:ICommandConnectionClosures
    {
        public List<NetworkConnectionContext> Connections {get;}=[];public CancellationToken Token {get;private set;}
        public ValueTask RequestCloseAsync(NetworkConnectionContext c,CancellationToken ct)
        { Connections.Add(c);Token=ct;return ValueTask.CompletedTask; }
    }
    private sealed class Profiles(List<string> events):IClientSettingsDomainService
    {
        public ClientSettingsOrigin? Origin {get;private set;}public byte? KeyType {get;private set;}
        public bool ActivateHotkeyProfile(ClientSettingsOrigin origin,byte keyType)
        { Origin=origin;KeyType=keyType;events.Add("activate");return true; }
        public ClientHotkeyUploadContext? CaptureHotkeyUpload(ClientSettingsOrigin origin)=>throw new NotSupportedException();
        public void ForgetConnection(NetworkConnectionId id){} public void ForgetConnection(GameSessionIdentity id){}
        public ValueTask<ClientAppearanceOptions> ReadAppearanceAsync(AccountId id,byte b,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<bool> SaveMainAsync(ClientSettingsOrigin o,byte[] p,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<bool> SaveQuickChatAsync(ClientSettingsOrigin o,byte b,byte[] p,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<bool> SaveHotkeysAsync(ClientHotkeyUploadContext o,byte[] p,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<IReadOnlyList<HotkeyProfileState>> ReadHotkeysAsync(AccountId id,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class RecordingLog:ILogger<GameLoginCommandHandler>
    {
        private readonly List<string> messages=[];public string Text=>string.Join("\n",messages);
        public IDisposable? BeginScope<T>(T state) where T:notnull=>null;public bool IsEnabled(LogLevel level)=>true;
        public void Log<T>(LogLevel l,EventId e,T state,Exception? ex,Func<T,Exception?,string> formatter)=>messages.Add(formatter(state,ex));
    }
}
