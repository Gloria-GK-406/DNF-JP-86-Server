using System.Security.Cryptography;
using System.Text.Json;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application;
using Dfo.Server.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Encodings.Web;

// Resolves read-only content/rules only. No Host, sockets, persistence module or player database.
if (args.Length != 1 || !File.Exists(args[0]))
    throw new ArgumentException("Pass an explicit PVF file path.");
var archive = Path.GetFullPath(args[0]);
var services = new ServiceCollection();
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Pvf:ArchivePath"] = archive,
    ["Pvf:ChunkCacheBytes"] = "16777216",
}).Build();
services.AddLogging();
new ApplicationModule().ConfigureServices(services, config);
new InfrastructureModule().ConfigureServices(services, config);
using var provider = services.BuildServiceProvider();
var definition = await provider.GetRequiredService<IDungeonDefinitionSource>().GetAsync(4000, default);
var town = await provider.GetRequiredService<ITownDefinitionSource>().GetAsync(2, default);
string? refusal = null;
try
{
    _ = await provider.GetRequiredService<IDungeonStructureRuleService>()
        .ResolveAsync(4000, DungeonEntryFacts.None, 77, default);
}
catch (InvalidDataException error) { refusal = error.Message; }
if (definition is null || definition.MinimumRequiredLevel != 25 || definition.ConsumesFatigue
    || definition.PartyLimit != 1 || !definition.ProhibitsPractice || !definition.NoGiveupPenalty
    || definition.Mazes.Count != 1 || definition.Mazes[0].Width != 10 || definition.Mazes[0].Height != 10
    || !definition.Mazes[0].StartCandidates.SequenceEqual([new MazeCell(9, 6)])
    || refusal != "Dungeon '4000' maze '0': boss cell (9,0) is unreachable from the start.")
    throw new InvalidDataException("Content or current behavior changed; recheck the investigation before reusing its conclusions.");
using var source = File.OpenRead(archive);
Console.WriteLine(JsonSerializer.Serialize(new
{
    Archive = archive, Sha256 = Convert.ToHexString(SHA256.HashData(source)),
    definition?.ArchivePath, definition?.MinimumRequiredLevel, definition?.ConsumesFatigue,
    definition?.NoGiveupPenalty, definition?.PartyLimit, definition?.ProhibitsPractice,
    Mazes = definition?.Mazes.Select(m => new
    {
        m.Width, m.Height, OccupiedCells = m.Cells.Count,
        m.StartCandidates, m.BossCandidates, m.ConfigurationError,
        StartMaps = m.StartCandidates.Select(c => m.Cells.GetValueOrDefault(c)?.MapCandidates),
        BossDeclared = m.BossCandidates.Select(c => m.Cells.ContainsKey(c)),
        WithoutExplicitMap = m.Cells.Where(pair => pair.Value.MapCandidates.Count == 0 && pair.Value.BossMapCandidates.Count == 0).Select(pair => pair.Key),
    }),
    OrdinaryTopologyRefusal = refusal,
    Town2WorldMap101Areas = town?.Areas.Where(a => a.Kind == TownAreaKind.DungeonGate && a.Operand1 == 101),
    EntryReferences = EntryReferences.Read(archive),
    ProbeChecks = "PASS: current declarations and ordinary route refusal reproduced; no gameplay acceptance",
}, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
