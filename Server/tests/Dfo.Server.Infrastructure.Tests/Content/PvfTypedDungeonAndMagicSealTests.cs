using Dfo.Pvf.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfTypedDungeonAndMagicSealTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dungeon_identical_event_ticket_restatement_is_one_requirement_but_conflicts_are_rejected(bool identical)
    {
        var builder = DungeonBuilder();
        var script = builder.AddScript("dungeon", "test.dgn")
            .Tag("[event required item]").Int(10004955).Int(1).Int(1)
            .Tag("[event required item]").Int(10004955).Int(identical ? 1 : 2).Int(1);
        AddMaze(script).End();
        using var file = new BuiltArchive(builder);
        var source = new PvfDungeonDefinitionSource(file.Provider);
        if (!identical)
            await Assert.ThrowsAsync<InvalidDataException>(() => source.GetAsync(7, default).AsTask());
        else
            Assert.Equal(new DungeonRequiredItem(10004955, 1, true), Assert.Single((await source.GetAsync(7, default))!.EventRequiredItems));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("extra")]
    [InlineData("range")]
    public async Task Dungeon_bad_scalar_never_uses_an_old_or_default_level(string defect)
    {
        var builder = DungeonBuilder();
        var script = builder.AddScript("dungeon", "test.dgn").Tag("[minimum required level]");
        if (defect == "type") script.Quoted("bad"); else script.Int(defect == "range" ? 256 : 2);
        if (defect == "extra") script.Int(3);
        AddMaze(script).End();
        using var file = new BuiltArchive(builder);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new PvfDungeonDefinitionSource(file.Provider).GetAsync(7, default).AsTask());
        Assert.Contains("dungeon/test.dgn", error.Message);
        Assert.Contains("[minimum required level]", error.Message);
    }

    [Fact]
    public async Task Dungeon_repeated_minimum_levels_validate_each_declaration_and_keep_last_override()
    {
        var builder = DungeonBuilder();
        var script = builder.AddScript("dungeon", "test.dgn").Tag("[minimum required level]").Int(1)
            .Tag("[basis level]").Int(5).Tag("[recommended level]").Int(46).Int(85)
            .Tag("[minimum required level]").Int(46);
        AddMaze(script).End();
        using var file = new BuiltArchive(builder);
        var dungeon = await new PvfDungeonDefinitionSource(file.Provider).GetAsync(7, default);
        Assert.NotNull(dungeon);
        Assert.Equal((byte)46, dungeon.MinimumRequiredLevel);
        Assert.Equal((byte)5, dungeon.BasisLevel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Dungeon_legal_restatement_cannot_mask_an_invalid_minimum_level(bool damageFirst)
    {
        var builder = DungeonBuilder();
        var script = builder.AddScript("dungeon", "test.dgn").Tag("[minimum required level]");
        if (damageFirst) script.Quoted("bad"); else script.Int(1);
        script.Tag("[recommended level]").Int(46).Int(85).Tag("[minimum required level]");
        if (damageFirst) script.Int(46); else script.Int(256);
        AddMaze(script).End();
        using var file = new BuiltArchive(builder);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new PvfDungeonDefinitionSource(file.Provider).GetAsync(7, default).AsTask());
        Assert.Contains("[minimum required level]", error.Message);
        Assert.Contains("dungeon/test.dgn", error.Message);
    }

    [Theory]
    [InlineData("[size]")]
    [InlineData("[map specification]")]
    [InlineData("[start map]")]
    [InlineData("[boss map]")]
    [InlineData("[quest connection]")]
    public async Task Dungeon_incomplete_rows_never_return_a_usable_prefix(string field)
    {
        var builder = DungeonBuilder();
        var script = builder.AddScript("dungeon", "test.dgn").Tag("[maze info]").Tag(field);
        if (field == "[map specification]") script.Quoted("map");
        script.Int(1).Tag("[unknown]").End();
        using var file = new BuiltArchive(builder);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new PvfDungeonDefinitionSource(file.Provider).GetAsync(7, default).AsTask());
        Assert.Contains(field, error.Message);
        Assert.Contains("dungeon/test.dgn", error.Message);
    }

    [Fact]
    public async Task Dungeon_distinct_maze_scopes_preserve_repeated_fields_and_missing_defaults()
    {
        var builder = DungeonBuilder();
        var script = builder.AddScript("dungeon", "test.dgn");
        AddMaze(script).Tag("[quest connection]").Int(0).Int(1776);
        AddMaze(script).Tag("[quest connection]").Int(1).Int(1777).End();
        using var file = new BuiltArchive(builder);
        var dungeon = await new PvfDungeonDefinitionSource(file.Provider).GetAsync(7, default);
        Assert.NotNull(dungeon);
        Assert.Equal(2, dungeon.Mazes.Count);
        Assert.Equal((byte)1, dungeon.MinimumRequiredLevel);
        Assert.Equal(1d, dungeon.ExperienceWeight);
        Assert.Equal(1776, dungeon.Mazes[0].QuestConnection!.QuestId);
        Assert.Equal(1777, dungeon.Mazes[1].QuestConnection!.QuestId);
    }

    [Fact]
    public async Task Dungeon_variable_map_candidates_preserve_all_candidates_without_phantom_cells()
    {
        var builder = DungeonBuilder();
        builder.AddScript("dungeon", "test.dgn").Tag("[maze info]").Tag("[size]").Int(9).Int(1)
            .Tag("[map specification]").Quoted("map").Int(7).Int(0).Int(27136).Int(27137).Int(27138).Int(27139).Int(27140)
            .Quoted("boss").Int(8).Int(0).Int(27142).Int(27143).Int(27144).Int(27145)
            .Tag("[start map]").Int(7).Int(0).Tag("[boss map]").Int(8).Int(0).End();
        using var file = new BuiltArchive(builder);
        var dungeon = await new PvfDungeonDefinitionSource(file.Provider).GetAsync(7, default);
        Assert.NotNull(dungeon);
        var cells = Assert.Single(dungeon.Mazes).Cells;
        Assert.Equal(2, cells.Count);
        Assert.Equal([27136, 27137, 27138, 27139, 27140], cells[new(7, 0)].MapCandidates);
        Assert.Equal([27142, 27143, 27144, 27145], cells[new(8, 0)].BossMapCandidates);
        Assert.Empty(cells[new(8, 0)].MapCandidates);
        Assert.DoesNotContain(new MazeCell(0, 0), cells.Keys);
    }

    [Theory]
    [InlineData("single")]
    [InlineData("zero")]
    [InlineData("string")]
    public async Task Dungeon_variable_candidate_damage_is_not_hidden_after_a_valid_map(string defect)
    {
        var builder = DungeonBuilder();
        var script = builder.AddScript("dungeon", "test.dgn").Tag("[maze info]").Tag("[map specification]")
            .Quoted("map").Int(7).Int(0).Int(27136).Int(27137);
        if (defect == "single") script.Single(27138);
        if (defect == "zero") script.Int(0);
        if (defect == "string") script.Quoted("broken kind");
        script.End();
        using var file = new BuiltArchive(builder);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new PvfDungeonDefinitionSource(file.Provider).GetAsync(7, default).AsTask());
        Assert.Contains("[map specification]", error.Message);
        Assert.Contains("dungeon/test.dgn", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dungeon_repeated_cell_candidates_merge_in_order_without_crossing_kinds_or_mazes(bool separateSections)
    {
        var builder = DungeonBuilder();
        var script = builder.AddScript("dungeon", "test.dgn").Tag("[maze info]").Tag("[size]").Int(2).Int(1)
            .Tag("[map specification]").Quoted("map").Int(0).Int(0).Int(32984).Int(32984)
            .Quoted("boss").Int(0).Int(0).Int(32990);
        if (separateSections) script.Tag("[map specification]");
        script.Quoted("map").Int(0).Int(0).Int(32986).Int(32984).Int(32987)
            .Quoted("boss").Int(0).Int(0).Int(32991).Int(32990)
            .Tag("[start map]").Int(0).Int(0).Tag("[boss map]").Int(0).Int(0)
            .Tag("[quest connection]").Int(0).Int(2389);
        AddMaze(script).End();
        using var file = new BuiltArchive(builder);

        var dungeon = await new PvfDungeonDefinitionSource(file.Provider).GetAsync(7, default);

        Assert.NotNull(dungeon);
        Assert.Equal(2, dungeon.Mazes.Count);
        var questCell = Assert.Single(dungeon.Mazes[0].Cells).Value;
        Assert.Equal([32984, 32986, 32987], questCell.MapCandidates);
        Assert.Equal([32990, 32991], questCell.BossMapCandidates);
        var ordinaryCell = Assert.Single(dungeon.Mazes[1].Cells).Value;
        Assert.Equal([61000], ordinaryCell.MapCandidates);
        Assert.Empty(ordinaryCell.BossMapCandidates);
        Assert.False(questCell.HasDeclaredConnections);
        Assert.False(ordinaryCell.HasDeclaredConnections);
        Assert.All(dungeon.Mazes, maze => Assert.Null(maze.ConfigurationError));
        Assert.Equal(2389, dungeon.Mazes[0].QuestConnection!.QuestId);
        Assert.Null(dungeon.Mazes[1].QuestConnection);
    }

    [Theory]
    [InlineData("single")]
    [InlineData("zero")]
    [InlineData("truncated")]
    public async Task Dungeon_repeated_candidate_sections_still_validate_the_complete_tail(string defect)
    {
        var builder = DungeonBuilder();
        var script = AddMaze(builder.AddScript("dungeon", "test.dgn"))
            .Tag("[map specification]").Quoted("map").Int(0).Int(1);
        if (defect == "single") script.Single(32986);
        if (defect == "zero") script.Int(0);
        script.End();
        using var file = new BuiltArchive(builder);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PvfDungeonDefinitionSource(file.Provider).GetAsync(7, default).AsTask());

        Assert.Contains("[map specification]", error.Message);
        Assert.Contains(defect switch {
            "single" => "expected Int32",
            "zero" => "expected a positive map candidate id",
            _ => "at least one map candidate",
        }, error.Message);
    }

    [Fact]
    public async Task Unused_map_bindings_on_empty_grid_cells_do_not_create_rooms_or_invalidate_the_maze()
    {
        var builder = DungeonBuilder();
        builder.AddScript("dungeon", "test.dgn").Tag("[maze info]").Tag("[size]").Int(3).Int(1)
            .Tag("[greed]").Quoted("BBEEAA")
            .Tag("[map specification]").Quoted("map").Int(0).Int(0).Int(10)
            .Quoted("boss").Int(1).Int(0).Int(11).Quoted("map").Int(2).Int(0).Int(12)
            .Tag("[start map]").Int(0).Int(0).Tag("[boss map]").Int(1).Int(0).End();
        using var file = new BuiltArchive(builder);
        var maze = Assert.Single((await new PvfDungeonDefinitionSource(file.Provider).GetAsync(7, default))!.Mazes);

        Assert.Null(maze.ConfigurationError);
        Assert.Equal(2, maze.Cells.Count);
        Assert.DoesNotContain(new MazeCell(2, 0), maze.Cells.Keys);
    }

    [Theory]
    [InlineData("grid", "connection grid does not match maze size")]
    [InlineData("outside", "outside occupied maze cells")]
    [InlineData("zero-width", "positive width and height")]
    [InlineData("empty", "no occupied cells")]
    public async Task Dungeon_maze_geometry_diagnostics_do_not_block_reading_other_mazes(string defect, string reason)
    {
        var builder = DungeonBuilder();
        var script = AddMaze(builder.AddScript("dungeon", "test.dgn"))
            .Tag("[maze info]").Tag("[size]").Int(defect == "zero-width" ? 0 : 2).Int(1)
            .Tag("[quest connection]").Int(0).Int(2389);
        if (defect != "empty")
        {
            script.Tag("[greed]").Quoted(defect == "outside" ? "BBAA" : "BB")
                .Tag("[map specification]").Quoted("map").Int(defect == "outside" ? 2 : 0).Int(0).Int(32984)
                .Tag("[start map]").Int(0).Int(0).Tag("[boss map]").Int(0).Int(0);
        }
        AddMaze(script).End();
        using var file = new BuiltArchive(builder);

        var dungeon = await new PvfDungeonDefinitionSource(file.Provider).GetAsync(7, default);

        Assert.NotNull(dungeon);
        Assert.Equal(3, dungeon.Mazes.Count);
        Assert.Equal([0, 1, 2], dungeon.Mazes.Select(maze => maze.Index));
        Assert.Null(dungeon.Mazes[0].ConfigurationError);
        Assert.Null(dungeon.Mazes[2].ConfigurationError);
        var invalid = dungeon.Mazes[1];
        Assert.Contains(reason, invalid.ConfigurationError!);
        Assert.Equal(2389, invalid.QuestConnection!.QuestId);
        if (defect is not ("empty" or "zero-width"))
        {
            var cell = Assert.Single(invalid.Cells).Value;
            if (defect == "outside") Assert.Empty(cell.MapCandidates);
            else Assert.Equal([32984], cell.MapCandidates);
            Assert.True(cell.HasDeclaredConnections);
        }
        else Assert.Empty(invalid.Cells);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("truncated")]
    [InlineData("invalid-greed")]
    public async Task Dungeon_unselected_maze_syntax_errors_still_refuse_the_complete_file(string defect)
    {
        var builder = DungeonBuilder();
        var script = AddMaze(builder.AddScript("dungeon", "test.dgn"))
            .Tag("[maze info]").Tag("[size]").Int(2).Int(1)
            .Tag("[quest connection]").Int(0).Int(2389);
        if (defect == "invalid-greed") script.Tag("[greed]").Quoted("AB");
        else
        {
            script.Tag("[map specification]").Quoted("map").Int(0).Int(0);
            if (defect == "type") script.Single(32984);
        }
        script.End();
        using var file = new BuiltArchive(builder);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new PvfDungeonDefinitionSource(file.Provider).GetAsync(7, default).AsTask());

        Assert.Contains("dungeon/test.dgn", error.Message);
        Assert.Contains(defect == "invalid-greed" ? "[greed]" : "[map specification]", error.Message);
    }

    [Theory]
    [InlineData("tail")]
    [InlineData("word")]
    [InlineData("mixed")]
    public async Task Monster_records_require_every_integer_and_both_rank_words(string defect)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("map", "map.lst").Int(7).Quoted("test.map").End();
        var script = builder.AddScript("map", "test.map").Tag("[monster]");
        AddMonster(script, defect == "word");
        if (defect == "tail") script.Int(99);
        if (defect == "mixed") script.Quoted("bad");
        script.Tag("[/monster]").End();
        using var file = new BuiltArchive(builder);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new PvfDungeonRoomActorSource(file.Provider).GetAsync(7, default).AsTask());
        Assert.Contains("[monster]", error.Message);
        Assert.Contains("map/test.map", error.Message);
    }

    [Fact]
    public async Task Monster_repeated_blocks_remain_valid()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("map", "map.lst").Int(7).Quoted("test.map").End();
        var script = builder.AddScript("map", "test.map").Tag("[monster]");
        AddMonster(script).Tag("[/monster]").Tag("[monster]");
        AddMonster(script).Tag("[/monster]").End();
        using var file = new BuiltArchive(builder);
        var actors = await new PvfDungeonRoomActorSource(file.Provider).GetAsync(7, default);
        Assert.Equal(2, actors.Count);
        Assert.All(actors, actor => Assert.Equal((byte)3, actor.ActorType));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    public async Task Ai_minimum_info_uses_actual_positions_and_checks_the_complete_payload(string defect)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("map", "map.lst").Int(7).Quoted("test.map").End();
        builder.AddScript("map", "test.map").Tag("[ai character]").Int(461).Int(0).Int(0).Int(0)
            .Quoted("[character]").Quoted("[normal]").Int(0).Int(0).Tag("[/ai character]").End();
        builder.AddScript("aicharacter", "aicharacter.lst").Int(461).Quoted("test.aic").End();
        var script = builder.AddScript("aicharacter", "test.aic");
        AddMinimumInfo(script, defect == "type");
        if (defect == "extra") script.Quoted("bad");
        if (defect == "duplicate") AddMinimumInfo(script, false);
        script.End();
        using var file = new BuiltArchive(builder);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new PvfDungeonRoomActorSource(file.Provider).GetAsync(7, default).AsTask());
        Assert.Contains("[minimum info]", error.Message);
        Assert.Contains("aicharacter/test.aic", error.Message);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("negative")]
    public void Stack_limit_damage_cannot_become_zero(string defect)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("stackable", "test.stk").Tag("[stack limit]");
        if (defect == "type") script.Quoted("bad"); else script.Int(defect == "negative" ? -1 : 100);
        if (defect == "extra") script.Int(200);
        if (defect == "duplicate") script.Tag("[stack limit]").Int(200);
        script.Tag("[stackable type]").Quoted("[waste]").Int(1).End();
        using var file = new BuiltArchive(builder);
        var error = Assert.Throws<InvalidDataException>(() => ResolveMetadata(file));
        Assert.Contains("[stack limit]", error.Message);
        Assert.Contains("stackable/test.stk", error.Message);
    }

    [Fact]
    public void Nested_reward_item_metadata_does_not_duplicate_the_root_fields()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("stackable", "test.stk").Tag("[stack limit]").Int(100)
            .Tag("[stackable type]").Quoted("[waste]").Int(1)
            .Tag("[booster info]").Tag("[stack limit]").Int(2)
            .Tag("[stackable type]").Quoted("[material]").Int(0).Tag("[/booster info]").End();
        using var file = new BuiltArchive(builder);
        var metadata = ResolveMetadata(file);
        Assert.Equal(100, metadata.StackLimit);
        Assert.Equal((byte)2, metadata.ItemKind);
    }

    [Theory]
    [InlineData("selection", "[choose option group]")]
    [InlineData("group", "[option group]")]
    [InlineData("level", "[level]")]
    [InlineData("range", "[physical attack]")]
    [InlineData("nonfinite", "[physical attack]")]
    [InlineData("quantity", "[common]")]
    [InlineData("cost", "[break seal cost]")]
    [InlineData("change", "[option modification]")]
    [InlineData("duplicatecost", "[break seal cost]")]
    [InlineData("duplicaterange", "[physical attack]")]
    [InlineData("valuerange", "[different weight]")]
    [InlineData("valueweights", "[modified option type]")]
    public async Task Magic_seal_usable_prefix_does_not_hide_damage_later_in_the_field(string defect, string field)
    {
        using var file = new BuiltArchive(MagicSealBuilder(defect));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new PvfEquipmentMagicSealContentSource(file.Provider).GetEquipmentAsync(7, default).AsTask());
        Assert.Contains(field, error.Message);
        Assert.Contains("PVF '", error.Message);
    }

    [Fact]
    public async Task Magic_seal_preserves_zero_weight_disabled_rows_and_costs()
    {
        using var file = new BuiltArchive(MagicSealBuilder());
        var definition = await new PvfEquipmentMagicSealContentSource(file.Provider).GetEquipmentAsync(7, default);
        Assert.NotNull(definition);
        Assert.Equal(1720, definition.UnsealGoldCost);
        Assert.Equal(3800, definition.ChangeGoldCost);
        Assert.Single(definition.Quantities);
        Assert.All(definition.InitialGroups, group => Assert.Equal(new Dfo.Server.Abstractions.Content.MagicSealCandidate(15, 13, 98, 10), Assert.Single(group)));
    }

    [Fact]
    public async Task Magic_seal_preserves_single_stat_ranges_separately_from_integer_quality()
    {
        using var file = new BuiltArchive(MagicSealBuilder("single"));
        var definition = await new PvfEquipmentMagicSealContentSource(file.Provider).GetEquipmentAsync(7, default);
        Assert.NotNull(definition);
        Assert.Equal(new Dfo.Server.Abstractions.Content.MagicSealCandidate(15, 13.5, 98.5, 10), Assert.Single(definition.InitialGroups[0]));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("extra")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    public void Bad_stackable_type_does_not_become_the_default_consumable(string defect)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("stackable", "test.stk").Tag("[stackable type]");
        if (defect == "type") script.Int(99);
        if (defect != "empty") script.Quoted("[material]").Int(0);
        // Multiple integer operands are legal; a noninteger tail must still reject the whole declaration.
        if (defect == "extra") script.Int(1).Quoted("invalid tail");
        if (defect == "duplicate") script.Tag("[stackable type]").Quoted("[waste]").Int(1);
        script.End();
        using var file = new BuiltArchive(builder);
        var error = Assert.Throws<InvalidDataException>(() => ResolveMetadata(file));
        Assert.Contains("[stackable type]", error.Message);
        Assert.Contains("stackable/test.stk", error.Message);
    }

    [Fact]
    public void Undeclared_optional_stack_metadata_still_uses_existing_defaults()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("stackable", "test.stk").Tag("[name]").Quoted("test").End();
        using var file = new BuiltArchive(builder);
        var metadata = ResolveMetadata(file);
        Assert.Equal(0, metadata.StackLimit);
        Assert.Equal((byte)2, metadata.ItemKind);
    }

    [Theory]
    [InlineData("tail")]
    [InlineData("type")]
    public async Task Refill_rows_reject_complete_prefix_followed_by_structural_damage(string defect)
    {
        var builder = new PvfArchiveBuilder();
        var script = builder.AddScript("etc", "chn_server_limititemusageinfo.etc").Tag("[refill item]")
            .Int(7).Int(2).Quoted("2030-01-01 06:00:00");
        if (defect == "type") script.Quoted("bad"); else script.Int(0).Int(8);
        script.End();
        using var file = new BuiltArchive(builder);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new PvfEntryLifecycleContentSource(file.Provider, slotMappings: new Dfo.Server.Abstractions.Inventory.InventorySlotMappingRuleService())
            .GetDailyRefillRulesAsync(DateTimeOffset.UtcNow, default).AsTask());
        Assert.Contains("[refill item]", error.Message);
        Assert.Contains("etc/chn_server_limititemusageinfo.etc", error.Message);
    }

    [Theory]
    [InlineData("clamp", 300, 500)]
    [InlineData("scopes", 13, 98)]
    public async Task Magic_seal_keeps_stat_magnitudes_and_does_not_overwrite_dungeon_with_pvp(string content, int first, int second)
    {
        using var file = new BuiltArchive(MagicSealBuilder(content));
        var definition = await new PvfEquipmentMagicSealContentSource(file.Provider).GetEquipmentAsync(7, default);
        Assert.NotNull(definition);
        Assert.Equal(new Dfo.Server.Abstractions.Content.MagicSealCandidate(15, first, second, 10), Assert.Single(definition.InitialGroups[0]));
    }

    private static PvfArchiveBuilder DungeonBuilder()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("dungeon", "dungeon.lst").Int(7).Quoted("test.dgn").End();
        return builder;
    }

    private static PvfArchiveBuilder.ScriptWriter AddMaze(PvfArchiveBuilder.ScriptWriter script) =>
        script.Tag("[maze info]").Tag("[size]").Int(3).Int(2)
            .Tag("[map specification]").Quoted("map").Int(0).Int(1).Int(61000)
            .Tag("[start map]").Int(0).Int(1).Tag("[boss map]").Int(0).Int(1);

    private static PvfArchiveBuilder.ScriptWriter AddMonster(PvfArchiveBuilder.ScriptWriter script, bool omitRank = false)
    {
        script.Int(61670).Int(1).Int(0).Int(50).Int(30).Int(0).Int(1).Int(1).Quoted("[fixed]");
        if (!omitRank) script.Quoted("[boss]");
        return script;
    }

    private static void AddMinimumInfo(PvfArchiveBuilder.ScriptWriter script, bool wrongType)
    {
        script.Tag("[minimum info]").Quoted("guide");
        if (wrongType) script.Quoted("bad"); else script.Int(0);
        script.Int(2).Int(1).Int(1).Int(80).Int(0).Int(0).Int(0).Int(0).Int(0).Int(0)
            .Quoted("no creature").Int(0).Int(0).Int(0);
    }

    private static PvfEntryLifecycleContentSource.RefillItemMetadata ResolveMetadata(BuiltArchive file) =>
        PvfEntryLifecycleContentSource.ResolveItemMetadataFor(file.Provider.Archive, 7,
            new Dictionary<int, (int Id, string RelativePath)> { [7] = (7, "test.stk") }, new Dictionary<int, string>());

    private static PvfArchiveBuilder MagicSealBuilder(string? defect = null)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "equipment.lst").Int(7).Quoted("weapon/katana/test.equ").End();
        builder.AddScript("equipment/weapon/katana", "test.equ").Tag("[random option]").Int(1)
            .Tag("[rarity]").Int(2).Tag("[minimum level]").Int(75).Tag("[equipment type]").Quoted("[weapon]").Int(0).End();
        var selection = builder.AddScript("etc/randomoption", "optiongroupselection.etc").Tag("[choose option group]")
            .Int(2).Int(-1).Quoted("katana").Int(1).Int(3).Int(1000).Int(1000).Int(1000).Int(0).Int(0).Int(1000);
        if (defect == "selection") selection.Int(9);
        selection.End();
        builder.AddScript("etc/randomoption", "randomoption.lst").Int(15).Quoted("option.etc").End();
        var group = builder.AddScript("etc/randomoption", "optiongrouping.etc").Tag("[option group]").Int(1000).Int(15).Int(10);
        if (defect == "group") group.Quoted("bad");
        group.Tag("[/option group]").End();
        var option = builder.AddScript("etc/randomoption", "option.etc").Tag("[dungeon]").Tag("[option]").Int(15)
            .Tag("[level]").Int(1).Tag("[physical attack]").Int(8).Int(45).Tag("[/level]").Tag("[level]").Int(75);
        if (defect == "level") option.Int(76);
        option.Tag("[physical attack]");
        if (defect == "single") option.Single(13.5f).Single(98.5f);
        else if (defect == "nonfinite") option.Single(float.NaN).Int(98);
        else if (defect == "clamp") option.Int(300).Int(500);
        else option.Int(13).Int(98);
        if (defect == "range") option.Quoted("bad");
        if (defect == "duplicaterange") option.Tag("[physical attack]").Int(13).Int(98);
        option.Tag("[/level]");
        if (defect == "scopes") option.Tag("[pvp]").Tag("[option]").Int(15).Tag("[level]").Int(75)
            .Tag("[physical attack]").Int(14).Int(99).Tag("[/level]");
        option.End();
        var quantity = builder.AddScript("etc/randomoption", "optionquantity.etc").Tag("[common]").Int(1).Int(1000).Int(2).Int(0);
        if (defect == "quantity") quantity.Int(3);
        quantity.Tag("[/common]").End();
        var costs = builder.AddScript("etc/randomoption", "randomizedoptionoverall2.etc").Tag("[break seal cost]").Int(2).Int(75).Int(1).Int(1720);
        if (defect == "cost") costs.Int(99);
        if (defect == "duplicatecost") costs.Int(2).Int(75).Int(1).Int(1720);
        costs.Tag("[option modification]").Int(75).Int(3800).Int(38000);
        if (defect == "change") costs.Quoted("bad");
        costs.Tag("[different weight]").Int(2).Int(1).Int(0).Int(25)
            .Int(2).Int(2).Int(10).Int(30).Int(2).Int(3).Int(30).Int(defect == "valuerange" ? 256 : 55).Tag("[/different weight]").End();
        var quality = builder.AddScript("etc/randomoption", "randomizedoptionoverall1.etc")
            .Tag("[option type]").Int(2).Int(-1).Int(75).Int(7000).Int(7000).Int(2500).Tag("[/option type]")
            .Tag("[modified option type]").Int(2).Int(-1).Int(75).Int(1500).Int(7000).Int(1500)
            .Tag("[/modified option type]");
        if (defect == "valueweights") quality.Tag("[modified option type]").Int(2);
        quality.End();
        return builder;
    }

    private sealed class BuiltArchive : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pvf-dungeon-typed-{Guid.NewGuid():N}.pvf");
        internal PvfArchiveProvider Provider { get; }
        internal BuiltArchive(PvfArchiveBuilder builder)
        {
            File.WriteAllBytes(path, builder.Build());
            Provider = new(new PvfContentOptions { ArchivePath = path });
        }
        public void Dispose() { Provider.Dispose(); File.Delete(path); }
    }
}
