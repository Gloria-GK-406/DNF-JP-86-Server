using Dfo.Pvf.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfQuestEnemyRewardTests
{
    [Theory]
    [InlineData(1, QuestEnemyType.Monster)]
    [InlineData(2, QuestEnemyType.APC)]
    [InlineData(3, QuestEnemyType.PassiveObject)]
    public void Eight_fields_preserve_type_and_all_operands(int type, QuestEnemyType expected) =>
        Assert.Equal(new QuestEnemyRewardItem(8961, expected, 84, -1, 10149153, 2, 35, 10),
            Assert.Single(PvfQuestDefinitionSource.ProjectEnemyRewardItems(
                [8961, type, 84, -1, 10149153, 2, 35, 10], 2201)));

    [Theory]
    [InlineData(new int[] { 1, 0, -1, -1, 100, 1, 100, -1 })]
    [InlineData(new int[] { 1, 4, -1, -1, 100, 1, 100, -1 })]
    [InlineData(new int[] { 1, 2, -1, -1, 100, 1, 101, -1 })]
    [InlineData(new int[] { 1, 2, -1, -1, 100, 0, 100, -1 })]
    [InlineData(new int[] { 1, 2, -1, -1, 100, 1, 100 })]
    public void Malformed_groups_fail_with_quest_identity(int[] values) =>
        Assert.Contains("2201", Assert.Throws<InvalidDataException>(() =>
            PvfQuestDefinitionSource.ProjectEnemyRewardItems(values, 2201)).Message);

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Last_declaration_wins_even_when_empty_or_at_end_of_file(bool legacyLast, bool emptyLast)
    {
        var builder = new PvfArchiveBuilder();
        builder.AddScript("n_quest", "quest.lst").Int(1).Quoted("one.qst").End();
        var script = builder.AddScript("n_quest", "one.qst").Tag("[name]").Quoted("test")
            .Tag("[type]").Quoted("[seeking]").Tag("[int data]").Int(100).Int(10);
        void Reward(bool legacy, bool empty)
        {
            script.Tag(legacy ? "[monster reward item]" : "[enemy reward item]");
            if (empty) return;
            script.Int(17);
            if (!legacy) script.Int(2);
            script.Int(-1).Int(-1).Int(100).Int(1).Int(100).Int(10);
        }
        Reward(!legacyLast, false);
        Reward(legacyLast, emptyLast);
        script.End();
        var path = Path.Combine(Path.GetTempPath(), $"quest-enemy-{Guid.NewGuid():N}.pvf");
        try
        {
            File.WriteAllBytes(path, builder.Build());
            using var provider = new PvfArchiveProvider(new PvfContentOptions { ArchivePath = path });
            var quest = await new PvfQuestDefinitionSource(provider).GetAsync(1, CancellationToken.None);
            Assert.NotNull(quest);
            if (emptyLast) Assert.Empty(quest.EnemyRewardItems);
            else Assert.Equal(legacyLast ? QuestEnemyType.Monster : QuestEnemyType.APC,
                Assert.Single(quest.EnemyRewardItems).EnemyType);
        }
        finally { File.Delete(path); }
    }

    [SkippableFact]
    public async Task A21_APC_object_and_repeated_candidate_declarations_are_preserved()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);
        var apc = await source.GetAsync(2415, CancellationToken.None);
        Assert.Contains(new(21604, QuestEnemyType.APC, 73, -1, 4755, 1, 100, 15), apc!.EnemyRewardItems);
        var obj = await source.GetAsync(2201, CancellationToken.None);
        Assert.Contains(new(8961, QuestEnemyType.PassiveObject, 84, -1, 10149153, 1, 100, 10), obj!.EnemyRewardItems);
        var repeated = await source.GetAsync(4486, CancellationToken.None);
        Assert.Equal(new[] { (1,80), (2,60), (3,30) }, repeated!.EnemyRewardItems
            .Where(r => r.EnemyCode == 66315 && r.DungeonId == 80).Select(r => (r.Count, r.DropRate)));
        var all = await source.GetAllAsync(CancellationToken.None);
        // Same server-PVF snapshot as Projects_every_neutral_eligibility_input_from_the_real_archive.
        // Regional/five-field kill objectives now survive catalogue projection.
        Assert.Equal(3253, all.Count); // Adds Southern Dale scheduled exchange 20549.
        Assert.Equal(3827, all.Sum(q => q.EnemyRewardItems.Count(r => r.EnemyType == QuestEnemyType.APC)));
        Assert.Equal(9, all.Sum(q => q.EnemyRewardItems.Count(r => r.EnemyType == QuestEnemyType.PassiveObject)));
    }
}
