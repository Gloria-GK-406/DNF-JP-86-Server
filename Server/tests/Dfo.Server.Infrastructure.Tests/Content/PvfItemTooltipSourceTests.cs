using Dfo.Pvf.Tests.Support;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfItemTooltipSourceTests
{
    [Fact]
    public async Task Equipment_tooltip_keeps_declaration_order_formats_units_and_skips_conditional_blocks()
    {
        var builder = Lists(equipment: [(9001, "weapon/katana/blade.equ"), (9002, "armor/coat.equ")], stackable: []);
        builder.AddScript("equipment/weapon/katana", "blade.equ")
            .Tag("[name]").Quoted("测试之刀").Tag("[detail explain]").Quoted("有3%%的几率").Tag("[flavor text]").Quoted("铸造的刀。")
            .Tag("[rarity]").Int(2).Tag("[usable job]").Quoted("[swordman]").Quoted("[at swordman]").Tag("[/usable job]")
            .Tag("[attach type]").Quoted("[sealing]").Tag("[minimum level]").Int(20)
            .Tag("[physical attack]").Int(15).Tag("[magical attack]").Int(23).Tag("[attack speed]").Int(80).Tag("[cast speed]").Int(-40)
            .Tag("[stuck]").Single(1).Tag("[future stat]").Int(9).Tag("[value]").Int(24640)
            .Tag("[equipment physical attack]").Int(201).Int(186).Tag("[separate attack]").Int(107).Int(69)
            .Tag("[physical critical hit]").Int(2).Tag("[elemental property]").Quoted("[light element]")
            .Tag("[equipment type]").Quoted("[weapon]").Int(22).Tag("[durability]").Int(45).Tag("[weight]").Int(2800)
            .Tag("[if]").Tag("[physical attack]").Int(99).Tag("[/if]").End();
        builder.AddScript("equipment/armor", "coat.equ")
            .Tag("[name]").Quoted("测试上衣").Tag("[usable job]").Quoted("[all]").Tag("[/usable job]").Tag("[attach type]").Quoted("[trade]")
            .Tag("[equipment type]").Quoted("[coat]").Int(0).Tag("[item group name]").Quoted("cl coat")
            .Tag("[equipment physical defense]").Int(300).Int(300).Tag("[fire resistance]").Int(5).End();
        AddJobs(builder);
        using var archive = new BuiltArchive(builder);
        var source = new PvfItemTooltipSource(archive.Provider, NullLogger<PvfItemTooltipSource>.Instance);

        var blade = (await source.FindAsync(9001, default))!;
        Assert.Equal(("测试之刀", 2, "equipment", "太刀", 20, (int?)null), (blade.Name, blade.Rarity, blade.Category, blade.TypeName, blade.MinimumLevel, blade.MaximumLevel));
        Assert.Equal(["鬼剑士（男）", "鬼剑士（女）"], blade.Jobs!);
        Assert.Equal(("sealing", "有3%的几率", "铸造的刀。", 45, 2.8, 24640, (int?)null),
            (blade.Attach, blade.Explain, blade.Flavor, blade.Durability, blade.Weight, blade.Price, blade.StackLimit));
        Assert.Equal([
            new("力量", "+15"), new("智力", "+23"), new("攻击速度", "+8.0%"), new("施放速度", "-4.0%"), new("命中率", "+1"),
            new("物理攻击力", "186 ~ 201"), new("独立攻击力", "69 ~ 107"), new("物理暴击率", "+2%"), new ItemTooltipStat("攻击属性", "光属性")], blade.Stats);

        var coat = (await source.FindAsync(9002, default))!;
        Assert.Equal(("布甲 上衣", "trade"), (coat.TypeName, coat.Attach)); Assert.Null(coat.Jobs);
        Assert.Equal([new("物理防御力", "300"), new ItemTooltipStat("火属性抗性", "+5")], coat.Stats);
        Assert.Null(await source.FindAsync(424242, default));
    }

    [Fact]
    public async Task Stackable_tooltip_reports_stack_limit_period_daily_delete_and_type_name()
    {
        var builder = Lists(equipment: [], stackable: [(3037, "cube.stk"), (8001, "ticket.stk")]);
        builder.AddScript("stackable", "cube.stk").Tag("[name]").Quoted("无色小晶块").Tag("[rarity]").Int(1)
            .Tag("[attach type]").Quoted("[free]").Tag("[minimum level]").Int(1).Tag("[price]").Int(100)
            .Tag("[stackable type]").Quoted("[material]").Int(2).End();
        builder.AddScript("stackable", "ticket.stk").Tag("[name]").Quoted("通行证").Tag("[explain]").Quoted("  入场材料。 ")
            .Tag("[attach type]").Quoted("[trade delete]").Tag("[stackable type]").Quoted("[quest]").Int(0)
            .Tag("[stack limit]").Int(10).Tag("[usable period]").Int(7).Tag("[daily delete item]").Int(1).End();
        using var archive = new BuiltArchive(builder);
        var source = new PvfItemTooltipSource(archive.Provider, NullLogger<PvfItemTooltipSource>.Instance);

        var cube = (await source.FindAsync(3037, default))!;
        Assert.Equal(("stackable", "材料", "free", (int?)null, false, (int?)null, (int?)null), (cube.Category, cube.TypeName, cube.Attach, cube.Price, cube.DailyDelete, cube.PeriodDays, cube.StackLimit));
        Assert.Empty(cube.Stats);
        var ticket = (await source.FindAsync(8001, default))!;
        Assert.Equal(("任务材料", "trade delete", "入场材料。", 10, 7, true), (ticket.TypeName, ticket.Attach, ticket.Explain, ticket.StackLimit, ticket.PeriodDays, ticket.DailyDelete));
    }

    [SkippableFact]
    public async Task A21_reference_items_match_values_read_off_the_real_archive()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfItemTooltipSource(provider, NullLogger<PvfItemTooltipSource>.Instance);
        var blade = await source.FindAsync(27650, default);
        Skip.If(blade?.Name != "复仇灵魂之刀", "The located archive is not the A21 reference archive.");
        Assert.Equal(("太刀", 20, "sealing", 45, 2.8, 24640), (blade!.TypeName, blade.MinimumLevel, blade.Attach, blade.Durability, blade.Weight, blade.Price));
        foreach (var expected in new ItemTooltipStat[] { new("物理攻击力", "186 ~ 201"), new("魔法攻击力", "206 ~ 223"), new("独立攻击力", "69 ~ 107"),
            new("力量", "+15"), new("智力", "+23"), new("攻击速度", "+8.0%"), new("施放速度", "+4.0%") }) Assert.Contains(expected, blade.Stats);
        var necklace = (await source.FindAsync(2681114, default))!;
        Assert.Equal(("分离的痛苦项链", "项链"), (necklace.Name, necklace.TypeName)); Assert.Null(necklace.Jobs);
        foreach (var expected in new ItemTooltipStat[] { new("魔法防御力", "1640 ~ 2000"), new("智力", "+24"), new("精神", "+32"),
            new("攻击速度", "+1.0%"), new("火属性抗性", "+5"), new("命中率", "-1") }) Assert.Contains(expected, necklace.Stats);
        var cube = (await source.FindAsync(3037, default))!;
        Assert.Equal(("无色小晶块", "材料", "free"), (cube.Name, cube.TypeName, cube.Attach));
        var ticket = (await source.FindAsync(690001556, default))!;
        Assert.Equal(("牛头怪乐园通行证", "trade delete", 10), (ticket.Name, ticket.Attach, ticket.StackLimit));
    }

    private static PvfArchiveBuilder Lists((int Id, string Path)[] equipment, (int Id, string Path)[] stackable)
    {
        // An empty .lst is an index error, so each list carries one never-read row.
        var builder = new PvfArchiveBuilder();
        equipment = [.. equipment, (1, "unused.equ")]; stackable = [.. stackable, (2, "unused.stk")];
        var equipmentList = builder.AddScript("equipment", "equipment.lst");
        foreach (var (id, path) in equipment) equipmentList.Int(id).Quoted(path);
        equipmentList.End();
        var stackableList = builder.AddScript("stackable", "stackable.lst");
        foreach (var (id, path) in stackable) stackableList.Int(id).Quoted(path);
        stackableList.End();
        return builder;
    }

    private static void AddJobs(PvfArchiveBuilder builder)
    {
        builder.AddScript("character", "character.lst").Int(0).Quoted("swordman/swordman.chr").Int(7).Quoted("swordman/atswordman.chr").End();
        builder.AddScript("character/swordman", "swordman.chr").Tag("[job]").Quoted("[swordman]").Tag("[growtype name]").Quoted("鬼剑士").Quoted("剑魂").End();
        builder.AddScript("character/swordman", "atswordman.chr").Tag("[job]").Quoted("[at swordman]").Tag("[growtype name]").Quoted("鬼剑士").Quoted("驭剑士").End();
    }

    private sealed class BuiltArchive : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"dfo-item-tooltip-{Guid.NewGuid():N}.pvf");
        public PvfArchiveProvider Provider { get; }
        public BuiltArchive(PvfArchiveBuilder builder) { File.WriteAllBytes(path, builder.Build()); Provider = new(new PvfContentOptions { ArchivePath = path }); }
        public void Dispose() { Provider.Dispose(); File.Delete(path); }
    }
}
