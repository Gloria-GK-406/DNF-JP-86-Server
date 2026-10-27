using Dfo.Server.Abstractions;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Application;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Events;
using Dfo.Server.GameServer;
using Dfo.Server.HttpApi;
using Dfo.Server.Infrastructure;
using Dfo.Server.Persistence;

// Research composition only: no production deployment settings or player database.
if (args.Length != 1) throw new ArgumentException("Expected the absolute private probe.json path.");
var path = Path.GetFullPath(args[0]);
using var configuration = new ConfigurationManager();
configuration.AddJsonFile(path, optional: false, reloadOnChange: false);
var root = Path.GetDirectoryName(path)!;
var database = Path.GetFullPath(configuration["Database:Path"]!);
if (!database.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Probe database must remain inside its private runtime folder.");
var useDefaultPorts = configuration.GetValue<bool>("Research:UseDefaultPorts");
foreach (var key in new[] { "Networking:ChannelPort", "Networking:GamePorts:0", "Channel:Channels:0:Port",
    "LauncherAuth:PublicChannel:Port", "GameLogin:ConnectionPort", "GameLogin:EndpointPortA", "GameLogin:EndpointPortB" })
    if (!int.TryParse(configuration[key], out var port)
        || (port is < 22000 or > 22999) && !(useDefaultPorts && port is 7001 or 10011))
        throw new InvalidOperationException($"Probe port outside assigned range: {key}");
if (configuration["Channel:UseScriptCatalog"] != "false")
    throw new InvalidOperationException("Probe cannot derive listeners from the shared channel catalog.");

using var host = Host.CreateDefaultBuilder(Array.Empty<string>())
    .UseContentRoot(root)
    .ConfigureAppConfiguration((_, sources) => { sources.Sources.Clear(); sources.AddConfiguration(configuration); })
    .ConfigureServices((context, services) =>
    {
        IServerModule[] modules = [new InfrastructureModule(), new PersistenceModule(), new ApplicationModule()];
        foreach (var module in modules)
        {
            module.ConfigureServices(services, context.Configuration);
            services.AddSingleton(module);
        }
        services.AddSingleton<IManagedEventDomainService, ProbeCircusEvent>();
    })
    .ConfigureGameServer()
    .ConfigureHttpApi(configuration)
    .Build();
await host.RunAsync();

// A fixed research activity opens the native entrance; it does not implement quotas,
// continuation, clearing, rewards or an EPLP interpretation.
sealed class ProbeCircusEvent : IManagedEventDomainService
{
    public int EventId => 2226;
    private const string Title = "疯狂马戏团";
    private const string ShortName = "马戏团";
    private const string Description = "从暗黑城的马戏团热气球前往疯狂马戏团。通关后若出现凌凌，"
        + "可在结算界面选择“进入下一个地下城”，继续挑战凌凌的天幕剧场；未出现时，可选择“再次挑战”。";
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public ValueTask<ManagedEventEntry> DescribeAsync(DateTimeOffset now, CancellationToken ct) =>
        ValueTask.FromResult(new ManagedEventEntry(EventId, "circus-research-only", 0, "probe", true,
            "active", Title, ShortName, Description, "", "", Start, End, true, null, null, null));
    public ValueTask<EventInfo[]> GetEventInfoEntriesAsync(AccountId account, DateTimeOffset now, CancellationToken ct) =>
        ValueTask.FromResult<EventInfo[]>([new(EventId, Title, ShortName, Description, "", "", Start, End, "")]);
    public ValueTask ChangeAsync(EventChange change, string actor, DateTimeOffset now, CancellationToken ct) =>
        ValueTask.FromException(new NotSupportedException("Research activity is fixed in the private host."));
}
