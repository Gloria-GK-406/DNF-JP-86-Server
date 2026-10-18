using System.Text.Json;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Read-only template audit. Database changes are deliberately not supported by this tool.
try
{
    if (args.Length != 2) throw new ArgumentException("Usage: ItemLifetimeAudit <item-ids.json> <report.json>; set PVF_ARCHIVE_PATH.");
    var path = Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH") ?? throw new InvalidOperationException("Missing PVF_ARCHIVE_PATH.");
    var services = new ServiceCollection();
    services.AddLogging();
    new InfrastructureModule().ConfigureServices(services, new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Pvf:ArchivePath"] = path }).Build());
    await using var provider = services.BuildServiceProvider();
    var content = provider.GetRequiredService<IItemPlacementDefinitionSource>();
    var ids = JsonSerializer.Deserialize<int[]>(await File.ReadAllTextAsync(args[0])) ?? throw new InvalidDataException("Missing item IDs.");
    var output = new List<AuditEntry>();
    foreach (var id in ids.Distinct().Order())
    {
        try
        {
            var item = await content.GetAsync(id, default);
            output.Add(item is null ? new(id, null, null, "Unknown template; excluded") : new(id, item.ItemKind, item.Lifetime, null));
        }
        catch (InvalidDataException error) { output.Add(new(id, null, null, error.Message)); }
    }
    await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Audited {output.Count} templates; {output.Count(x => x.Error is not null)} excluded. No database was accessed.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
internal sealed record AuditEntry(int ItemId, byte? ItemKind, ItemLifetimeDefinition? Lifetime, string? Error);
