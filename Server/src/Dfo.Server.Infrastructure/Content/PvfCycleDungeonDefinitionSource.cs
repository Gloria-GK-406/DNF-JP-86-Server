using Dfo.Pvf.Schema;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfCycleDungeonDefinitionSource(PvfArchiveProvider provider) : ICycleDungeonDefinitionSource
{
    private readonly Lock gate = new();
    private IReadOnlyList<CycleDungeonRegion>? regions;

    public IReadOnlyList<CycleDungeonRegion> Load()
    {
        lock (gate) return regions ??= Read();
    }

    private IReadOnlyList<CycleDungeonRegion> Read()
    {
        const string path = "etc/circledungeoninfo.etc";
        var result = new List<CycleDungeonRegion>();
        var rewards = new Dictionary<int, IReadOnlyList<QuestRewardItemGrant>>();
        int world = 0, open = 0, difficulty = -1;
        bool inRegion = false, inReward = false;
        var pairs = new List<CycleDungeonCandidate>();
        foreach (var section in PvfDocument.Open(provider.Archive, path).Sections)
        {
            var name = section.Name.ToLowerInvariant();
            if (name == "[circle dungeon list info]")
            { inRegion = true; inReward = false; world = open = 0; difficulty = -1; pairs = []; }
            else if (name == "[reward data]")
            { inReward = true; inRegion = false; world = 0; }
            else if (name == "[worldmap index]" && (inRegion || inReward)) world = One(section.Values);
            else if (name == "[open quest]" && inRegion) open = One(section.Values);
            else if (name == "[difficulty level]" && inRegion) difficulty = One(section.Values);
            else if (name == "[dungeon]" && inRegion)
            {
                var values = Integers(section.Values);
                if (values.Length == 0 || values.Length % 2 != 0) throw new InvalidDataException($"{path}: incomplete dungeon/quest pairs.");
                for (var i = 0; i < values.Length; i += 2)
                {
                    if (values[i] <= 0 || values[i + 1] is <= 0 or > ushort.MaxValue)
                        throw new InvalidDataException($"{path}: invalid dungeon/quest pair.");
                    pairs.Add(new(values[i], values[i + 1]));
                }
            }
            else if (name == "[/circle dungeon list info]" && inRegion)
            {
                if (world <= 0 || open <= 0 || difficulty < 0 || pairs.Count == 0)
                    throw new InvalidDataException($"{path}: incomplete region.");
                result.Add(new(world, open, difficulty, pairs.ToArray(), []));
                inRegion = false;
            }
            else if (name == "[reward int data]" && inReward)
            {
                var values = Integers(section.Values);
                if (world <= 0 || values.Length == 0 || values.Length % 2 != 0)
                    throw new InvalidDataException($"{path}: incomplete regional rewards.");
                var items = new List<QuestRewardItemGrant>();
                for (var i = 0; i < values.Length; i += 2)
                {
                    if (values[i] <= 0 || values[i + 1] <= 0) throw new InvalidDataException($"{path}: invalid reward.");
                    items.Add(new(values[i], values[i + 1]));
                }
                if (!rewards.TryAdd(world, items.ToArray())) throw new InvalidDataException($"{path}: duplicate regional reward.");
            }
            else if (name == "[/reward data]") inReward = false;
        }
        if (result.Count == 0 || result.GroupBy(x => x.WorldMapId).Any(x => x.Count() != 1)
            || result.SelectMany(x => x.Candidates).GroupBy(x => x.QuestId).Any(x => x.Count() != 1))
            throw new InvalidDataException($"{path}: empty or ambiguous cycle catalog.");
        return result.Select(region => region with { Rewards = rewards.TryGetValue(region.WorldMapId, out var items)
            ? items : throw new InvalidDataException($"{path}: region lacks rewards.") }).ToArray();
    }

    private static int[] Integers(IReadOnlyList<PvfToken> values) => values.Select(value =>
        value.Kind == PvfTokenKind.Int32 ? value.Value : throw new InvalidDataException("Cycle data must contain integers.")).ToArray();
    private static int One(IReadOnlyList<PvfToken> values) => values.Count == 1
        ? Integers(values)[0] : throw new InvalidDataException("Cycle field must contain one integer.");
}
