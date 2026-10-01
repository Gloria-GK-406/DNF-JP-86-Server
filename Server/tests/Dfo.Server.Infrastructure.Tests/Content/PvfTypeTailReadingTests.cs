using Dfo.Pvf.Schema;
using Dfo.Pvf.Tests.Support;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfTypeTailReadingTests
{
    [Theory]
    [InlineData("name-only", true)]
    [InlineData("subtype", true)]
    [InlineData("nested", true)]
    [InlineData("empty", false)]
    [InlineData("wrong-tail", false)]
    [InlineData("single-tail", false)]
    [InlineData("extra-int", false)]
    [InlineData("extra-string", false)]
    [InlineData("duplicate", false)]
    [InlineData("negative", false)]
    public async Task Equipment_slot_requires_a_complete_root_type_shape(string shape, bool valid)
    {
        using var file = new BuiltArchive(Build(shape));
        var slot = await new PvfEquipmentSlotDefinitionSource(file.Provider).GetAsync(7, default);
        if (valid) Assert.Equal(new BusinessSlot(BusinessInventorySpace.WornAvatar, 3), slot);
        else Assert.Null(slot);
    }

    [Theory]
    [InlineData("name-only", true)]
    [InlineData("subtype", true)]
    [InlineData("nested", true)]
    [InlineData("empty", false)]
    [InlineData("wrong-tail", false)]
    [InlineData("single-tail", false)]
    [InlineData("extra-int", false)]
    [InlineData("extra-string", false)]
    [InlineData("duplicate", false)]
    [InlineData("negative", false)]
    public void Extra_drop_avatar_classification_cannot_ignore_a_bad_type_tail(string shape, bool valid)
    {
        using var file = new BuiltArchive(Build(shape));
        if (!valid)
        {
            var error = Assert.Throws<InvalidDataException>(() => PvfExtraDropContentSource.Read(file.Provider.Archive));
            Assert.Contains("[equipment type]", error.Message);
            return;
        }
        var content = PvfExtraDropContentSource.Read(file.Provider.Archive);
        Assert.Equal(1, content.ItemCount);
        var pool = content.Pools[(DropItemKind.Avatar, 0, ExtraDropDungeonType.Normal)];
        Assert.Equal(new DropPoolEntry(7, 100, 35), Assert.Single(pool.Bucket(20, DropRarity.Rare)));
    }

    [Theory]
    [InlineData("name-only", true)]
    [InlineData("subtype", true)]
    [InlineData("nested", true)]
    [InlineData("empty", false)]
    [InlineData("wrong-tail", false)]
    [InlineData("single-tail", false)]
    [InlineData("extra-int", true)]
    [InlineData("extra-string", false)]
    [InlineData("duplicate", false)]
    [InlineData("negative", false)]
    public async Task Unlimited_use_requires_a_complete_root_type_shape(string shape, bool valid)
    {
        using var file = new BuiltArchive(Build(shape));
        var rule = await new PvfItemUseRuleDefinitionSource(file.Provider).GetAsync(9, default);
        Assert.NotNull(rule);
        Assert.Equal(valid, rule.IsReadable);
        if (valid) Assert.False(rule.IsConsumedOnUse);
        if (shape == "nested") Assert.Equal(100, rule.CooldownMilliseconds);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("string")]
    [InlineData("single")]
    [InlineData("negative")]
    public async Task Coupon_integer_operands_are_validated_in_both_placement_and_use(string shape)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("stackable", "stackable.lst").Int(9).Quoted("test.stk").End();
        var script = builder.AddScript("stackable", "test.stk").Tag("[stack limit]").Int(100)
            .Tag("[stackable type]").Quoted("[coupon]").Int(26);
        if (shape == "string") script.Quoted("bad");
        else if (shape == "single") script.Single(0);
        else script.Int(shape == "negative" ? -1 : 0);
        script.End();
        using var file = new BuiltArchive(builder);
        var rule = await new PvfItemUseRuleDefinitionSource(file.Provider).GetAsync(9, default);
        Assert.NotNull(rule);
        Assert.Equal(shape == "valid", rule.IsReadable);
        PvfEntryLifecycleContentSource.RefillItemMetadata ReadMetadata() =>
            PvfEntryLifecycleContentSource.ResolveItemMetadataFor(file.Provider.Archive, 9,
                new Dictionary<int, (int Id, string RelativePath)> { [9] = (9, "test.stk") }, new Dictionary<int, string>());
        if (shape == "valid") Assert.Equal((100, (byte)2), (ReadMetadata().StackLimit, ReadMetadata().ItemKind));
        else
        {
            var error = Assert.Throws<InvalidDataException>(() => ReadMetadata());
            Assert.Contains("[stackable type]", error.Message);
            Assert.Contains("stackable/test.stk", error.Message);
        }
    }

    [Fact]
    public void A_nested_only_extra_drop_tag_is_not_a_root_candidate()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "equipment.lst").Int(7).Quoted("test.equ").End();
        builder.AddScript("stackable", "stackable.lst").Int(9).Quoted("test.stk").End();
        builder.AddScript("dungeon", "dungeon.lst").Int(1).Quoted("test.dgn").End();
        builder.AddScript("equipment", "test.equ").Tag("[unknown]").Tag("[special monster drop]").Int(1)
            .Tag("[equipment type]").Quoted("[nested]").Quoted("bad tail").Tag("[/unknown]")
            .Tag("[equipment type]").Quoted("[coat avatar]").Int(0).End();
        builder.AddScript("stackable", "test.stk").Tag("[stackable type]").Quoted("[usable]").Int(0).End();
        builder.AddScript("dungeon", "test.dgn").Tag("[ancient dungeon]").Int(0).End();
        using var file = new BuiltArchive(builder);
        Assert.Equal(0, PvfExtraDropContentSource.Read(file.Provider.Archive).ItemCount);
    }

    [Fact]
    public async Task Fatigue_action_keeps_its_operand_cursor_after_an_earlier_nested_action()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("stackable", "stackable.lst").Int(9).Quoted("test.stk").End();
        builder.AddScript("stackable", "test.stk").Tag("[unknown]").Tag("[action type]").Quoted("[add fatigue]").Int(5)
            .Tag("[/unknown]").Tag("[action type]").Quoted("[add fatigue]").Int(42).End();
        using var file = new BuiltArchive(builder);
        var action = await new PvfStackableActionDefinitionSource(file.Provider).GetAsync(9, default);
        Assert.NotNull(action);
        Assert.True(action.IsReadable);
        Assert.Equal(42, action.FatigueRecovery);
    }

    [Fact]
    public void Action_string_helper_keeps_specialized_mixed_operands_without_imposing_type_tail_shape()
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("stackable", "test.stk").Tag("[action type]").Quoted("[custom action]")
            .Quoted("target").Int(2).Int(3).End();
        using var file = new BuiltArchive(builder);
        var archive = file.Provider.Archive;
        var reader = archive.OpenScript("stackable/test.stk");
        var unreadable = false;
        Assert.True(PvfItemUseRuleDefinitionSource.TryReadFirstString(archive, ref reader,
            PvfField.Tokens("[action type]"), out var name, ref unreadable));
        Assert.False(unreadable);
        Assert.Equal("[custom action]", archive.ResolveString(name.Value));
        Assert.True(reader.TryReadSectionValue(out var target));
        Assert.Equal("target", archive.ResolveString(target.Value));
        Assert.True(reader.TryReadInt32(out var first));
        Assert.Equal(2, first);
        Assert.True(reader.TryReadInt32(out var second));
        Assert.Equal(3, second);
    }

    private static PvfArchiveBuilder Build(string shape)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("equipment", "equipment.lst").Int(7).Quoted("test.equ").End();
        builder.AddScript("stackable", "stackable.lst").Int(9).Quoted("test.stk").End();
        builder.AddScript("dungeon", "dungeon.lst").Int(1).Quoted("test.dgn").End();
        builder.AddScript("dungeon", "test.dgn").Tag("[ancient dungeon]").Int(0).End();
        var equipment = builder.AddScript("equipment", "test.equ");
        Type(equipment, "[equipment type]", "[coat avatar]", shape);
        equipment.Tag("[special monster drop]").Int(1).Tag("[grade]").Int(20).Tag("[rarity]").Int(2)
            .Tag("[creation rate]").Int(100).Tag("[durability]").Int(35)
            .Tag("[difficulty for extra drop]").Quoted("[easy]").Tag("[dungeon type for extra drop]").Quoted("[normal]").End();
        var stackable = builder.AddScript("stackable", "test.stk");
        Type(stackable, "[stackable type]", "[unlimited]", shape);
        stackable.Tag("[cool time]").Int(100);
        if (shape == "nested") stackable.Tag("[restriction]").Tag("[cool time]").Int(200).Tag("[/restriction]");
        stackable.End();
        return builder;
    }

    private static void Type(PvfArchiveBuilder.ScriptWriter script, string field, string name, string shape)
    {
        if (shape == "nested") script.Tag("[unknown]").Tag(field).Quoted("[nested]").Quoted("bad tail").Tag("[/unknown]");
        script.Tag(field);
        if (shape != "empty") script.Quoted(name);
        if (shape == "wrong-tail") script.Quoted("bad tail");
        else if (shape == "single-tail") script.Single(1);
        else if (shape == "negative") script.Int(-1);
        else if (shape is "subtype" or "nested" or "extra-int" or "extra-string") script.Int(0);
        if (shape == "extra-int") script.Int(1);
        if (shape == "extra-string") script.Quoted("extra");
        if (shape == "duplicate") script.Tag(field).Quoted(name).Int(0);
    }

    private sealed class BuiltArchive : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pvf-type-tail-{Guid.NewGuid():N}.pvf");
        internal PvfArchiveProvider Provider { get; }
        internal BuiltArchive(PvfArchiveBuilder builder)
        {
            File.WriteAllBytes(path, builder.Build());
            Provider = new(new PvfContentOptions { ArchivePath = path });
        }
        public void Dispose()
        {
            Provider.Dispose();
            File.Delete(path);
        }
    }
}
