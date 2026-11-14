using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using PvfPackageContentRepair;

// Issue #340: package content that the PVF itself gets wrong. Rewards whose item id no list declares are removed,
// pets whose species creature.lst forgot get their species row back, package rewards with an absolute expiration
// date that has passed become permanent, random-pool avatar rows naming an ability the avatar does not declare are
// removed, and one unclosed selection header that hides [action usable place] from the client is dropped.
// Packages left with no reward are deleted: their stackable.lst rows go, and so do the entries that name them in
// other packages and in the few non-package files listed in Repair.References.
// Everything else in the archive is preserved byte for byte.
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
    private sealed record ItemFacts(string Path, int? PetSpecies, DateTime[] Expirations, HashSet<int>? Abilities);

    private sealed class Census
    {
        public required Engine Engine { get; init; }
        public required IReadOnlyDictionary<int, string> Equipment { get; init; }
        public required IReadOnlyDictionary<int, string> Stackable { get; init; }
        public required HashSet<int> Species { get; init; }
        public required List<Package> Packages { get; init; }
        public required Dictionary<int, ItemFacts?> Items { get; init; }
        /// <summary><c>skill/abilitydatas.dat</c> <c>[ability case] id …</c>: the ability indexes each case declares.</summary>
        public required Dictionary<int, HashSet<int>> AbilityCases { get; init; }
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
            if (plan.Deleted.ContainsKey(package.Id)) continue;
            var tokens = package.Tokens;
            var stray = StrayHeader(tokens);
            if (stray >= 0)
            {
                rows.Add($"header\t{package.Id}\t{package.Path}\t{string.Join(" ", tokens.Skip(stray).Take(StrayHeaderLength))}\t\t\tremoved: unclosed selection header that swallows [action usable place]");
                tokens = [.. tokens.Take(stray), .. tokens.Skip(stray + StrayHeaderLength)];
            }
            var script = PackageScript.Repair(tokens, plan.RemoveReason, plan.AbilityReason);
            if (script.Changes.Count == 0 && stray < 0) continue;
            engine.Replace(package.Path, script.Result.ToList(), stray >= 0 ? "#340: stray selection header removed" : $"#340: {script.Changes.Count(c => c.ItemId != 0)} rewards removed");
            foreach (var c in script.Changes)
                rows.Add($"package\t{package.Id}\t{package.Path}\t{c.Container}\t{(c.ItemId == 0 ? "" : c.ItemId)}\t{(c.ItemId == 0 ? "" : c.Count)}\t{c.Reason}");
        }

        // 3. Expired rewards: drop the absolute expiration so the item is permanent.
        foreach (var (id, facts) in plan.Expired.Where(x => !plan.Deleted.ContainsKey(x.Id)))
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

        // 4. Deleted packages: drop their stackable.lst rows (the .stk bodies stay as unlisted files, like the archive's
        //    other unlisted .stk files; removing file entries would rewrite the file and hash tables) and every
        //    reference outside packages whose format is known.
        if (plan.Deleted.Count > 0)
        {
            var deleted = plan.Deleted;
            const string list = "stackable/stackable.lst";
            var lst = engine.Read(list);
            var keptRows = new List<Sym>();
            for (var k = 0; k < lst.Count; k++)
            {
                if (lst[k].Kind == PvfTokenKind.Int32 && k + 1 < lst.Count && lst[k + 1].IsString && deleted.TryGetValue(lst[k].Value, out var round))
                {
                    var package = census.Packages.First(p => p.Id == lst[k].Value);
                    rows.Add($"delete\t{package.Id}\t{package.Path}\t{list}\t\t\tpackage deleted: no reward left after removal (round {round}, {package.Type})");
                    k++;
                    continue;
                }
                keptRows.Add(lst[k]);
            }
            if (lst.Count - keptRows.Count != 2 * deleted.Count) throw new InvalidDataException("stackable.lst rows do not match the deleted packages");
            engine.Replace(list, keptRows, $"#340: {deleted.Count} empty packages deleted");

            foreach (var reference in References)
            {
                var tokens = engine.Read(reference.Path);
                var (kept, removed) = reference.Remove(tokens, deleted.ContainsKey);
                if (removed.Count == 0) continue;
                engine.Replace(reference.Path, kept, $"#340: {removed.Count} references to deleted packages removed");
                foreach (var (id, text) in removed)
                    rows.Add($"reference\t{id}\t{reference.Path}\t{reference.Container}\t{id}\t\tremoved {text}");
            }
        }

        engine.Write(output);

        // Re-audit the written archive with a fresh reader: every category must now be empty.
        using var patched = new Engine(output);
        var patchedCensus = Load(patched);
        var after = Summarize(patchedCensus, Plan(patchedCensus, now), now);
        if (after.MissingRewardOccurrences != 0 || after.UnresolvedPetOccurrences != 0 || after.ExpiredRewardItems != 0 || after.SpeciesToRestore.Length != 0
            || after.UndeclaredAbilityRows.Length != 0 || after.StrayHeaders.Length != 0 || after.DeletedPackages.Length != 0)
            throw new InvalidDataException("Repaired archive still has findings: " + JsonSerializer.Serialize(after, Json));
        // Unlisted .stk files (the deleted packages' own bodies among them) are unreachable and not checked.
        var listed = patchedCensus.Stackable.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var leftovers = ScanReferences(patched.Archive, [.. plan.Deleted.Keys],
            path => path.StartsWith("stackable/", StringComparison.OrdinalIgnoreCase) && path.EndsWith(".stk", StringComparison.OrdinalIgnoreCase) && !listed.Contains(path));
        var unexpected = leftovers.Where(h => !IgnoredReference(h.Path)).ToList();
        if (unexpected.Count != 0)
            throw new InvalidDataException("Deleted packages are still referenced:\n" + string.Join("\n", unexpected.Select(h => $"{h.Id}\t{h.Path}\t{h.Context}")));

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
            RemainingIgnoredReferences = leftovers.Select(h => $"{h.Id}\t{h.Path}\t{h.Context}").ToArray(),
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
            AbilityCases = [],
        };
        var abilityData = Engine.Read(a, "skill/abilitydatas.dat");
        for (var k = 0; k + 1 < abilityData.Count; k++)
        {
            if (!PackageScript.IsTag(abilityData[k]) || abilityData[k].Text != "[ability case]" || abilityData[k + 1].Kind != PvfTokenKind.Int32) continue;
            var end = k + 2;
            while (end < abilityData.Count && !PackageScript.IsTag(abilityData[end])) end++;
            census.AbilityCases[abilityData[k + 1].Value] = AbilityIndexes(abilityData, k + 2, end);
        }
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
            HashSet<int>? abilities = null;
            for (var k = 0; k + 1 < tokens.Count; k++)
            {
                if (!PackageScript.IsTag(tokens[k])) continue;
                var tag = tokens[k].Text;
                if (tag == "[equipment type]" && tokens[k + 1].IsString && tokens[k + 1].Text == "[creature]") pet = true;
                else if (tag == "[creature species]" && tokens[k + 1].Kind == PvfTokenKind.Int32) species = tokens[k + 1].Value;
                else if (tag == "[avatar select ability]")
                {
                    var end = k + 1;
                    while (end < tokens.Count && !PackageScript.IsTag(tokens[end])) end++;
                    (abilities ??= []).UnionWith(AbilityIndexes(tokens, k + 1, end));
                }
                else if (tag == "[ability case index]" && tokens[k + 1].Kind == PvfTokenKind.Int32)
                    (abilities ??= []).UnionWith(census.AbilityCases.GetValueOrDefault(tokens[k + 1].Value, []));
                else if (tag == ExpirationTag && tokens[k + 1].IsString)
                    expirations.Add(DateTime.ParseExact(tokens[k + 1].Text.Trim(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            }
            facts = new(path, pet && census.Equipment.ContainsKey(id) ? species ?? -1 : null, [.. expirations], abilities);
        }
        census.Items[id] = facts;
        return facts;
    }

    /// <summary>
    /// Ability entries are <c>index [TYPE] operands…</c>: an index is an integer directly followed by an upper-case
    /// bracketed type such as <c>[MAGICAL_ATTACK]</c> or <c>[SKILL_LEVEL]</c> (job names in operands are lower case).
    /// </summary>
    private static HashSet<int> AbilityIndexes(IReadOnlyList<Sym> tokens, int from, int to)
    {
        var set = new HashSet<int>();
        for (var k = from; k + 1 < to; k++)
            if (tokens[k].Kind == PvfTokenKind.Int32 && tokens[k + 1].IsString && tokens[k + 1].Text is var t
                && t.StartsWith('[') && t == t.ToUpperInvariant() && t.Any(char.IsLetter))
                set.Add(tokens[k].Value);
        return set;
    }

    private sealed record RepairPlan(
        Func<int, string?> RemoveReason,
        Func<int, int, string?> AbilityReason,
        HashSet<int> MissingIds,
        Dictionary<int, int> UnresolvedPets,
        List<int> RestoredSpecies,
        List<(int Id, ItemFacts Facts)> Expired,
        List<(int Id, ItemFacts Facts)> FutureExpiring,
        Dictionary<int, int> Deleted);

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

        // Packages left without any reward are deleted from the PVF (user decision on #340). A package whose only
        // rewards were deleted packages is then empty too, so deletion repeats until nothing more empties; the value
        // is the round that emptied it (1 = emptied by the category 10/11 removals themselves).
        var deleted = new Dictionary<int, int>();
        string? Reason(int id) =>
            missing.Contains(id) ? "missing from equipment.lst and stackable.lst"
            : unresolved.TryGetValue(id, out var sp) ? $"pet species {sp} not in creature.lst and no source to restore it"
            : deleted.TryGetValue(id, out var round) ? $"package {id} deleted (empty after removal, round {round})"
            : null;
        string? AbilityReason(int id, int ability) =>
            Facts(census, id) is { Abilities: { } declared } && !declared.Contains(ability)
                ? $"avatar ability {ability} is not one the avatar declares" : null;
        for (var round = 1; ; round++)
        {
            var emptied = census.Packages
                .Where(p => !deleted.ContainsKey(p.Id))
                .Where(p => PackageScript.Repair(p.Tokens, Reason, AbilityReason) is { RewardsBefore: > 0, RewardsAfter: 0 })
                .Select(p => p.Id).ToList();
            if (emptied.Count == 0) break;
            foreach (var id in emptied) deleted[id] = round;
        }
        return new(Reason, AbilityReason, missing, unresolved, restored, expired, future, deleted);
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
        string[] DeletedPackages,
        string[] SelectionBlocksEmptied,
        string[] SelectionBlocksBelowSelectionNum,
        string[] UndeclaredAbilityRows,
        string[] StrayHeaders,
        string[] ParseAnomalies);

    private static Report Summarize(Census census, RepairPlan plan, DateTime now)
    {
        var affected = 0; var occurrences = 0; var petOccurrences = 0;
        var abilityRows = new List<string>();
        var emptied = new List<string>(); var emptiedBlocks = new List<string>(); var shortBlocks = new List<string>(); var anomalies = new List<string>();
        foreach (var p in census.Packages)
        {
            var script = PackageScript.Repair(p.Tokens, plan.RemoveReason, plan.AbilityReason);
            var removed = script.Changes.Where(c => c.ItemId != 0 && !c.Reason.StartsWith("marker", StringComparison.Ordinal)).ToList();
            anomalies.AddRange(script.Anomalies.Select(x => $"{p.Id}\t{p.Path}\t{x}"));
            if (removed.Count == 0 && !script.Changes.Any()) continue;
            affected++;
            occurrences += removed.Count(c => plan.MissingIds.Contains(c.ItemId));
            abilityRows.AddRange(removed.Where(c => c.Reason.StartsWith("avatar ability", StringComparison.Ordinal))
                .Select(c => $"{p.Id}\t{p.Path}\t{c.Container}\t{c.ItemId}\t{c.Reason}"));
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
            [.. census.Packages.Where(p => plan.Deleted.ContainsKey(p.Id)).Select(p => $"{p.Id}\t{p.Type}\t{p.Path}\tround {plan.Deleted[p.Id]}")],
            [.. emptiedBlocks],
            [.. shortBlocks],
            [.. abilityRows],
            [.. census.Packages.Where(p => StrayHeader(p.Tokens) >= 0).Select(p => $"{p.Id}\t{p.Path}")],
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

    // ---------------- references to deleted packages outside packages ----------------

    private sealed record Reference(string Path, string Container, Func<List<Sym>, Func<int, bool>, (List<Sym> Kept, List<(int Id, string Text)> Removed)> Remove);

    /// <summary>
    /// Every non-package file that names a deleted package as an item, found by scanning the whole archive for the
    /// deleted ids (2026-11-14, source revised-20261113). Only files without a <c>(r)</c>/<c>(f)</c> prefix are edited:
    /// see <see cref="IgnoredReference"/>.
    /// </summary>
    private static readonly Reference[] References =
    [
        // id, 2, level, name, description, icon: one six-token record per item.
        new("etc/iteminfo.dat", "item record", (t, deleted) => Records(t, 6, r =>
            t[r].Kind == PvfTokenKind.Int32 && t[r + 1].Kind == PvfTokenKind.Int32 && t[r + 2].Kind == PvfTokenKind.Int32
            && t[r + 3].IsString && t[r + 4].IsString && t[r + 5].Kind == PvfTokenKind.Int32, deleted)),
        // [grow type] n, then rows: level, item, count, two message keys.
        new("event/levelupreward.evt", "[grow type]", LevelUpRows),
        // [stackable item] condition item count …
        new("event/eventreward/reward_alljob.evt", "[stackable item]", (t, deleted) => Triples(t, "[stackable item]", deleted)),
        // [reward item] letters item count
        new("event/seriaroomdecoanimation.evt", "[reward item]", (t, deleted) => Triples(t, "[reward item]", deleted)),
    ];

    private static (List<Sym>, List<(int, string)>) Records(List<Sym> t, int stride, Func<int, bool> shape, Func<int, bool> deleted)
    {
        if (t.Count % stride != 0) throw new InvalidDataException($"record file is not {stride}-token records");
        var kept = new List<Sym>(); var removed = new List<(int, string)>();
        for (var r = 0; r < t.Count; r += stride)
        {
            if (!shape(r)) throw new InvalidDataException($"unexpected record at token {r}");
            if (deleted(t[r].Value)) removed.Add((t[r].Value, string.Join(" ", t.Skip(r).Take(stride))));
            else kept.AddRange(t.Skip(r).Take(stride));
        }
        return (kept, removed);
    }

    private static (List<Sym>, List<(int, string)>) Triples(List<Sym> t, string tag, Func<int, bool> deleted)
    {
        var kept = new List<Sym>(); var removed = new List<(int, string)>();
        for (var k = 0; k < t.Count; k++)
        {
            kept.Add(t[k]);
            if (!PackageScript.IsTag(t[k]) || t[k].Text != tag) continue;
            var end = k + 1;
            while (end < t.Count && t[end].Kind == PvfTokenKind.Int32) end++;
            if (end >= t.Count || !PackageScript.IsTag(t[end]) || t[end].Text != "[/" + tag[1..] || (end - k - 1) % 3 != 0)
                throw new InvalidDataException($"{tag} at token {k} is not a closed list of integer triples");
            for (var r = k + 1; r < end; r += 3)
                if (deleted(t[r + 1].Value)) removed.Add((t[r + 1].Value, string.Join(" ", t.Skip(r).Take(3))));
                else kept.AddRange(t.Skip(r).Take(3));
            k = end - 1;
        }
        return (kept, removed);
    }

    private static (List<Sym>, List<(int, string)>) LevelUpRows(List<Sym> t, Func<int, bool> deleted)
    {
        var kept = new List<Sym>(); var removed = new List<(int, string)>();
        for (var k = 0; k < t.Count; k++)
        {
            if (k + 4 < t.Count && t[k].Kind == PvfTokenKind.Int32 && t[k + 1].Kind == PvfTokenKind.Int32 && deleted(t[k + 1].Value)
                && t[k + 2].Kind == PvfTokenKind.Int32 && t[k + 3].IsString && t[k + 4].IsString && t[k + 3].Text.StartsWith("game_server_msg_", StringComparison.Ordinal))
            {
                removed.Add((t[k + 1].Value, string.Join(" ", t.Skip(k).Take(5))));
                k += 4;
                continue;
            }
            kept.Add(t[k]);
        }
        return (kept, removed);
    }

    /// <summary>
    /// Hits the scan finds that are not item references, or are left on purpose:
    /// <c>(r)</c>/<c>(f)</c>-prefixed files are other-region leftovers (Korean names, a <c>(r)cerashop.etc</c> next to the
    /// <c>etc/cerashop.etc</c> the server reads) and are not edited; the .xui panels only show an item icon for an
    /// event the server does not run; the rest are other id spaces where 8556/8557 appear as indexes or skill values.
    /// </summary>
    private static bool IgnoredReference(string path) =>
        path[(path.LastIndexOf('/') + 1)..] is var name && (name.StartsWith("(r)", StringComparison.Ordinal) || name.StartsWith("(f)", StringComparison.Ordinal))
        || path.EndsWith(".xui", StringComparison.Ordinal)
        || IgnoredPaths.Contains(path);

    private static readonly HashSet<string> IgnoredPaths =
    [
        "etc/itemdictionary/baseitemdictionarytable.etc",
        "etc/techimageindexfile1.etc",
        "n_quest/quest.lst",
        "passiveobject/passiveobject.lst",
        "passiveobject/actionobject/monster/action/isadorasun.act",
        "skill/atgunner/g3.skl",
        "skill/gunner/fastdraw.skl",
        "skill/swordman/flowmindtwo.skl",
        "skill/thief/verticalspiralex.skl",
    ];

    private static List<(int Id, string Path, string Context)> ScanReferences(PvfArchive archive, HashSet<int> ids, Func<string, bool>? skip = null)
    {
        var hits = new List<(int, string, string)>();
        var number = new System.Text.RegularExpressions.Regex(@"\d+");
        for (var i = 0; i < archive.FileCount; i++)
        {
            var e = archive.GetEntry(i);
            var path = archive.ResolveString(e.PathOffset) + "/" + archive.ResolveString(e.NameOffset);
            if (skip?.Invoke(path) == true) continue;
            if (e.IsScript)
            {
                var tokens = Engine.Read(archive, path);
                for (var k = 0; k < tokens.Count; k++)
                    if (tokens[k].Kind == PvfTokenKind.Int32 && ids.Contains(tokens[k].Value))
                        hits.Add((tokens[k].Value, path, string.Join(" ", tokens.Skip(Math.Max(0, k - 3)).Take(7))));
            }
            else if (e.IsUnicodeText)
            {
                var text = archive.ReadUnicodeText(i);
                foreach (System.Text.RegularExpressions.Match m in number.Matches(text))
                    if (int.TryParse(m.Value, out var v) && ids.Contains(v)) hits.Add((v, path, "text offset " + m.Index));
            }
        }
        return hits;
    }

    private const int StrayHeaderLength = 7;

    /// <summary>
    /// <c>[booster category num] n [booster selection num] n [booster select category] c1 c2</c> left unclosed right before
    /// <c>[action usable place]</c> (490001919). The client's block tag reader (028A9A70) skips the place names, swallows
    /// <c>[/action usable place]</c> and ends the block, so the place restriction never reaches its top-level handler, while
    /// the complete declarations after it overwrite the leftovers. Removing the leftovers gives the file its evident reading.
    /// </summary>
    private static int StrayHeader(IReadOnlyList<Sym> t)
    {
        bool Tag(int k, string text) => k >= 0 && k < t.Count && PackageScript.IsTag(t[k]) && t[k].Text == text;
        bool Int(int k) => k >= 0 && k < t.Count && t[k].Kind == PvfTokenKind.Int32;
        for (var k = 0; k + 7 < t.Count; k++)
            if (Tag(k, "[booster category num]") && Int(k + 1) && Tag(k + 2, "[booster selection num]") && Int(k + 3)
                && Tag(k + 4, "[booster select category]") && Int(k + 5) && Int(k + 6) && Tag(k + 7, "[action usable place]"))
                return k;
        return -1;
    }

    private static int SelectionNum(List<Sym> tokens)
    {
        for (var k = 0; k + 1 < tokens.Count; k++)
            if (PackageScript.IsTag(tokens[k]) && tokens[k].Text == "[booster selection num]" && tokens[k + 1].Kind == PvfTokenKind.Int32)
                return tokens[k + 1].Value;
        return 0;
    }
}
