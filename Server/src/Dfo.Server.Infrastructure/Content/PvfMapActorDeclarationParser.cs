using Dfo.Pvf;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Decodes MAP actor declarations without deciding when they become live actors.</summary>
internal static class PvfMapActorDeclarationParser
{
    public static DungeonMapActorDeclarations Parse(PvfArchive archive, PvfDocument document, string path) =>
        ParseSections(document.Sections, archive.ResolveString, path);

    internal static DungeonMapActorDeclarations ParseSections(
        IReadOnlyList<PvfSection> sections, Func<int, string> resolve, string path)
    {
        var actors = new List<DungeonActorSpawnDeclaration>();
        var supplements = new List<DungeonActorDeclarationSupplement>();
        var indexes = new Dictionary<DungeonActorSpawnSource, int>();
        var parentIndex = 0;
        var teams = new List<int>();
        foreach (var section in sections)
        {
            var name = section.Name.ToLowerInvariant();
            var cursor = new Cursor(section.Values, resolve, path, section.Name);
            switch (name)
            {
                case "[monster]":
                case "[monster condition]":
                case "[conditional summon monster]":
                    var source = name switch
                    {
                        "[monster]" => DungeonActorSpawnSource.Monster,
                        "[monster condition]" => DungeonActorSpawnSource.ConditionalMonster,
                        _ => DungeonActorSpawnSource.ConditionalSummon,
                    };
                    while (!cursor.End)
                    {
                        cursor.Record = indexes.GetValueOrDefault(source);
                        actors.Add(ParseMonster(cursor, source));
                        indexes[source] = cursor.Record + 1;
                    }
                    break;
                case "[ai character]":
                    while (!cursor.End)
                    {
                        cursor.Record = indexes.GetValueOrDefault(DungeonActorSpawnSource.AiCharacter);
                        actors.Add(ParseAiCharacter(cursor));
                        indexes[DungeonActorSpawnSource.AiCharacter] = cursor.Record + 1;
                    }
                    break;
                case "[special passive object]":
                    ParseObjects(cursor, actors, supplements, ref parentIndex,
                        indexes.GetValueOrDefault(DungeonActorSpawnSource.SpecialPassiveObject));
                    indexes[DungeonActorSpawnSource.SpecialPassiveObject] =
                        actors.Count(actor => actor.Source == DungeonActorSpawnSource.SpecialPassiveObject);
                    supplements.Add(Supplement(section, resolve, path));
                    break;
                case "[monster team]":
                    while (!cursor.End) teams.Add(cursor.Int());
                    supplements.Add(Supplement(section, resolve, path));
                    break;
                case "[blood monster]":
                case "[ultimate monster]":
                    ValidateIntegerRows(cursor, 10);
                    supplements.Add(Supplement(section, resolve, path));
                    break;
                case "[attacked monster info]":
                    ValidateIntegerRows(cursor, 9);
                    supplements.Add(Supplement(section, resolve, path));
                    break;
                case "[monster spawn pos]":
                    var count = cursor.NonnegativeInt("position count");
                    cursor.Require(count <= cursor.Remaining / 3 && cursor.Remaining == count * 3,
                        "position count does not match XYZ triples");
                    ValidateIntegerRows(cursor, 3);
                    supplements.Add(Supplement(section, resolve, path));
                    break;
                case "[event monster position]":
                    ValidateIntegerRows(cursor, 3);
                    supplements.Add(Supplement(section, resolve, path));
                    break;
                default:
                    if (name.StartsWith("[/", StringComparison.Ordinal)
                        && (name.Contains("monster", StringComparison.Ordinal)
                            || name == "[/ai character]" || name == "[/special passive object]"))
                        cursor.Require(cursor.End, "closing marker must not have values");
                    else if (name.Contains("monster", StringComparison.Ordinal))
                        supplements.Add(Supplement(section, resolve, path));
                    break;
            }
        }

        for (var index = 0; index < actors.Count; index++)
        {
            var actor = actors[index];
            if (actor.Source != DungeonActorSpawnSource.Monster || actor.RecordIndex >= teams.Count)
                continue;
            var team = teams[actor.RecordIndex];
            actors[index] = actor with { MonsterTeam = team };
        }
        return new(actors.AsReadOnly(), supplements.AsReadOnly());
    }

    private static DungeonActorSpawnDeclaration ParseMonster(Cursor cursor, DungeonActorSpawnSource source)
    {
        var values = cursor.Ints(8);
        cursor.Require(values[0] > 0, "monster id must be positive");
        cursor.Require(values[6] is >= 0 and <= 127 && values[7] >= 0,
            "invalid random or fixed drop count");
        var placement = cursor.Word();
        cursor.Require(placement is "[fixed]" or "[ratio]" or "[half]", $"unknown placement '{placement}'");
        var (rank, dummy, npc) = ParseRankAndModifiers(cursor);
        var (features, hasCount) = ParseFeatures(cursor, rank, allowMissing: true);
        var position = new DungeonSpawnPosition(values[3], values[4], values[5]);
        IReadOnlyList<int> parameters = [];
        if (source == DungeonActorSpawnSource.ConditionalSummon)
        {
            parameters = Array.AsReadOnly(values[3..6]);
            var coordinates = cursor.Ints(3);
            position = new(coordinates[0], coordinates[1], coordinates[2]);
        }
        return new(values[0], source, cursor.Record)
        {
            RelativeLevel = values[1] != 0, LevelValue = values[2], Position = position,
            Placement = placement, Rank = rank, IsDummy = dummy, BoundNpcId = npc,
            ChampionFeatures = features, HasChampionFeatureCount = hasCount,
            RandomDropCount = values[6], FixedDropCount = values[7], Parameters = parameters,
        };
    }

    private static DungeonActorSpawnDeclaration ParseAiCharacter(Cursor cursor)
    {
        var values = cursor.Ints(4);
        cursor.Require(values[0] > 0, "AI character id must be positive");
        var factionWord = cursor.Word();
        var faction = factionWord switch
        {
            "[character]" => DungeonActorFaction.Character,
            "[monster]" => DungeonActorFaction.Monster,
            "[neutral]" => DungeonActorFaction.Neutral,
            _ => throw cursor.Error($"unknown APC faction '{factionWord}'"),
        };
        var (rank, dummy, npc) = ParseRankAndModifiers(cursor);
        // Native ScanChampionFeatures precedes the two trailing APC parameters.
        var (features, hasCount) = ParseFeatures(cursor, rank, allowMissing: false);
        var tail = cursor.Ints(2);
        return new(values[0], DungeonActorSpawnSource.AiCharacter, cursor.Record)
        {
            Position = new(values[1], values[2], 0), Rank = rank, Faction = faction,
            IsDummy = dummy, BoundNpcId = npc, ChampionFeatures = features,
            HasChampionFeatureCount = hasCount,
            Parameters = Array.AsReadOnly(new[] { values[3], tail[0], tail[1] }),
        };
    }

    private static (byte Rank, bool Dummy, int? Npc) ParseRankAndModifiers(Cursor cursor)
    {
        var dummy = false;
        int? npc = null;
        while (true)
        {
            var word = cursor.Word();
            switch (word)
            {
                case "[dummy]":
                    cursor.Require(!dummy, "duplicate dummy modifier");
                    dummy = true;
                    continue;
                case "[npc]":
                    cursor.Require(npc is null, "duplicate NPC binding");
                    npc = cursor.Int();
                    cursor.Require(npc > 0, "NPC id must be positive");
                    continue;
                case "[normal]": return (0, dummy, npc);
                case "[champion]": return (1, dummy, npc);
                case "[super champion]": return (2, dummy, npc);
                case "[boss]": return (3, dummy, npc);
                default: throw cursor.Error($"unknown monster rank or modifier '{word}'");
            }
        }
    }

    private static (IReadOnlyList<string> Features, bool HasCount) ParseFeatures(
        Cursor cursor, byte rank, bool allowMissing)
    {
        if (rank is not (1 or 2)) return ([], false);
        // A complete next monster prefix identifies an omitted count without stealing its ID.
        if (allowMissing && (cursor.End || cursor.HasMonsterPrefix())) return ([], false);
        var count = cursor.NonnegativeInt("champion feature count");
        cursor.Require(count <= cursor.Remaining, "champion feature count exceeds remaining tokens");
        var features = new string[count];
        for (var index = 0; index < count; index++) features[index] = cursor.Text();
        return (Array.AsReadOnly(features), true);
    }

    private static void ParseObjects(Cursor cursor, List<DungeonActorSpawnDeclaration> actors,
        List<DungeonActorDeclarationSupplement> supplements, ref int parentIndex, int recordIndex)
    {
        while (!cursor.End)
        {
            cursor.Record = parentIndex;
            // Older MAPs append a hellparty table after the last object's declared actions.
            if (cursor.HasWord("[hellparty]"))
            {
                cursor.Word();
                ParseHellParty(cursor, supplements);
                cursor.Require(cursor.End, "standalone hellparty must be the trailing object declaration");
                break;
            }
            var header = cursor.Ints(5);
            // Actual MAPs include zero-code empty slots and -1 virtual trap parents.
            // They retain an object index but must never become monster template parents.
            cursor.Require(header[0] >= -1 && header[4] >= 0, "invalid object id or action count");
            cursor.Require(header[0] != 0 || header[4] == 0, "zero-code object must be an empty slot");
            cursor.Require(header[4] <= cursor.Remaining, "action count exceeds remaining tokens");
            for (var child = 0; child < header[4]; child++)
            {
                var kind = cursor.Word();
                if (kind == "[hellparty]")
                {
                    ParseHellParty(cursor, supplements);
                    continue;
                }
                cursor.Require(kind is "[monster]" or "[item]" or "[trap]" or "[quest]",
                    $"unknown special-object action '{kind}'");
                var values = cursor.Ints(5);
                if (kind != "[monster]") continue;
                cursor.Require(header[0] > 0, "monster template requires a positive parent object id");
                cursor.Require(values[0] > 0, "object monster id must be positive");
                actors.Add(new(values[0], DungeonActorSpawnSource.SpecialPassiveObject, recordIndex++)
                {
                    LevelValue = values[1], Position = new(header[1], header[2], header[3]),
                    ParentObjectIndex = parentIndex, ParentObjectCode = header[0], ChildIndex = child,
                    Parameters = Array.AsReadOnly(values[2..5]),
                });
            }
            parentIndex++;
        }
    }

    private static void ParseHellParty(Cursor cursor, List<DungeonActorDeclarationSupplement> supplements)
    {
        var values = new List<DungeonDeclarationValue>();
        while (!cursor.End && cursor.IsInteger) values.Add(new(cursor.Int(), null));
        cursor.Require(values.Count % 3 == 0, "hellparty entries require group/rate/order triples");
        cursor.Require(cursor.Word() == "[/hellparty]", "missing hellparty closing marker");
        supplements.Add(new("[hellparty]", values.AsReadOnly()));
    }

    private static void ValidateIntegerRows(Cursor cursor, int width)
    {
        cursor.Require(cursor.Remaining % width == 0, $"expected complete {width}-integer records");
        while (!cursor.End)
        {
            cursor.Ints(width);
            cursor.Record++;
        }
    }

    private static DungeonActorDeclarationSupplement Supplement(PvfSection section, Func<int, string> resolve, string path)
    {
        var cursor = new Cursor(section.Values, resolve, path, section.Name);
        var values = new List<DungeonDeclarationValue>();
        while (!cursor.End)
        {
            if (cursor.IsInteger) values.Add(new(cursor.Int(), null));
            else values.Add(new(null, cursor.Text()));
        }
        return new(section.Name, values.AsReadOnly());
    }

    private sealed class Cursor(IReadOnlyList<PvfToken> tokens, Func<int, string> resolve, string path, string section)
    {
        private int at;
        public int Record { get; set; }
        public bool End => at == tokens.Count;
        public int Remaining => tokens.Count - at;
        public bool IsInteger => !End && tokens[at].Kind == PvfTokenKind.Int32;
        public InvalidDataException Error(string reason) =>
            new($"PVF '{path}', section '{section}', record {Record}, token {at}: {reason}.");
        public void Require(bool valid, string reason) { if (!valid) throw Error(reason); }
        public int Int()
        {
            Require(IsInteger, "expected Int32");
            return tokens[at++].Value;
        }
        public int NonnegativeInt(string label)
        {
            var value = Int();
            Require(value >= 0, $"negative {label}");
            return value;
        }
        public int[] Ints(int count)
        {
            var result = new int[count];
            for (var index = 0; index < count; index++) result[index] = Int();
            return result;
        }
        public string Text()
        {
            Require(!End && tokens[at].IsString, "expected string token");
            var value = resolve(tokens[at++].Value);
            Require(!string.IsNullOrWhiteSpace(value), "empty string token");
            return value;
        }
        public string Word() => Text().Trim().Trim('`', '"').ToLowerInvariant();
        public bool HasWord(string word) => !End && tokens[at].IsString
            && string.Equals(resolve(tokens[at].Value).Trim().Trim('`', '"'), word,
                StringComparison.OrdinalIgnoreCase);
        public bool HasMonsterPrefix()
        {
            if (Remaining < 10) return false;
            for (var index = 0; index < 8; index++)
                if (tokens[at + index].Kind != PvfTokenKind.Int32) return false;
            if (!tokens[at + 8].IsString) return false;
            var placement = resolve(tokens[at + 8].Value).Trim().ToLowerInvariant();
            return placement is "[fixed]" or "[ratio]" or "[half]";
        }
    }
}
