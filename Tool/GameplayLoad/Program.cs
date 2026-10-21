using System.Globalization;
using GameplayLoad;

// GameplayLoad: synthetic gameplay load and measurement against a Release Dfo.Server.Host child process.
// Usage:
//   GameplayLoad run      --out <dir> [options]   full baseline run: (f) cold start, steady, (d) logins, (e) flood
//   GameplayLoad ramp     --out <dir> [options]   throughput ramp, stops at p99 > 50 ms / errors / < 90 % of target
//   GameplayLoad outbound --out <dir> [options]   exact per-request outbound bytes on an idle Host
// Options: --host <Dfo.Server.Host.dll> --pvf <Script.pvf> --tag <a> --solos 8 --pairs 6 --rate 20 --seed 1
//          --cold 60 --steady 120 --bursts 6 --burst-size 10 --burst-every 10 --flood 30 --flood-window 32 --dwell 4
//          --weights fence,move,area,reselect,create (0.47,0.45,0.068,0.01,0.002)
//          --rates 400,800,... --step 20 --warm 30

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("GameplayLoad run|ramp|outbound --out <dir> [--host <dll>] [--pvf <path>] [...] (see Program.cs header)");
    return 2;
}

var command = args[0];
var values = new Dictionary<string, string>(StringComparer.Ordinal);
for (var index = 1; index + 1 < args.Length; index += 2)
{
    if (!args[index].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unexpected argument '{args[index]}'.");
    values[args[index][2..]] = args[index + 1];
}

string Get(string key, string fallback) => values.TryGetValue(key, out var value) ? value : fallback;
double Number(string key, double fallback) => double.Parse(Get(key, fallback.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
TimeSpan Seconds(string key, double fallback) => TimeSpan.FromSeconds(Number(key, fallback));

var repository = FindRepository(AppContext.BaseDirectory);
var weights = Get("weights", "0.47,0.45,0.068,0.01,0.002").Split(',').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
var options = new RunOptions(
    HostDll: Path.GetFullPath(Get("host", Path.Combine(repository, "Server/src/Dfo.Server.Host/bin/Release/net10.0/Dfo.Server.Host.dll"))),
    Pvf: Path.GetFullPath(Get("pvf", Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH") ?? Path.Combine(repository, "Script.pvf"))),
    Output: Path.GetFullPath(values.TryGetValue("out", out var output) ? output : throw new ArgumentException("--out is required.")),
    Tag: Get("tag", "a"),
    Solos: (int)Number("solos", 8),
    Pairs: (int)Number("pairs", 6),
    Rate: Number("rate", 20),
    Seed: (int)Number("seed", 1),
    Cold: Seconds("cold", 60),
    Steady: Seconds("steady", 120),
    Bursts: (int)Number("bursts", 6),
    BurstSize: (int)Number("burst-size", 10),
    BurstEvery: Seconds("burst-every", 10),
    Flood: Seconds("flood", 30),
    FloodWindow: (int)Number("flood-window", 32),
    Dwell: Seconds("dwell", 4),
    Weights: new SoloWeights(weights[0], weights[1], weights[2], weights[3], weights[4]),
    RampRates: Get("rates", "400,800,1200,1600,2000,2400,3000,4000,5000,6000").Split(',').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray(),
    RampStep: Seconds("step", 20),
    RampWarm: Seconds("warm", 30),
    EntryConcurrency: (int)Number("entry-concurrency", 0));

if (!File.Exists(options.HostDll)) throw new FileNotFoundException("Build the Host in Release first: dotnet build Server/86JP.Server.slnx -c Release", options.HostDll);
if (!File.Exists(options.Pvf)) throw new FileNotFoundException("Script.pvf not found; pass --pvf or set PVF_ARCHIVE_PATH.", options.Pvf);

return command switch
{
    "run" => await Scenarios.RunAsync(options),
    "ramp" => await Scenarios.RampAsync(options),
    "outbound" => await Scenarios.OutboundAsync(options),
    _ => throw new ArgumentException($"Unknown command '{command}'."),
};

static string FindRepository(string start)
{
    for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "Server", "86JP.Server.slnx"))) return directory.FullName;
    throw new DirectoryNotFoundException("Run GameplayLoad from inside the repository.");
}
