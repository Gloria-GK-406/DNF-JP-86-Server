using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;

namespace GmIconPack;

/// <summary>One item to draw: its [icon] (img path + frame), or why it has none.</summary>
internal sealed record IconRequest(int ItemId, string? Img, int Frame, IconFailure? Failure = null);

/// <summary>What the PVF scan found, before any IMG is opened.</summary>
internal sealed record ItemScan(IReadOnlyList<IconRequest> Requests, int Equipment, int Stackables, int ExcludedAvatars, int ExcludedCreatures);

/// <summary>Reads equipment.lst / stackable.lst and each listed script's root [icon] and [equipment type].</summary>
internal static class PvfItemCatalog
{
    private const string EquipmentList = "equipment/equipment.lst";
    private const string StackableList = "stackable/stackable.lst";

    /// <param name="includeAll">Also take avatar and creature equipment, which the default scope leaves out.</param>
    public static ItemScan Scan(PvfArchive archive, bool includeAll)
    {
        var requests = new List<IconRequest>();
        int equipment = 0, stackables = 0, avatars = 0, creatures = 0;
        foreach (var (id, path) in ReadList(archive, EquipmentList))
        {
            var document = Open(archive, path);
            if (!includeAll && document is not null)
            {
                switch (Classify(EquipmentType(archive, document)))
                {
                    case EquipmentClass.Avatar: avatars++; continue;
                    case EquipmentClass.Creature: creatures++; continue;
                }
            }
            equipment++;
            requests.Add(Request(archive, id, document));
        }
        foreach (var (id, path) in ReadList(archive, StackableList))
        {
            stackables++;
            requests.Add(Request(archive, id, Open(archive, path)));
        }
        return new(requests, equipment, stackables, avatars, creatures);
    }

    internal enum EquipmentClass { Equipment, Avatar, Creature }

    /// <summary>
    /// Mirrors PvfEntryLifecycleContentSource.ClassifyEquipmentType: backticks stripped, trimmed, lowercased;
    /// "[creature]" is the contract creature, anything ending "avatar]" is an avatar. Artifacts stay equipment.
    /// </summary>
    internal static EquipmentClass Classify(string? equipmentType)
    {
        var normalized = (equipmentType ?? string.Empty).Replace("`", string.Empty).Trim().ToLowerInvariant();
        if (normalized == "[creature]") return EquipmentClass.Creature;
        return normalized.EndsWith("avatar]", StringComparison.Ordinal) ? EquipmentClass.Avatar : EquipmentClass.Equipment;
    }

    private static IconRequest Request(PvfArchive archive, int id, PvfDocument? document)
    {
        if (document is null) return new(id, null, 0, IconFailure.NoScript);
        var icon = document.Sections.FirstOrDefault(section => section.Name == "[icon]");
        if (icon is null || icon.Values.Count < 2 || !icon.Values[0].IsString || icon.Values[1].Kind != PvfTokenKind.Int32)
            return new(id, null, 0, IconFailure.NoIconSection);
        var img = archive.ResolveString(icon.Values[0].Value).Trim('`').Trim();
        return img.Length == 0 ? new(id, null, 0, IconFailure.NoIconSection) : new(id, img, icon.Values[1].Value);
    }

    private static string? EquipmentType(PvfArchive archive, PvfDocument document)
    {
        var type = document.Sections.FirstOrDefault(section => section.Name == "[equipment type]");
        return type is { Values.Count: > 0 } && type.Values[0].IsString ? archive.ResolveString(type.Values[0].Value) : null;
    }

    // Root sections only: the same tags can recur inside conditional blocks.
    private static PvfDocument? Open(PvfArchive archive, string path)
    {
        var index = archive.FindFile(path);
        if (index < 0 || !archive.GetEntry(index).IsScript) return null;
        try { return PvfDocument.Open(archive, path).AtRoot(); }
        catch (InvalidDataException) { return null; }
    }

    /// <summary>A .lst is a flat run of (id, path) pairs; paths are backtick-quoted and relative to the list's folder.</summary>
    internal static List<(int Id, string Path)> ReadList(PvfArchive archive, string listPath)
    {
        var folder = listPath[..(listPath.LastIndexOf('/') + 1)];
        var entries = new List<(int, string)>();
        var reader = archive.OpenScript(listPath);
        int? pending = null;
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Int32) { pending = token.Value; continue; }
            if (!token.IsString || pending is null) continue;
            entries.Add((pending.Value, folder + archive.ResolveString(token.Value).Trim('`').Replace('\\', '/')));
            pending = null;
        }
        return entries;
    }
}
