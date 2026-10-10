using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfMirrorDungeonDefinitionSource(PvfArchiveProvider provider) : IMirrorDungeonDefinitionSource
{
    private readonly Lock gate = new();
    private MirrorDungeonCatalog? catalog;
    public MirrorDungeonCatalog Load()
    {
        lock (gate) return catalog ??= Read();
    }

    private MirrorDungeonCatalog Read()
    {
        const string path = "etc/crackofdimensionlist.etc";
        var archive = provider.Archive;
        var document = PvfDocument.Open(archive, path);
        var minimum = One(document, "[limit min level]", path);
        var entryLimit = One(document, "[daily enter count]", path);
        var clearLimit = One(document, "[daily clear count]", path);
        var daily = Ints(document, "[daily reward info]", path);
        var pairs = Ints(document, "[crack info list]", path);
        if (minimum is <= 0 or > byte.MaxValue || entryLimit <= 0 || clearLimit <= 0
            || daily.Length != 3 || daily.Any(x => x <= 0) || pairs.Length == 0 || pairs.Length % 2 != 0)
            throw new InvalidDataException($"{path}: incomplete mirror configuration.");
        var candidates = new List<MirrorDungeonCandidate>();
        for (var i = 0; i < pairs.Length; i += 2)
        {
            if (pairs[i] <= 0 || pairs[i + 1] is <= 0 or > ushort.MaxValue)
                throw new InvalidDataException($"{path}: invalid mirror pair.");
            candidates.Add(new(pairs[i], pairs[i + 1]));
        }
        if (candidates.GroupBy(x => x.QuestId).Any(x => x.Count() != 1))
            throw new InvalidDataException($"{path}: ambiguous mirror quest mapping.");
        var rewards = new List<MirrorDungeonRewardBand>();
        var inReward = false;
        int low = 0, high = 0;
        IReadOnlyList<QuestRewardItemGrant> items = [];
        foreach (var section in document.Sections)
        {
            switch (section.Name)
            {
                case "[reward data]": inReward = true; low = high = 0; items = []; break;
                case "[min level]" when inReward: low = Scalar(section, path); break;
                case "[max level]" when inReward: high = Scalar(section, path); break;
                case "[reward int data]" when inReward:
                    var values = Ints(section, path);
                    if (values.Length == 0 || values.Length % 2 != 0 || values.Any(x => x <= 0))
                        throw new InvalidDataException($"{path}: invalid mirror rewards.");
                    items = Enumerable.Range(0, values.Length / 2)
                        .Select(i => new QuestRewardItemGrant(values[i * 2], values[i * 2 + 1])).ToArray();
                    break;
                case "[/reward data]" when inReward:
                    if (low <= 0 || high < low || items.Count == 0)
                        throw new InvalidDataException($"{path}: incomplete mirror reward band.");
                    rewards.Add(new(low, high, items)); inReward = false; break;
            }
        }
        if (inReward || rewards.Count == 0) throw new InvalidDataException($"{path}: missing mirror rewards.");

        // Resolve A21's semantic resource names through their lists instead of fixing numeric IDs.
        var wrapper = Listed("dungeon", "Crackofdimension/Crackofdimension.dgn");
        var wrapperDocument = PvfDocument.Open(archive, wrapper.Path);
        if (One(wrapperDocument, "[crack of dimension dungeon]", wrapper.Path) != 1)
            throw new InvalidDataException($"{wrapper.Path}: not a mirror wrapper.");
        minimum = Math.Max(minimum, One(wrapperDocument, "[minimum required level]", wrapper.Path));
        var world = Listed("worldmap", "MirrorArad.wdm");
        var worldDocument = PvfDocument.Open(archive, world.Path);
        if (!Ints(worldDocument, "[dungeon]", world.Path).Contains(wrapper.Id))
            throw new InvalidDataException($"{world.Path}: mirror wrapper missing from selection.");
        var town = Listed("town", "WestCoast.twn");
        var townDocument = PvfDocument.Open(archive, town.Path);
        int area = -1, entryArea = -1;
        var quests = new List<int>();
        IReadOnlyList<int> required = [];
        foreach (var section in townDocument.Sections)
        {
            if (section.Name == "[area]")
            {
                area = section.Values.Count > 0 && section.Values[0].Kind == PvfTokenKind.Int32
                    ? section.Values[0].Value : -1;
                quests = [];
            }
            else if (section.Name == "[need quest]") quests.AddRange(Ints(section, town.Path).Where(x => x > 0));
            // Town area kinds are quoted values after the permission block, not section tags.
            for (var index = 0; index + 1 < section.Values.Count; index++)
                if (section.Values[index].Kind == PvfTokenKind.QuotedString
                    && archive.ResolveString(section.Values[index].Value) == "[dungeon gate]"
                    && section.Values[index + 1].Kind == PvfTokenKind.Int32
                    && section.Values[index + 1].Value == world.Id)
                { entryArea = area; required = quests.ToArray(); }
        }
        if (town.Id is < 0 or > byte.MaxValue || entryArea is < 0 or > byte.MaxValue
            || minimum > byte.MaxValue || required.Count == 0)
            throw new InvalidDataException($"{town.Path}: mirror entry area or prerequisites missing.");
        return new(wrapper.Id, world.Id, (byte)town.Id, (byte)entryArea, (byte)minimum,
            entryLimit, clearLimit, new(daily[0], daily[1], daily[2]), rewards.ToArray(), candidates.ToArray())
            { RequiredQuestIds = required };
    }

    private (int Id, string Path) Listed(string directory, string relative)
    {
        var matches = PvfListFile.Read(provider.Archive, $"{directory}/{directory}.lst")
            .Where(x => x.Item2.Equals(relative, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException($"Ambiguous or missing mirror resource {directory}/{relative}.");
        return (matches[0].Item1, $"{directory}/{matches[0].Item2}");
    }

    private int[] Ints(PvfSection section, string path) => PvfField.Int32List(section.Name, optional: true)
        .Decode(provider.Archive, section.Values, path).ToArray();
    private int[] Ints(PvfDocument document, string name, string path)
    {
        var sections = document.Sections.Where(x => x.Name == name).ToArray();
        if (sections.Length != 1) throw new InvalidDataException($"{path}: missing or duplicate {name}.");
        return Ints(sections[0], path);
    }
    private int Scalar(PvfSection section, string path)
    {
        var values = Ints(section, path);
        return values.Length == 1 ? values[0] : throw new InvalidDataException($"{path}: {section.Name} must be scalar.");
    }
    private int One(PvfDocument document, string name, string path)
    {
        var values = Ints(document, name, path);
        return values.Length == 1 ? values[0] : throw new InvalidDataException($"{path}: {name} must be scalar.");
    }
}
