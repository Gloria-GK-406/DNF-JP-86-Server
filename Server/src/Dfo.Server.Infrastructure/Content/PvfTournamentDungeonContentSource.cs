using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfTournamentDungeonContentSource(
    PvfArchiveProvider provider, IDungeonMapDefinitionSource maps) : ITournamentDungeonContentSource
{
    private readonly Lock gate = new();
    private readonly Dictionary<int, TournamentDefinition> cached = [];

    public async ValueTask<TournamentDefinition> GetAsync(int dungeonId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dungeonId is not (120 or 121 or 122)) throw new ArgumentOutOfRangeException(nameof(dungeonId));
        lock (gate) if (cached.TryGetValue(dungeonId, out var found)) return found;
        var owned = await maps.ListForDungeonAsync(dungeonId, cancellationToken).ConfigureAwait(false);
        if (owned.Count != 1) throw new InvalidDataException($"Tournament {dungeonId}: expected one arena map.");
        lock (gate)
        {
            if (!cached.TryGetValue(dungeonId, out var definition))
                cached[dungeonId] = definition = Read(provider.Archive, dungeonId, owned[0].MapId);
            return definition;
        }
    }

    private static TournamentDefinition Read(PvfArchive archive, int dungeonId, int mapId)
    {
        var path = Resolve(archive, "dungeon", dungeonId);
        var dungeon = PvfDocument.Open(archive, path);
        Require(dungeon.Read(PvfField.Int32("[tournament dungeon]")) == 1, path, "not a tournament dungeon");
        Require(dungeon.Read(PvfField.Int32("[limit party count]")) == 1, path, "unsupported tournament team size");
        var minimumLevel = dungeon.Read(PvfField.Int32("[minimum required level]", minimum: 1, maximum: 255));
        var basisLevel = dungeon.Read(PvfField.Int32("[basis level]", minimum: 1, maximum: 255));
        var difficulty = dungeon.Read(PvfField.Int32("[designate dungeon difficulty]", minimum: 0, maximum: 4));
        var fatigue = dungeon.Read(PvfField.Int32("[tournament round fatigue]", minimum: 0));
        var withoutFatigue = dungeon.Read(PvfField.Int32("[enter without fatigue]", optional: true));
        var coinLimit = dungeon.Read(PvfField.Int32("[coin limit]", minimum: 0));
        var goldRate = dungeon.Read(PvfField.Number("[tournament clear reward gold rate]"));
        Require(double.IsFinite(goldRate) && goldRate > 0, path, "invalid gold multiplier");
        var requiredItems = new List<DungeonRequiredItem>();
        var required = dungeon.Read(PvfField.Int32List("[required item]", optional: true)) ?? [];
        Require(required.Count % 3 == 0, path, "truncated required item triples");
        for (var index = 0; index < required.Count; index += 3)
        {
            Require(required[index] > 0 && required[index + 1] > 0 && required[index + 2] is 0 or 1, path, "invalid required item");
            requiredItems.Add(new(required[index], required[index + 1], required[index + 2] == 1));
        }
        var experience = dungeon.Read(PvfField.Int32List("[tournament clear reward exp]"));
        Require(experience.Count == 10, path, "expected experience keys 0 through 4");
        var experienceByRound = new uint[5];
        for (var index = 0; index < 5; index++)
        {
            Require(experience[index * 2] == index && experience[index * 2 + 1] >= 0, path, "invalid experience row");
            experienceByRound[index] = (uint)experience[index * 2 + 1];
        }
        var resultCards = ReadResultCards(dungeon, path);
        var rewards = dungeon.Read(PvfField.Int32List("[reward item rate]"));
        Require(rewards.Count > 0 && rewards.Count % 3 == 0, path, "invalid reward item triples");
        var rewardItems = new List<TournamentRewardItem>();
        for (var index = 0; index < rewards.Count; index += 3)
        {
            Require(rewards[index] > 0 && rewards[index + 1] > 0 && rewards[index + 2] > 0, path, "invalid reward item");
            rewardItems.Add(new(rewards[index], rewards[index + 1], rewards[index + 2]));
        }
        Require(rewardItems.Sum(value => (long)value.Weight) <= int.MaxValue, path, "reward weights exceed supported range");
        var mapPath = Resolve(archive, "map", mapId);
        var map = PvfDocument.Open(archive, mapPath);
        Require(map.Read(PvfField.Int32("[dungeon]")) == dungeonId, mapPath, "arena belongs to another dungeon");
        var starts = map.ReadAll(PvfField.Int32List("[tournament start area]"));
        Require(starts.Count == 2 && starts.All(value => value.Count == 4 && value[0] == 1
            && value[1] is >= 0 and <= ushort.MaxValue && value[2] is >= 0 and <= ushort.MaxValue
            && value[3] is >= 0 and <= byte.MaxValue), mapPath, "invalid tournament start areas");
        var candidates = ReadCandidates(archive, map, mapPath, (byte)basisLevel);
        return new(dungeonId, mapId, minimumLevel, basisLevel, (byte)difficulty, fatigue,
            requiredItems.AsReadOnly(), candidates, Array.AsReadOnly(experienceByRound), resultCards,
            rewardItems.AsReadOnly(), goldRate) { CoinLimit = coinLimit, ConsumesFatigue = withoutFatigue != 1 };
    }

    private static IReadOnlyList<TournamentResultCard> ReadResultCards(PvfDocument document, string path)
    {
        // The matrix follows the nested reward-item block's closing tag; a flat field read loses it.
        var sections = document.Sections;
        var start = sections.Select((value, index) => (value, index)).Where(pair => pair.value.Name == "[result card]").ToArray();
        var end = sections.Select((value, index) => (value, index)).Where(pair => pair.value.Name == "[/result card]").ToArray();
        Require(start.Length == 1 && end.Length == 1 && start[0].index < end[0].index, path, "invalid result card block");
        var matrix = new List<PvfToken>();
        var nested = false;
        var nestedSeen = false;
        for (var index = start[0].index; index < end[0].index; index++)
        {
            var section = sections[index];
            if (section.Name == "[reward item rate]")
            {
                Require(!nested && !nestedSeen, path, "duplicate nested reward items");
                nested = nestedSeen = true;
                continue;
            }
            if (section.Name == "[/reward item rate]")
            {
                Require(nested, path, "unexpected reward item closing tag");
                nested = false;
            }
            else Require(section.Name == "[result card]", path, "unexpected result card section");
            Require(!nested, path, "unclosed nested reward items");
            matrix.AddRange(section.Values);
        }
        Require(!nested && nestedSeen && matrix.Count == 24 && matrix.All(value => value.Kind == PvfTokenKind.Int32),
            path, "expected six result-card rows");
        var cards = new List<TournamentResultCard>();
        for (var index = 0; index < 6; index++)
        {
            var row = matrix.Skip(index * 4).Take(4).Select(value => value.Value).ToArray();
            var total = row.Skip(1).Sum(value => (long)value);
            Require(row[0] == index && row.Skip(1).All(value => value >= 0) && total is > 0 and <= int.MaxValue,
                path, "invalid result card weights");
            cards.Add(new(row[0], row[1], row[2], row[3]));
        }
        return cards.AsReadOnly();
    }

    private static IReadOnlyList<TournamentCandidate> ReadCandidates(
        PvfArchive archive, PvfDocument map, string path, byte basisLevel)
    {
        var candidates = new List<TournamentCandidate>();
        foreach (var tokens in map.ReadAll(PvfField.Tokens("[tournament enemies]")))
        {
            Require(tokens.Count >= 2 && tokens[0].Kind == PvfTokenKind.Int32 && tokens[0].Value == 1
                && tokens[1].Kind == PvfTokenKind.QuotedString && (tokens.Count - 2) % 3 == 0,
                path, "invalid tournament enemy group");
            var kind = archive.ResolveString(tokens[1].Value);
            Require(kind is "[monster]" or "[apc]" or "[ai character]", path, "unsupported tournament enemy kind");
            var isAiCharacter = kind != "[monster]";
            for (var index = 2; index < tokens.Count; index += 3)
            {
                Require(tokens[index].Kind == PvfTokenKind.Int32 && tokens[index].Value > 0
                    && tokens[index + 1].Kind == PvfTokenKind.Int32 && tokens[index + 1].Value is > 0 and <= ushort.MaxValue
                    && tokens[index + 2].Kind == PvfTokenKind.QuotedString
                    && !string.IsNullOrWhiteSpace(archive.ResolveString(tokens[index + 2].Value)), path, "invalid tournament enemy");
                var code = tokens[index].Value;
                var level = basisLevel;
                if (isAiCharacter)
                {
                    var aicPath = Resolve(archive, "aicharacter", code);
                    var minimum = PvfDocument.Open(archive, aicPath).Read(PvfField.Tokens("[minimum info]"));
                    Require(minimum.Count == 16 && minimum[5].Kind == PvfTokenKind.Int32
                        && minimum[5].Value is > 0 and <= byte.MaxValue, aicPath, "invalid APC minimum info");
                    level = (byte)minimum[5].Value;
                }
                candidates.Add(new(code, tokens[index + 1].Value, level, isAiCharacter));
            }
        }
        Require(candidates.Count >= 15 && candidates.Select(value => value.Code).Distinct().Count() == candidates.Count,
            path, "insufficient or duplicate tournament candidates");
        return candidates.AsReadOnly();
    }

    private static string Resolve(PvfArchive archive, string directory, int id) =>
        PvfListFile.Read(archive, $"{directory}/{directory}.lst").FirstOrDefault(entry => entry.Id == id) is var entry
        && entry.RelativePath is not null ? $"{directory}/{entry.RelativePath}"
        : throw new InvalidDataException($"PVF '{directory}': missing identifier {id}.");
    private static void Require(bool condition, string path, string reason)
    {
        if (!condition) throw new InvalidDataException($"PVF '{path}': {reason}.");
    }
}
