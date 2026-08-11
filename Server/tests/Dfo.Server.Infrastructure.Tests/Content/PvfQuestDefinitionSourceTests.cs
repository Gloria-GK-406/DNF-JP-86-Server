using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// Locates the operator-supplied archive. It is excluded from version control, so these
/// tests skip rather than fail where it is absent.
/// </summary>
internal static class ContentArchive
{
    public static string? Path { get; } = Locate();

    public static bool Available => Path is not null;

    public const string SkipReason =
        "Script.pvf not found. Set PVF_ARCHIVE_PATH, or place the archive in the repository root or Data/Pvf/.";

    public static PvfArchiveProvider CreateProvider() =>
        new(new PvfContentOptions { ArchivePath = Path });

    private static string? Locate()
    {
        var configured = Environment.GetEnvironmentVariable(PvfContentOptions.PathVariable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var candidate in new[]
                     {
                         System.IO.Path.Combine(directory.FullName, "Script.pvf"),
                         System.IO.Path.Combine(directory.FullName, "Data", "Pvf", "Script.pvf"),
                     })
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

public sealed class PvfQuestDefinitionSourceTests
{
    [SkippableFact]
    public async Task Projects_every_quest_the_list_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var quests = await source.GetAllAsync(CancellationToken.None);

        Assert.NotEmpty(quests);
        Assert.All(quests, quest => Assert.True(quest.QuestId > 0));
        Assert.All(quests, quest => Assert.NotNull(quest.Name));

        // Some content declares no name at all -- tower placeholders, for instance -- so a
        // blanket non-empty assertion would encode a rule the archive does not follow.
        // What must hold is that named quests keep their name.
        Assert.Contains(quests, quest => quest.Name.Length > 0);
    }

    [SkippableFact]
    public async Task Projects_a_known_quest_field_by_field()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var quest = await source.GetAsync(1, CancellationToken.None);
        Skip.If(quest is null, "This archive does not define quest 1.");

        // n_quest/Title/achievement_34_title.qst
        Assert.Equal(QuestGrade.Achievement, quest!.Grade);
        Assert.Equal(QuestJobRequirement.Any, quest.JobRequirement);
        Assert.Equal(new LevelRange(34, 99), quest.RequiredLevel);
        Assert.Equal(QuestDefinition.AnyGrowType, quest.GrowTypeRequirement);
        Assert.True(quest.AppliesToAnyGrowType);
        Assert.Equal(3, quest.NpcIndex);
        Assert.Equal(-1, quest.CompleteNpcIndex);

        var reward = Assert.Single(quest.Rewards);
        Assert.Equal(QuestRewardKind.Title, reward.Kind);
        Assert.Equal([26607, 1], reward.Values);
    }

    [SkippableFact]
    public async Task Every_job_and_grade_token_in_the_archive_is_modelled()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var quests = await source.GetAllAsync(CancellationToken.None);

        // An Unrecognized value means the archive uses a token this contract does not
        // model, which would silently drop a distinction the content draws.
        Assert.DoesNotContain(quests, quest => quest.JobRequirement == QuestJobRequirement.Unrecognized);
        Assert.DoesNotContain(quests, quest => quest.Grade == QuestGrade.Unrecognized);
        Assert.DoesNotContain(
            quests,
            quest => quest.Rewards.Any(reward => reward.Kind == QuestRewardKind.Unrecognized));
    }

    [SkippableFact]
    public async Task Repeated_reads_return_the_same_catalog_without_reprojecting()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var first = await source.GetAllAsync(CancellationToken.None);
        var second = await source.GetAllAsync(CancellationToken.None);

        Assert.Same(first, second);
    }

    [SkippableFact]
    public async Task An_undefined_quest_returns_null_rather_than_a_placeholder()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        Assert.Null(await source.GetAsync(int.MaxValue, CancellationToken.None));
    }

    [SkippableFact]
    public void A_missing_archive_fails_with_an_actionable_message()
    {
        using var provider = new PvfArchiveProvider(
            new PvfContentOptions { ArchivePath = "/definitely/not/here/Script.pvf" });

        // Resolution falls through to the conventional locations, so this only proves the
        // failure shape when no archive is reachable at all.
        Skip.If(ContentArchive.Available, "An archive is reachable from this working tree.");

        var error = Assert.Throws<FileNotFoundException>(() => provider.Archive);
        Assert.Contains("PVF_ARCHIVE_PATH", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// SC-6: the definitions must carry enough to drive matrix row 12, which needs an
    /// <em>available</em> quest list rather than a raw catalog. The availability rule
    /// itself belongs to G5; this only proves the contract does not starve it.
    /// </summary>
    [SkippableFact]
    public async Task Definitions_support_computing_an_available_quest_list()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);
        var quests = await source.GetAllAsync(CancellationToken.None);

        // Stand-in for character progress that G3 will supply.
        var character = new { Level = 40, Job = QuestJobRequirement.Mage, GrowType = 1 };
        var cleared = quests.Take(50).Select(quest => quest.QuestId).ToHashSet();

        var available = quests
            .Where(quest => !cleared.Contains(quest.QuestId))
            .Where(quest => quest.RequiredLevel.Includes(character.Level))
            .Where(quest => quest.JobRequirement is QuestJobRequirement.Any || quest.JobRequirement == character.Job)
            .Where(quest => quest.AppliesToAnyGrowType || quest.GrowTypeRequirement == character.GrowType)
            .ToList();

        Assert.NotEmpty(available);
        Assert.DoesNotContain(available, quest => cleared.Contains(quest.QuestId));
        Assert.All(available, quest => Assert.True(quest.RequiredLevel.Includes(40)));
        Assert.All(
            available,
            quest => Assert.True(quest.JobRequirement is QuestJobRequirement.Any or QuestJobRequirement.Mage));

        // A level-1 character must see a different set, or the level band is not being read.
        var lowLevel = quests.Count(quest => quest.RequiredLevel.Includes(1));
        Assert.NotEqual(lowLevel, quests.Count(quest => quest.RequiredLevel.Includes(40)));
    }
}
