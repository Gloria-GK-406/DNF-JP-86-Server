using System.Collections.Frozen;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfBossTowerContentSource(PvfArchiveProvider provider, IDungeonAdmissionDefinitionSource admissions) : IBossTowerContentSource
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private BossTowerDefinition? cached;
    public async ValueTask<BossTowerDefinition> GetAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (cached is not null) return cached;
            var admission = await admissions.GetAdmissionAsync(3505, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Boss tower admission header is missing.");
            return cached = Read(provider.Archive, admission);
        }
        finally { gate.Release(); }
    }
    private static BossTowerDefinition Read(PvfArchive archive, DungeonAdmissionDefinition admission)
    {
        var dungeon = PvfDocument.Open(archive, admission.ArchivePath).AtRoot();
        Require(dungeon.Read(PvfField.String("[dungeon type]")) == "tower of boss", admission.ArchivePath, "unexpected dungeon type");
        var count = dungeon.Read(PvfField.Int32("[tower random map indexes]", minimum: 1, maximum: 8));
        var stages = new List<BossTowerStageDefinition>();
        var maps = PvfListFile.Paths(archive, "map/map.lst", Dfo.Pvf.PvfListDuplicates.First, prefix: "map/");
        foreach (var row in dungeon.ReadAll(PvfField.Int32List("[tower stage]")))
        {
            Require(row.Count >= 3 && row.Count % 2 == 1 && row[0] == stages.Count + 1, admission.ArchivePath, "invalid stage pool");
            var candidates = new List<BossTowerMapCandidate>();
            for (var i = 1; i < row.Count; i += 2)
            {
                Require(row[i] > 0 && row[i + 1] > 0 && maps.ContainsKey(row[i]), admission.ArchivePath, "invalid weighted map");
                candidates.Add(ReadRoom(archive, maps[row[i]], row[0], row[i], row[i + 1]));
            }
            Require(candidates.Sum(x => (long)x.Weight) == 10000, admission.ArchivePath, "stage weights must total 10000");
            stages.Add(new(row[0], candidates.AsReadOnly()));
        }
        Require(stages.Count == count, admission.ArchivePath, "incomplete stage table");
        var autoRoot = PvfDocument.Open(archive, "etc/autorooting.etc").AtRoot()
            .Read(PvfField.Int32List("[auto rooting index]"));
        Require(autoRoot.All(x => x >= 0), "etc/autorooting.etc", "invalid auto-root item");
        var rewards = PvfDocument.Open(archive, "etc/global_bosstower.etc").AtRoot();
        return new(admission, dungeon.Read(PvfField.Int32("[basis level]", minimum: 1, maximum: 255)), stages.AsReadOnly(),
            new(rewards.Read(PvfField.Numbers("[reward exp weight]")), rewards.Read(PvfField.Int32List("[reward card num]")),
                rewards.Read(PvfField.Int32List("[reward item prob]")), rewards.Read(PvfField.Number("[reward gold weight]")),
                rewards.Read(PvfField.Number("[apc exp weight]")), rewards.Read(PvfField.Number("[monster exp weight]"))),
            dungeon.Read(PvfField.Int32("[tower max clear item num]", minimum: 0, maximum: 10))) { AutoRootItemIds = autoRoot.ToFrozenSet() };
    }
    private static BossTowerMapCandidate ReadRoom(PvfArchive archive, string path, int stage, int mapId, int weight)
    {
        var map = PvfDocument.Open(archive, path).AtRoot();
        Require(map.Read(PvfField.Int32("[dungeon]")) == 3505, path, "foreign dungeon map");
        Require(!map.TryRead(PvfField.Tokens("[ai character]", optional: true), out var apcs) || apcs.Count == 0,
            path, "unsupported APC declaration");
        var actors = new List<TowerActorDeclaration>();
        foreach (var row in Rows(map, "[monster]", 10, path))
        {
            Require(row.Take(8).All(t => t.Kind == PvfTokenKind.Int32) && row.Skip(8).All(t => t.IsString)
                && row[0].Value > 0 && row[1].Value is 0 or 1, path, "invalid monster declaration");
            var rank = archive.ResolveString(row[9].Value) switch
            {
                "[normal]" => (byte)0, "[champion]" => (byte)1, "[super champion]" => (byte)2, "[boss]" => (byte)3,
                _ => throw new InvalidDataException($"PVF '{path}': unsupported monster rank."),
            };
            actors.Add(new(row[0].Value, row[2].Value, row[1].Value == 1, rank, true,
                new(row[3].Value, row[4].Value, row[5].Value)));
        }
        var npcs = new List<int>();
        foreach (var row in Rows(map, "[NPC]", 5, path))
        {
            Require(row[0].Kind == PvfTokenKind.Int32 && row[0].Value > 0 && row[1].IsString
                && row.Skip(2).All(t => t.Kind == PvfTokenKind.Int32), path, "invalid NPC declaration");
            npcs.Add(row[0].Value);
        }
        var objects = new List<TowerObjectDeclaration>();
        foreach (var row in Rows(map, "[passive object]", 4, path))
        {
            Require(row.All(t => t.Kind == PvfTokenKind.Int32), path, "invalid passive object declaration");
            objects.Add(new(row[0].Value, new(row[1].Value, row[2].Value, row[3].Value)));
        }
        Require(actors.Count is > 0 and <= 255, path, "invalid monster count");
        return new(weight, new(stage, mapId, actors.AsReadOnly(), 0, [], objects.AsReadOnly()), npcs.AsReadOnly());
    }
    private static IEnumerable<PvfToken[]> Rows(PvfDocument document, string name, int width, string path)
    {
        foreach (var values in document.ReadAll(PvfField.Tokens(name, optional: true)))
        {
            Require(values.Count % width == 0, path, $"truncated {name}");
            for (var i = 0; i < values.Count; i += width) yield return values.Skip(i).Take(width).ToArray();
        }
    }
    private static void Require(bool valid, string path, string reason)
    {
        if (!valid) throw new InvalidDataException($"PVF '{path}': {reason}.");
    }
}
