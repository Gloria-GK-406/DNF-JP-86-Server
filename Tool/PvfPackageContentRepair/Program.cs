using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using PvfPackageContentRepair;

// Issue #340: package content that the PVF itself gets wrong. Rewards whose item id no list declares are removed,
// pets whose species creature.lst forgot get their TW70 species row back, and package rewards with an absolute
// expiration date that has passed become permanent. Everything else in the archive is preserved byte for byte.
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;
try
{
    return args switch
    {
        ["audit", var pvf] => Repair.Audit(pvf),
        ["repair", var source, var output] => Repair.Run(source, output),
        _ => Usage(),
    };
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

static int Usage()
{
    Console.Error.WriteLine("""
        PvfPackageContentRepair audit  <Script.pvf>
        PvfPackageContentRepair repair <source.pvf> <new.pvf>

        audit   counts what repair would change; a repaired archive reports zero in every category.
        repair  writes <new.pvf>, <new.pvf>.json (summary) and <new.pvf>.changes.tsv (every edit).
        """);
    return 2;
}

internal static class Repair
{
    /// <summary>Stackable types the #328 census opens as packages.</summary>
    private static readonly HashSet<string> PackageTypes =
        ["[booster]", "[cera package]", "[cera booster]", "[booster random]", "[booster selection]", "[usable cera package]"];

    private const string Tw70 = "TW70 creature.lst";
    private const string SelfNamed = "in-archive .cre named name_0<species>";

    /// <summary>
    /// Species rows the 86 creature.lst lacks. Only species a package reward pet needs are written back.
    /// <see cref="Tw70"/>: the row is in Garena TW 2013-07-04 creature/creature.lst
    /// (Research/tw70-pvf-archive/Script.20130704.pvf). <see cref="SelfNamed"/>: TW70 predates the species; the
    /// only unlisted .cre whose <c>[name]</c> is <c>name_0&lt;species&gt;</c> is used (brown.cre plays
    /// brownie_*.ani; the two petit_skeleton_gold .cre files are byte-identical, the _7 one matches the 7-day pet).
    /// Every target .cre, its .ani files and their .img frames exist in the archive and the client's ImagePacks2.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, (string Path, string Source)> RestorableSpecies = new Dictionary<int, (string, string)>
    {
        [70] = ("Bakal/bakal.cre", Tw70),
        [75] = ("event-Amy/Amy.cre", Tw70),
        [76] = ("event-Petit_Alex/Petit_Alex.cre", Tw70),
        [77] = ("event-Petit_Assaulter/Petit_Assaulter.cre", Tw70),
        [78] = ("event-Petit_Tiger/Petit_Tiger.cre", Tw70),
        [85] = ("GirlsDay/girlsDay.cre", Tw70),
        [143] = ("Princess_Petit/princess_petit_period.cre", Tw70),
        [144] = ("Petit_Bebe/petit_bebe_period.cre", Tw70),
        [146] = ("Petit_Bebe/petit_bebe.cre", Tw70),
        [158] = ("SDCharacterKR/ATFighter/SDCharacter_ATFighter.cre", Tw70),
        [159] = ("SDCharacterKR/ATGunner/SDCharacter_ATGunner.cre", Tw70),
        [160] = ("SDCharacterKR/ATMage/SDCharacter_ATMage.cre", Tw70),
        [163] = ("SDCharacterKR/Fighter/SDCharacter_Fighter.cre", Tw70),
        [164] = ("SDCharacterKR/Gunner/SDCharacter_Gunner.cre", Tw70),
        [165] = ("SDCharacterKR/Mage/SDCharacter_Mage.cre", Tw70),
        [166] = ("SDCharacterKR/Priest/SDCharacter_Priest.cre", Tw70),
        [167] = ("SDCharacterKR/SwordMan/SDCharacter_SwordMan.cre", Tw70),
        [168] = ("SDCharacterKR/Thief/SDCharacter_Thief.cre", Tw70),
        [172] = ("brown/brown.cre", SelfNamed),
        [477] = ("petit_skeleton_gold/petit_skeleton_gold_7.cre", SelfNamed),
    };

    private const string ExpirationTag = "[expiration date]";

    private sealed record Package(int Id, string Path, string Type, List<Sym> Tokens);
    private sealed record ItemFacts(string Path, int? PetSpecies, DateTime[] Expirations);

    private sealed class Census
    {
        public required Engine Engine { get; init; }
        public required IReadOnlyDictionary<int, string> Equipment { get; init; }
        public required IReadOnlyDictionary<int, string> Stackable { get; init; }
        public required HashSet<int> Species { get; init; }
        public required List<Package> Packages { get; init; }
        public required Dictionary<int, ItemFacts?> Items { get; init; }
        public int UnlistedPackageFiles { get; set; }
    }

    public static int Audit(string pvf)
    {
        using var engine = new Engine(Path.GetFullPath(pvf));
        var census = Load(engine);
        var now = DateTime.Now;
        var plan = Plan(census, now);
        var report = Summarize(census, plan, now);
        Console.WriteLine(JsonSerializer.Serialize(report, Json));
        return 0;
    }

    public static int Run(string sourcePath, string outputPath)
    {
        var source = Path.GetFullPath(sourcePath);
        var output = Path.GetFullPath(outputPath);
        if (source.Equals(output, StringComparison.OrdinalIgnoreCase)) throw new IOException("Refuse in-place repair.");
        foreach (var f in new[] { output, output + ".json", output + ".changes.tsv" })
            if (File.Exists(f)) throw new IOException("Refuse to overwrite " + f);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var now = DateTime.Now;
        using var engine = new Engine(source);
        var census = Load(engine);
        var plan = Plan(census, now);
        var before = Summarize(census, plan, now);
        var rows = new List<string> { "kind\tpackage_id\tpath\tcontainer\titem_id\tcount\treason" };

        // 1. creature.lst: put the lost species rows back, each before the first larger id.
        if (plan.RestoredSpecies.Count > 0)
        {
            const string list = "creature/creature.lst";
            var tokens = engine.Read(list);
            var template = tokens.First(t => t.IsString);
            foreach (var species in plan.RestoredSpecies.Order())
            {
                var (path, provenance) = RestorableSpecies[species];
                var text = template.Wide
                    ? new Sym(template.Kind, 0, true, null, path)
                    : new Sym(template.Kind, 0, false, Encoding.UTF8.GetBytes(path));
                var at = tokens.Count;
                for (var k = 0; k + 1 < tokens.Count; k++)
                    if (tokens[k].Kind == PvfTokenKind.Int32 && tokens[k + 1].IsString && tokens[k].Value > species) { at = k; break; }
                tokens.InsertRange(at, [Sym.Int(species), text]);
                rows.Add($"species\t\t{list}\t\t{species}\t\tadded {path} ({provenance})");
            }
            engine.Replace(list, tokens, $"#340: {plan.RestoredSpecies.Count} species rows restored");
        }

        // 2. Packages: remove rewards no list declares and pets whose species cannot be restored.
        foreach (var package in census.Packages)
        {
            var script = PackageScript.Repair(package.Tokens, plan.RemoveReason);
            if (script.Changes.Count == 0) continue;
            engine.Replace(package.Path, script.Result.ToList(), $"#340: {script.Changes.Count(c => c.ItemId != 0)} rewards removed");
            foreach (var c in script.Changes)
                rows.Add($"package\t{package.Id}\t{package.Path}\t{c.Container}\t{(c.ItemId == 0 ? "" : c.ItemId)}\t{(c.ItemId == 0 ? "" : c.Count)}\t{c.Reason}");
        }

        // 3. Expired rewards: drop the absolute expiration so the item is permanent.
        foreach (var (id, facts) in plan.Expired)
        {
            var tokens = engine.Read(facts.Path);
            var kept = new List<Sym>();
            for (var k = 0; k < tokens.Count; k++)
            {
                if (PackageScript.IsTag(tokens[k]) && tokens[k].Text == ExpirationTag && k + 1 < tokens.Count && tokens[k + 1].IsString)
                { rows.Add($"expiration\t\t{facts.Path}\t{ExpirationTag}\t{id}\t\tremoved {tokens[k + 1].Text}"); k++; continue; }
                kept.Add(tokens[k]);
            }
            engine.Replace(facts.Path, kept, $"#340: permanent, {ExpirationTag} removed");
        }

        engine.Write(output);

        // Re-audit the written archive with a fresh reader: every category must now be empty.
        using var patched = new Engine(output);
        var patchedCensus = Load(patched);
        var after = Summarize(patchedCensus, Plan(patchedCensus, now), now);
        if (after.MissingRewardOccurrences != 0 || after.UnresolvedPetOccurrences != 0 || after.ExpiredRewardItems != 0 || after.SpeciesToRestore.Length != 0)
            throw new InvalidDataException("Repaired archive still has findings: " + JsonSerializer.Serialize(after, Json));

        var summary = new
        {
            Source = source,
            SourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),
            Output = output,
            OutputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))),
            RunAt = now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Before = before,
            After = after,
            ReplacedFiles = engine.Log.Count - 1,
            engine.AppendedNarrow,
            engine.AppendedWideChars,
            Log = engine.Log,
        };
        var json = JsonSerializer.Serialize(summary, Json);
        File.WriteAllText(output + ".json", json, new UTF8Encoding(false));
        File.WriteAllLines(output + ".changes.tsv", rows, new UTF8Encoding(false));
        Console.WriteLine(json);
        return 0;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static Census Load(Engine engine)
    {
        var a = engine.Archive;
        var census = new Census
        {
            Engine = engine,
            Equipment = a.ReadList("equipment/equipment.lst").Paths(PvfListDuplicates.First, "equipment/"),
            Stackable = a.ReadList("stackable/stackable.lst").Paths(PvfListDuplicates.First, "stackable/"),
            Species = a.ReadList("creature/creature.lst").Entries.Select(e => e.Id).ToHashSet(),
            Packages = [],
            Items = [],
        };
        foreach (var (id, path) in census.Stackable)
        {
            var index = a.FindFile(path);
            if (index < 0 || !a.GetEntry(index).IsScript) { census.UnlistedPackageFiles++; continue; }
            var tokens = Engine.Read(a, path);
            string? type = null;
            for (var k = 0; k + 1 < tokens.Count; k++)
                // The client's [stackable type] handler overwrites: the last declaration wins.
                if (PackageScript.IsTag(tokens[k]) && tokens[k].Text == "[stackable type]" && tokens[k + 1].IsString) type = tokens[k + 1].Text;
            if (type is not null && PackageTypes.Contains(type)) census.Packages.Add(new(id, path, type, tokens));
        }
        return census;
    }

    private static ItemFacts? Facts(Census census, int id)
    {
        if (census.Items.TryGetValue(id, out var cached)) return cached;
        var a = census.Engine.Archive;
        string? path = census.Stackable.TryGetValue(id, out var s) ? s : census.Equipment.TryGetValue(id, out var e) ? e : null;
        ItemFacts? facts = null;
        if (path is not null && a.FindFile(path) is var index and >= 0 && a.GetEntry(index).IsScript)
        {
            var tokens = Engine.Read(a, path);
            int? species = null; var pet = false;
            var expirations = new List<DateTime>();
            for (var k = 0; k + 1 < tokens.Count; k++)
            {
                if (!PackageScript.IsTag(tokens[k])) continue;
                var tag = tokens[k].Text;
                if (tag == "[equipment type]" && tokens[k + 1].IsString && tokens[k + 1].Text == "[creature]") pet = true;
                else if (tag == "[creature species]" && tokens[k + 1].Kind == PvfTokenKind.Int32) species = tokens[k + 1].Value;
                else if (tag == ExpirationTag && tokens[k + 1].IsString)
                    expirations.Add(DateTime.ParseExact(tokens[k + 1].Text.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            }
            facts = new(path, pet && census.Equipment.ContainsKey(id) ? species ?? -1 : null, [.. expirations]);
        }
        census.Items[id] = facts;
        return facts;
    }

    private sealed record RepairPlan(
        Func<int, string?> RemoveReason,
        HashSet<int> MissingIds,
        Dictionary<int, int> UnresolvedPets,
        List<int> RestoredSpecies,
        List<(int Id, ItemFacts Facts)> Expired,
        List<(int Id, ItemFacts Facts)> FutureExpiring);

    private static RepairPlan Plan(Census census, DateTime now)
    {
        var referenced = new SortedSet<int>();
        foreach (var p in census.Packages) referenced.UnionWith(PackageScript.Repair(p.Tokens, _ => null).RewardIds);

        var missing = referenced.Where(id => !census.Equipment.ContainsKey(id) && !census.Stackable.ContainsKey(id)).ToHashSet();
        var petSpecies = new Dictionary<int, int>();
        var expired = new List<(int, ItemFacts)>();
        var future = new List<(int, ItemFacts)>();
        foreach (var id in referenced.Where(id => !missing.Contains(id)))
        {
            var facts = Facts(census, id);
            if (facts is null) continue;
            if (facts.PetSpecies is int species && !census.Species.Contains(species)) petSpecies[id] = species;
            if (facts.Expirations.Length == 0) continue;
            if (facts.Expirations.Any(d => d <= now)) expired.Add((id, facts));
            else future.Add((id, facts));
        }
        var restored = petSpecies.Values.Distinct()
            .Where(sp => RestorableSpecies.TryGetValue(sp, out var cre) && census.Engine.Archive.FindFile("creature/" + cre.Path) >= 0)
            .Order().ToList();
        var unresolved = petSpecies.Where(kv => !restored.Contains(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);

        string? Reason(int id) =>
            missing.Contains(id) ? "missing from equipment.lst and stackable.lst"
            : unresolved.TryGetValue(id, out var sp) ? $"pet species {sp} not in creature.lst and no source to restore it"
            : null;
        return new(Reason, missing, unresolved, restored, expired, future);
    }

    private sealed record Report(
        int PackagesScanned,
        int UnlistedStackableFiles,
        int PackagesWithMissingRewards,
        int MissingRewardIds,
        int MissingRewardOccurrences,
        int[] SpeciesToRestore,
        int UnresolvedPets,
        int UnresolvedPetOccurrences,
        int ExpiredRewardItems,
        string[] ExpiredRewards,
        string[] FutureExpiringRewards,
        string[] PetsWithMissingSpecies,
        string[] PackagesEmptiedByRemoval,
        string[] SelectionBlocksEmptied,
        string[] SelectionBlocksBelowSelectionNum,
        string[] ParseAnomalies);

    private static Report Summarize(Census census, RepairPlan plan, DateTime now)
    {
        var affected = 0; var occurrences = 0; var petOccurrences = 0;
        var emptied = new List<string>(); var emptiedBlocks = new List<string>(); var shortBlocks = new List<string>(); var anomalies = new List<string>();
        foreach (var p in census.Packages)
        {
            var script = PackageScript.Repair(p.Tokens, plan.RemoveReason);
            var removed = script.Changes.Where(c => c.ItemId != 0 && !c.Reason.StartsWith("marker", StringComparison.Ordinal)).ToList();
            anomalies.AddRange(script.Anomalies.Select(x => $"{p.Id}\t{p.Path}\t{x}"));
            if (removed.Count == 0 && !script.Changes.Any()) continue;
            affected++;
            occurrences += removed.Count(c => plan.MissingIds.Contains(c.ItemId));
            petOccurrences += removed.Count(c => plan.UnresolvedPets.ContainsKey(c.ItemId));
            if (script.RewardsAfter == 0 && script.RewardsBefore > 0) emptied.Add($"{p.Id}\t{p.Type}\t{p.Path}\t{script.RewardsBefore} rewards");
            var selectionNum = SelectionNum(p.Tokens);
            foreach (var b in script.Blocks.Where(b => b.Before > b.After))
                if (b.After == 0) emptiedBlocks.Add($"{p.Id}\t{p.Path}\t({b.C1},{b.C2})\t{b.Before}->0");
                else if (b.After < selectionNum) shortBlocks.Add($"{p.Id}\t{p.Path}\t({b.C1},{b.C2})\tselection num {selectionNum}\t{b.Before}->{b.After}");
        }
        return new(
            census.Packages.Count,
            census.UnlistedPackageFiles,
            affected,
            plan.MissingIds.Count,
            occurrences,
            [.. plan.RestoredSpecies],
            plan.UnresolvedPets.Count,
            petOccurrences,
            plan.Expired.Count,
            [.. plan.Expired.Select(x => $"{x.Id}\t{x.Facts.Path}\t{string.Join(",", x.Facts.Expirations.Select(d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)))}")],
            [.. plan.FutureExpiring.Select(x => $"{x.Id}\t{x.Facts.Path}")],
            [.. PetsWithMissingSpecies(census)],
            [.. emptied],
            [.. emptiedBlocks],
            [.. shortBlocks],
            [.. anomalies]);
    }

    private static IEnumerable<string> PetsWithMissingSpecies(Census census)
    {
        var referenced = new SortedSet<int>();
        foreach (var p in census.Packages) referenced.UnionWith(PackageScript.Repair(p.Tokens, _ => null).RewardIds);
        foreach (var id in referenced)
            if (Facts(census, id) is { PetSpecies: int sp } facts && !census.Species.Contains(sp))
                yield return $"{id}\t{facts.Path}\tspecies {sp}";
    }

    private static int SelectionNum(List<Sym> tokens)
    {
        for (var k = 0; k + 1 < tokens.Count; k++)
            if (PackageScript.IsTag(tokens[k]) && tokens[k].Text == "[booster selection num]" && tokens[k + 1].Kind == PvfTokenKind.Int32)
                return tokens[k + 1].Value;
        return 0;
    }
}
