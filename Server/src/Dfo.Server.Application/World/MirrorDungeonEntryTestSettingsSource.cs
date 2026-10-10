using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Application.World;

internal static class MirrorDungeonEntryTestSettingsSource
{
    public static MirrorDungeonEntryTestSettings From(IConfiguration configuration)
    {
        var section = configuration.GetSection("MirrorDungeon:EntryTest");
        var enabled = ReadBoolean(section, "Enabled", false);
        if (!enabled) return MirrorDungeonEntryTestSettings.Disabled;
        var ids = section.GetSection("DungeonIds").GetChildren().Select(x =>
            int.TryParse(x.Value, out var id) && id > 0 ? id
                : throw new InvalidDataException($"Invalid test dungeon ID at {x.Path}.")).ToArray();
        if (ids.Length is < 1 or > 64 || ids.Distinct().Count() != ids.Length)
            throw new InvalidDataException("MirrorDungeon:EntryTest:DungeonIds requires 1-64 distinct positive IDs.");
        return new(true, Array.AsReadOnly(ids), ReadBoolean(section, "SkipEntryQuota", false));
    }

    private static bool ReadBoolean(IConfiguration section, string key, bool fallback) =>
        section[key] is not { } raw ? fallback : bool.TryParse(raw, out var value) ? value
            : throw new InvalidDataException($"MirrorDungeon:EntryTest:{key} must be true or false.");
}
