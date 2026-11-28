using System.Collections.ObjectModel;
using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfGuildDungeonContentSource(PvfArchiveProvider provider, PvfContentOptions options) : IGuildDungeonContentSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private const string Directory = "etc/guild/guilddungeon/";
    private const string StyleList = Directory + "guilddungeonstyle.lst";
    private readonly Lazy<GuildDungeonCatalog?> catalog = new(() =>
        options.ResolveArchivePath() is null ? null : Load(provider.Archive));

    public string Name => "guild-dungeons";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Read() is null ? 0 : 1);

    public GuildDungeonCatalog? Read() => catalog.Value;

    private static GuildDungeonCatalog? Load(PvfArchive archive)
    {
        if (archive.FindFile(StyleList) < 0) return null;
        var styles = PvfListFile.Paths(archive, StyleList);
        PvfField.Require(styles.Count == 1, StyleList, "style", "expected a single style");
        var (style, file) = styles.Single();
        PvfField.Require(style >= 0 && !file.Contains("..", StringComparison.Ordinal)
            && !file.StartsWith('/'), StyleList, "style", "invalid style path");
        var path = Directory + file.Replace('\\', '/');
        var document = PvfDocument.Open(archive, path);
        var dungeons = new Dictionary<int, GuildDungeonDefinition>();
        foreach (var dungeon in Blocks(document.Sections, "[dungeon]", "[/dungeon]", path))
        {
            var rewards = Blocks(dungeon, "[gauge ratio]", "[/gauge ratio]", path).Select(reward =>
            {
                var item = Numbers(reward, "[item]", archive, path);
                var ratio = Numbers(reward, "[gauge ratio]", archive, path).Single();
                var contribution = Numbers(reward, "[contribute]", archive, path).Single();
                PvfField.Require(item.Count == 2 && item[0] > 0 && item[1] > 0 && ratio is >= 0 and <= 100
                    && contribution >= 0, path, "reward", "invalid guild dungeon reward");
                return new GuildDungeonReward(ratio, item[0], item[1], contribution);
            }).OrderBy(reward => reward.MaximumRatio).ToArray();
            var id = Numbers(dungeon, "[dungeon index]", archive, path).Single();
            var limit = Numbers(dungeon, "[gauge limit]", archive, path).Single();
            var gain = Numbers(dungeon, "[gauge on clear]", archive, path).Single();
            var conquest = Numbers(dungeon, "[conquest gauge ratio]", archive, path).Single();
            var prerequisites = Numbers(dungeon, "[required dungeon conquest]", archive, path, optional: true);
            PvfField.Require(id > 0 && limit > 0 && gain > 0 && conquest is >= 0 and <= 100
                && prerequisites.Distinct().Count() == prerequisites.Count
                && rewards.Length > 0 && rewards[^1].MaximumRatio == 100
                && rewards.Select(reward => reward.MaximumRatio).Distinct().Count() == rewards.Length,
                path, "dungeon", "invalid entry");
            PvfField.Require(dungeons.TryAdd(id, new(id, limit, gain, conquest, prerequisites, rewards)),
                path, "dungeon", "duplicate dungeon id");
        }

        PvfField.Require(dungeons.Count > 0, path, "dungeon", "missing dungeon entries");
        var pending = new HashSet<int>(dungeons.Keys);
        while (pending.Count > 0)
            PvfField.Require(pending.RemoveWhere(id =>
                dungeons[id].Prerequisites.All(prerequisite => dungeons.ContainsKey(prerequisite) && !pending.Contains(prerequisite))) > 0,
                path, "dungeon", "invalid or cyclic conquest prerequisite");
        var repeatReward = Numbers(document.Sections, "[repeat clear bonus reward]", archive, path, optional: true);
        GuildDungeonRepeatBonus? repeatBonus = null;
        if (repeatReward.Count > 0)
        {
            var levelDifference = Numbers(document.Sections, "[level difference]", archive, path);
            PvfField.Require(repeatReward.Count == 2 && repeatReward[0] > 0 && repeatReward[1] > 0
                && levelDifference.Count == 1 && levelDifference[0] > 0, path, "repeat bonus", "invalid reward or level difference");
            repeatBonus = new(repeatReward[0], repeatReward[1], levelDifference[0]);
        }
        var buffEntries = Numbers(document.Sections, "[party buff list]", archive, path, optional: true);
        PvfField.Require(buffEntries.Count % 2 == 0, path, "party buff list", "expected party size and buff ID pairs");
        var partyBuffs = new Dictionary<int, int>();
        for (var index = 0; index < buffEntries.Count; index += 2)
        {
            var size = buffEntries[index];
            var buffId = buffEntries[index + 1];
            PvfField.Require(size is >= 2 and <= 4 && buffId > 0 && partyBuffs.TryAdd(size, buffId),
                path, "party buff list", "invalid or repeated party size or invalid buff ID");
        }
        return new(style, new ReadOnlyDictionary<int, GuildDungeonDefinition>(dungeons))
        {
            RepeatBonus = repeatBonus,
            PartyBuffs = new ReadOnlyDictionary<int, int>(partyBuffs),
        };
    }

    private static IReadOnlyList<int> Numbers(IReadOnlyList<PvfSection> sections, string heading,
        PvfArchive archive, string path, bool optional = false)
    {
        var field = PvfField.Int32List(heading, optional: optional);
        var section = PvfSections.Single(sections, field, PvfRepeats.AsUndeclared, path);
        return section is null ? [] : field.Decode(archive, section.Values, path);
    }

    private static IEnumerable<PvfSection[]> Blocks(IReadOnlyList<PvfSection> sections, string open, string close, string path)
    {
        var start = -1;
        for (var index = 0; index < sections.Count; index++)
        {
            if (sections[index].Name == open)
            {
                PvfField.Require(start < 0, path, open, "unexpected nested block");
                start = index;
            }
            else if (sections[index].Name == close)
            {
                PvfField.Require(start >= 0, path, close, "unexpected closing section");
                yield return sections.Skip(start).Take(index - start).ToArray();
                start = -1;
            }
        }
        PvfField.Require(start < 0, path, open, "unclosed block");
    }
}
