using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfMapActorDeclarationParserTests
{
    [Fact]
    public void Variable_modifiers_and_champion_features_keep_the_next_record_aligned()
    {
        var input = new Input();
        input.Add("[monster]",
            [.. Monster(10, "[dummy]", "[NPC]", 1016, "[boss]"),
             .. Monster(11, "[champion]", 2, "speed", "[fire]"),
             .. Monster(12, "[super champion]", 0),
             .. Monster(13, "[champion]"),
             .. Monster(14, "[normal]"),
             .. Monster(15, "[champion]")]);
        input.Add("[monster team]", 100, 0, 200, 75, 110, 50);
        var result = input.Parse();
        Assert.Equal(new[] { 10, 11, 12, 13, 14, 15 }, result.Actors.Select(actor => actor.Code));
        Assert.Equal(Enumerable.Range(0, 6), result.Actors.Select(actor => actor.RecordIndex));
        Assert.True(result.Actors[0].IsDummy);
        Assert.Equal(1016, result.Actors[0].BoundNpcId);
        Assert.Equal(new[] { "speed", "[fire]" }, result.Actors[1].ChampionFeatures);
        Assert.True(result.Actors[1].HasChampionFeatureCount);
        Assert.True(result.Actors[2].HasChampionFeatureCount);
        Assert.False(result.Actors[3].HasChampionFeatureCount);
        Assert.False(result.Actors[5].HasChampionFeatureCount);
        Assert.Equal(75, result.Actors[3].MonsterTeam);
        Assert.Equal(0, result.Actors[1].MonsterTeam);
        Assert.Equal(DungeonActorFaction.Monster, result.Actors[1].Faction);
        Assert.Equal(new DungeonSpawnPosition(120, 230, 0), result.Actors[0].Position);
        Assert.Equal(2, result.Actors[0].RandomDropCount);
        Assert.Equal(1, result.Actors[0].FixedDropCount);
    }

    [Fact]
    public void Apc_feature_list_precedes_trailing_parameters_and_preserves_NPC_binding()
    {
        var input = new Input();
        input.Add("[ai character]", 20, 30, 40, 1, "[monster]", "[NPC]", 1016,
            "[champion]", 2, "speed", "armor", 2, 0,
            21, 31, 41, 0, "[neutral]", "[normal]", 5, 6);
        var result = input.Parse();
        Assert.Equal(new[] { 20, 21 }, result.Actors.Select(actor => actor.Code));
        var first = result.Actors[0];
        Assert.Equal(DungeonActorSpawnSource.AiCharacter, first.Source);
        Assert.Equal(1, first.Rank);
        Assert.Equal(1016, first.BoundNpcId);
        Assert.Equal(new[] { "speed", "armor" }, first.ChampionFeatures);
        Assert.Equal(new[] { 1, 2, 0 }, first.Parameters);
        Assert.Equal(DungeonActorFaction.Neutral, result.Actors[1].Faction);
    }

    [Fact]
    public void Conditional_templates_keep_trigger_parameters_separate_from_position()
    {
        var input = new Input();
        input.Add("[monster condition]", Monster(30, "[boss]"));
        input.Add("[conditional summon monster]", 31, 1, -2, 6, 7, 8, 1, 0,
            "[fixed]", "[boss]", 381, 243, 0);
        var result = input.Parse();
        Assert.Equal(DungeonActorSpawnSource.ConditionalMonster, result.Actors[0].Source);
        var summon = result.Actors[1];
        Assert.Equal(DungeonActorSpawnSource.ConditionalSummon, summon.Source);
        Assert.Equal(new[] { 6, 7, 8 }, summon.Parameters);
        Assert.Equal(new DungeonSpawnPosition(381, 243, 0), summon.Position);
        Assert.True(summon.RelativeLevel);
        Assert.Equal(-2, summon.LevelValue);
    }

    [Fact]
    public void Object_and_child_indexes_include_nonmonster_and_empty_declarations()
    {
        var input = new Input();
        input.Add("[special passive object]",
            100, 1, 2, 0, 0,
            101, 3, 4, 0, 2, "[item]", 1, 3, -1, -1, -1, "[monster]", 500, 62, 7, 8, 9,
            102, 5, 6, 0, 1, "[hellparty]", 10, 100, 0, 11, 50, 1, "[/hellparty]",
            103, 7, 8, 0, 1, "[monster]", 501, 63, -1, -1, -1);
        var result = input.Parse();
        Assert.Equal(new[] { 500, 501 }, result.Actors.Select(actor => actor.Code));
        Assert.Equal(new int?[] { 1, 3 }, result.Actors.Select(actor => actor.ParentObjectIndex));
        Assert.Equal(new int?[] { 1, 0 }, result.Actors.Select(actor => actor.ChildIndex));
        Assert.Equal(new int?[] { 101, 103 }, result.Actors.Select(actor => actor.ParentObjectCode));
        Assert.Equal(new[] { 7, 8, 9 }, result.Actors[0].Parameters);
        Assert.Equal(new int?[] { 10, 100, 0, 11, 50, 1 },
            Assert.Single(result.Supplements, row => row.Section == "[hellparty]").Values.Select(value => value.Integer));
        Assert.Contains(result.Supplements, row => row.Section == "[special passive object]");
    }

    [Fact]
    public void Native_half_placement_does_not_get_consumed_as_a_missing_feature_count()
    {
        var input = new Input();
        input.Add("[monster]", [.. Monster(10, "[champion]"),
            11, 0, 50, 120, 230, 0, 1, 0, "[half]", "[normal]"]);
        var result = input.Parse();
        Assert.Equal(new[] { 10, 11 }, result.Actors.Select(actor => actor.Code));
        Assert.False(result.Actors[0].HasChampionFeatureCount);
        Assert.Equal("[half]", result.Actors[1].Placement);
    }

    [Fact]
    public void Legacy_standalone_hellparty_table_is_retained_after_object_actions()
    {
        var input = new Input();
        input.Add("[special passive object]", 100, 1, 2, 0, 1,
            "[monster]", 500, 62, -1, -1, -1,
            "[hellparty]", 10, 100, 0, 11, 50, 1, "[/hellparty]");
        var result = input.Parse();
        Assert.Equal(500, Assert.Single(result.Actors).Code);
        Assert.Equal(new int?[] { 10, 100, 0, 11, 50, 1 },
            Assert.Single(result.Supplements, row => row.Section == "[hellparty]").Values.Select(value => value.Integer));
    }

    [Fact]
    public void Empty_object_slots_and_virtual_traps_preserve_later_parent_indexes()
    {
        var input = new Input();
        input.Add("[special passive object]",
            0, 1, -1, -1, 0,
            -1, 0, 0, 0, 1, "[trap]", 2211, 1, 3, 4, -1,
            803, 1362, 195, 0, 0,
            0, 1, -1, -1, 0,
            100, 1, 2, 0, 1, "[monster]", 500, 62, -1, -1, -1);
        var result = input.Parse();
        var actor = Assert.Single(result.Actors);
        Assert.Equal(4, actor.ParentObjectIndex);
        Assert.Equal(0, actor.ChildIndex);
        Assert.Equal(500, actor.Code);
        Assert.Equal(37, Assert.Single(result.Supplements).Values.Count);
    }

    [Fact]
    public void Auxiliary_declarations_are_retained_without_becoming_room_actors()
    {
        var input = new Input();
        input.Add("[blood monster]", 10, 0, 100, 200, 0, 3000, 1000, 1, 1, 2);
        input.Add("[ultimate monster]", 20, 0, 100, 200, 0, 5000, 4000, 1, 0, 1);
        input.Add("[attacked monster info]", 30, 100, 200, 0, 0, 0, 0, 0, 0);
        input.Add("[monster spawn pos]", 2, 100, 200, 0, 110, 210, 0);
        input.Add("[monster specific AI]", "[zone defence]", 1, 2);
        var result = input.Parse();
        Assert.Empty(result.Actors);
        Assert.Equal(5, result.Supplements.Count);
        Assert.Equal("[zone defence]", result.Supplements[4].Values[0].Text);
    }

    [Theory]
    [InlineData("unknown-rank")]
    [InlineData("missing-npc")]
    [InlineData("missing-feature")]
    [InlineData("negative-feature-count")]
    [InlineData("truncated-monster")]
    [InlineData("unknown-action")]
    [InlineData("negative-action-count")]
    [InlineData("invalid-parent-id")]
    [InlineData("nonempty-zero-parent")]
    [InlineData("virtual-monster-parent")]
    [InlineData("truncated-action")]
    [InlineData("unclosed-hellparty")]
    [InlineData("wrong-spawn-count")]
    [InlineData("truncated-blood")]
    public void Invalid_declarations_fail_with_resource_section_record_and_token(string shape)
    {
        var input = new Input();
        var section = "[monster]";
        object[] tokens = shape switch
        {
            "unknown-rank" => Monster(1, "[unknown]"),
            "missing-npc" => Monster(1, "[NPC]", "[boss]"),
            "missing-feature" => Monster(1, "[champion]", 2, "speed"),
            "negative-feature-count" => Monster(1, "[champion]", -1),
            "truncated-monster" => [1, 1, 0, 10],
            "unknown-action" => [100, 1, 2, 0, 1, "[unknown]", 1, 2, 3, 4, 5],
            "negative-action-count" => [100, 1, 2, 0, -1],
            "invalid-parent-id" => [-2, 1, 2, 0, 0],
            "nonempty-zero-parent" => [0, 1, 2, 0, 1, "[trap]", 1, 2, 3, 4, 5],
            "virtual-monster-parent" => [-1, 1, 2, 0, 1, "[monster]", 1, 2, 3, 4, 5],
            "truncated-action" => [100, 1, 2, 0, 1, "[monster]", 1],
            "unclosed-hellparty" => [100, 1, 2, 0, 1, "[hellparty]", 1, 100, 0],
            "wrong-spawn-count" => [2, 10, 20, 0],
            _ => [1, 2, 3],
        };
        if (shape.Contains("action", StringComparison.Ordinal) || shape.Contains("parent", StringComparison.Ordinal)
            || shape == "unclosed-hellparty")
            section = "[special passive object]";
        else if (shape == "wrong-spawn-count") section = "[monster spawn pos]";
        else if (shape == "truncated-blood") section = "[blood monster]";
        input.Add(section, tokens);
        var error = Assert.Throws<InvalidDataException>(() => input.Parse());
        Assert.Contains("map/test.map", error.Message);
        Assert.Contains(section, error.Message);
        Assert.Contains("record 0", error.Message);
        Assert.Contains("token ", error.Message);
    }

    [SkippableFact]
    public void Every_current_listed_map_has_a_complete_actor_declaration_parse()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var archive = provider.Archive;
        var failures = new List<string>();
        var count = 0;
        foreach (var (_, relative) in PvfListFile.Read(archive, "map/map.lst"))
        {
            var path = "map/" + relative;
            try { PvfMapActorDeclarationParser.Parse(archive, PvfDocument.Open(archive, path), path); }
            catch (InvalidDataException error) { failures.Add(error.Message); }
            count++;
        }
        Assert.True(count > 10_000, $"Only {count} maps were validated.");
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(20)));
    }

    [SkippableFact]
    public void Real_counterattack_and_defence_boss_declarations_preserve_source_identity()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var archive = provider.Archive;
        var counterPath = "map/CounterAttack_r/22036_2227(7,1).map";
        var counter = PvfMapActorDeclarationParser.Parse(archive, PvfDocument.Open(archive, counterPath), counterPath);
        Assert.Equal(5, counter.Actors.Count);
        Assert.Equal(61150, counter.Actors[0].Code);
        Assert.Equal(64639, counter.Actors[4].Code);
        Assert.True(counter.Actors[4].IsDummy);
        Assert.Equal(3, counter.Actors[0].Rank);
        Assert.Equal(3, counter.Actors[4].Rank);
        var defencePath = "map/GentDefence_r/21314.map";
        var defence = PvfMapActorDeclarationParser.Parse(archive, PvfDocument.Open(archive, defencePath), defencePath);
        var templates = defence.Actors.Where(actor => actor.Source == DungeonActorSpawnSource.SpecialPassiveObject).ToArray();
        Assert.Equal(30, templates.Length);
        Assert.Equal(Enumerable.Range(0, 9), templates.Select(actor => actor.ParentObjectIndex!.Value).Distinct());
        var apc = Assert.Single(defence.Actors, actor => actor.Source == DungeonActorSpawnSource.AiCharacter);
        Assert.Equal(10409, apc.Code);
        Assert.Equal(3, apc.Rank);
        Assert.Equal(DungeonActorFaction.Monster, apc.Faction);
    }

    private static object[] Monster(int code, params object[] suffix) =>
        [code, 1, 0, 120, 230, 0, 2, 1, "[fixed]", .. suffix];

    private sealed class Input
    {
        private readonly List<PvfSection> sections = [];
        private readonly Dictionary<int, string> strings = [];
        public void Add(string section, params object[] values)
        {
            var tokens = new List<PvfToken>();
            foreach (var value in values)
            {
                if (value is int integer) tokens.Add(new(PvfTokenKind.Int32, integer));
                else
                {
                    var index = strings.Count;
                    strings.Add(index, (string)value);
                    tokens.Add(new(PvfTokenKind.QuotedString, index));
                }
            }
            sections.Add(new(section, tokens));
        }
        public DungeonMapActorDeclarations Parse() =>
            PvfMapActorDeclarationParser.ParseSections(sections, index => strings[index], "map/test.map");
    }
}
