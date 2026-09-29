using System;
using System.Linq;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;

using var archive = PvfArchive.Open(args[0]);
if (args.Length > 1 && args[1] == "--scan-hunger")
{
    var list = archive.OpenScript("creature/creature.lst");
    var paths = new System.Collections.Generic.List<(int Id, string Path)>();
    int? id = null;
    while (list.Read(out var token))
    {
        if (token.Kind == PvfTokenKind.Int32) id = token.Value;
        else if (id is not null && token.IsString)
        {
            paths.Add((id.Value, "creature/" + archive.ResolveString(token.Value)));
            id = null;
        }
    }
    var selective = archive.GetTag("[selective grow creature]").Offset;
    var usingExp = archive.GetTag("[using exp item]").Offset;
    var a = archive.GetTag("[grow Atype item index]").Offset;
    var b = archive.GetTag("[grow Btype item index]").Offset;
    var count = 0;
    foreach (var (creatureId, path) in paths)
    {
        var reader = archive.OpenScript(path);
        var sections = new System.Collections.Generic.HashSet<int>();
        while (reader.Read(out var token))
            if (token.Kind == PvfTokenKind.Identifier &&
                (token.Value == selective || token.Value == usingExp || token.Value == a || token.Value == b))
                sections.Add(token.Value);
        if (sections.Count == 0) continue;
        Console.WriteLine($"GROW {creatureId} {path} selective={sections.Contains(selective)} usingExp={sections.Contains(usingExp)} A={sections.Contains(a)} B={sections.Contains(b)}");
        count++;
    }
    Console.WriteLine($"CREATURE-SCRIPTS {paths.Count}; GROW-DECLARATIONS {count}");
    return;
}
if (args.Length > 1 && args[1] == "--scan-creatures")
{
    var list = archive.OpenScript("equipment/equipment.lst");
    var ids = new System.Collections.Generic.List<(int Id, string Path)>();
    int? id = null;
    while (list.Read(out var token))
    {
        if (token.Kind == PvfTokenKind.Int32) id = token.Value;
        else if (id is not null && token.IsString)
        {
            var path = archive.ResolveString(token.Value);
            if (path.StartsWith("creature/", StringComparison.OrdinalIgnoreCase))
                ids.Add((id.Value, "equipment/" + path));
            id = null;
        }
    }
    var experienceTag = archive.GetTag("[creature experience amount rate]").Offset;
    var speciesTag = archive.GetTag("[creature species]").Offset;
    var selectiveTag = archive.GetTag("[selective grow creature]").Offset;
    var countBonus = 0;
    foreach (var (itemId, path) in ids)
    {
        var script = archive.OpenScript(path);
        var section = -1;
        while (script.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier) { section = token.Value; continue; }
            if (section == speciesTag && token.Kind == PvfTokenKind.Int32 && token.Value == 122)
                Console.WriteLine($"SPECIES-122 {itemId} {path}");
            if (section == experienceTag)
            {
                var value = token.Kind == PvfTokenKind.Single ? token.AsSingle().ToString("R") : token.Value.ToString();
                Console.WriteLine($"BONUS {itemId} {path} {value}");
                countBonus++;
                break;
            }
        }
    }
    Console.WriteLine($"CREATURE-EQUIPMENT {ids.Count}; BONUS-DECLARATIONS {countBonus}; SELECTIVE-TAG-OFFSET {selectiveTag}");
    return;
}
foreach (var path in args.Skip(1))
{
    Console.WriteLine(path);
    if (archive.FindFile(path) < 0) { Console.WriteLine("MISSING"); continue; }
    var reader = archive.OpenScript(path);
    var count = 0;
    while (reader.Read(out var token) && count++ < 250)
    {
        var value = token.Kind switch
        {
            PvfTokenKind.Int32 => token.Value.ToString(),
            PvfTokenKind.Single => token.AsSingle().ToString("R"),
            _ => archive.ResolveString(token.Value),
        };
        Console.WriteLine($"{token.Kind}: {value}");
    }
}
