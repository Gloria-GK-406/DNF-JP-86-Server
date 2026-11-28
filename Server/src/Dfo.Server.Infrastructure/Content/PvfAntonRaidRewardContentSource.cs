using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Raids;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfAntonRaidRewardContentSource(PvfArchiveProvider provider) : IAntonRaidRewardContentSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    private readonly Lazy<AntonRaidRewardDefinition> content = new(() => Read(provider.Archive));

    public string Name => "anton-raid-rewards";

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Read().Phases.Count);

    public AntonRaidRewardDefinition Read() => content.Value;

    internal static AntonRaidRewardDefinition Read(PvfArchive archive)
    {
        const string path = "etc/raid/anton.etc";
        var stackables = PvfListFile.Paths(archive, "stackable/stackable.lst", prefix: "stackable/");
        var sections = PvfDocument.Open(archive, path).Sections;
        var retry = sections.Single(section => section.Name == "[RETRY ITEM]");
        var retryValues = ReadIntegers(retry, 2);
        if (retryValues.Any(value => value <= 0) || !stackables.ContainsKey(retryValues[0]))
            throw Invalid(retry.Name);

        var phases = new List<AntonRaidPhaseRewardDefinition>();
        var open = false;
        var rewardSection = false;
        var rankSection = false;
        List<AntonRaidRankRange> ranks = [];
        List<AntonRaidRewardPool> pools = [];
        foreach (var section in sections)
        {
            switch (section.Name)
            {
                case "[PHASE]":
                    if (open) throw Invalid(section.Name);
                    open = true;
                    ranks = [];
                    pools = [];
                    break;
                case "[/PHASE]":
                    if (!open || rewardSection || rankSection) throw Invalid(section.Name);
                    ValidatePhase(ranks, pools);
                    phases.Add(new(ranks.AsReadOnly(), pools.AsReadOnly()));
                    open = false;
                    break;
                case "[CONDITION FOR RANK]" when open:
                    rankSection = true;
                    break;
                case "[/CONDITION FOR RANK]" when open:
                    rankSection = false;
                    break;
                case "[CLEAR REWARD ITEM]" when open:
                    rewardSection = true;
                    break;
                case "[/CLEAR REWARD ITEM]" when open:
                    rewardSection = false;
                    break;
                case "[DEAD COUNT]" when rankSection:
                    var range = ReadIntegers(section, 3);
                    if (range[0] < 0 || range[1] < range[0] || range[2] is < 0 or > 3)
                        throw Invalid(section.Name);
                    ranks.Add(new(range[0], range[1], (byte)range[2]));
                    break;
                case "[STATE REWARD]" when rewardSection:
                    if (ReadPool(archive, stackables, section) is { } pool)
                        pools.Add(pool);
                    break;
            }
        }
        if (open || phases.Count != 2)
            throw Invalid("[PHASE]");
        return new(phases.AsReadOnly(), new(retryValues[0], retryValues[1]));
    }

    private static AntonRaidRewardPool? ReadPool(PvfArchive archive,
        IReadOnlyDictionary<int, string> stackables, PvfSection row)
    {
        var tokens = row.Values;
        if (tokens.Count != 5 || !tokens[0].IsString || tokens.Skip(1).Any(token => token.Kind != PvfTokenKind.Int32))
            throw Invalid(row.Name);
        var label = archive.ResolveString(tokens[0].Value);
        if (label == "pcroom_card") return null;
        var kind = label switch
        {
            "party_card" => AntonRaidRewardKind.PartyCard,
            "gold" => AntonRaidRewardKind.Gold,
            "squad_item" => AntonRaidRewardKind.SquadItem,
            _ => throw Invalid(row.Name),
        };
        var state = tokens[1].Value;
        var weight = tokens[2].Value;
        var id = tokens[3].Value;
        var flags = tokens[4].Value;
        if (state is not (-1 or 2 or 3) || weight <= 0 || id <= 0 || flags is < 0 or > byte.MaxValue
            || !stackables.TryGetValue(id, out var path))
            throw Invalid(row.Name);

        var document = PvfDocument.Open(archive, path).AtRoot();
        var values = document.Read(PvfField.Int32List("[int data]"));
        if (values.Count == 0 || values.Count % 3 != 0)
            throw Invalid(path);

        var choices = new List<AntonRaidRewardChoice>(values.Count / 3);
        for (var i = 0; i < values.Count; i += 3)
        {
            if (values[i] < 0 || (kind == AntonRaidRewardKind.Gold) != (values[i] == 0)
                || values[i + 1] < 0 || values[i + 2] <= 0)
                throw Invalid(path);
            if (values[i + 1] > 0)
                choices.Add(new(values[i + 1], new(values[i], values[i + 2])));
        }
        if (choices.Count == 0)
            throw Invalid(path);
        return new(kind, state, weight, (byte)flags, choices.AsReadOnly());
    }

    private static void ValidatePhase(List<AntonRaidRankRange> ranks, List<AntonRaidRewardPool> pools)
    {
        ranks.Sort((left, right) => left.Minimum.CompareTo(right.Minimum));
        if (ranks.Count != 4 || ranks[0].Minimum != 0
            || ranks.Select((range, index) => range.Rank == index
                && (index == 0 || (long)ranks[index - 1].Maximum + 1 == range.Minimum)).Any(valid => !valid))
            throw Invalid("[CONDITION FOR RANK]");
        if (pools.Where(pool => pool.Kind == AntonRaidRewardKind.Gold).Select(pool => pool.State)
                .Distinct().Count() != 3
            || !pools.Any(pool => pool.Kind == AntonRaidRewardKind.PartyCard)
            || !pools.Any(pool => pool.Kind == AntonRaidRewardKind.SquadItem)
            || pools.Any(pool => pool.Kind != AntonRaidRewardKind.Gold && pool.State != -1))
            throw Invalid("[CLEAR REWARD ITEM]");
    }

    private static int[] ReadIntegers(PvfSection section, int count)
    {
        if (section.Values.Count != count || section.Values.Any(token => token.Kind != PvfTokenKind.Int32))
            throw Invalid(section.Name);
        return section.Values.Select(token => token.Value).ToArray();
    }

    private static InvalidDataException Invalid(string field) => new($"Invalid Anton raid reward declaration at {field}.");
}
