using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// The two facts an acceptance needs from the archive: what progress the quest opens at, and what
/// it hands over when it is taken.
/// </summary>
/// <remarks>
/// Quests 1016 and 101 are the two a player accepted on 2026-08-27, and the reference server's
/// capture shows what its own answer carried for 1016: trigger 1 and no items. Both are
/// <c>[meet npc]</c> with <c>[sub type] -1</c>, which is the branch that falls through to the
/// default in the reference server too -- so what these pin is that this source reaches the same
/// answer by the same route, not merely that it guesses one.
/// </remarks>
public sealed class PvfQuestAcceptanceContentTests
{
    [SkippableTheory]
    [InlineData(14396)]
    [InlineData(14397)]
    public async Task Regional_hunt_acceptance_starts_with_five_remaining_targets(int questId)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var quest = await new PvfQuestDefinitionSource(provider).GetAsync(questId, default);

        Assert.NotNull(quest);
        Assert.Equal(5u, quest.InitialTrigger);
    }

    [SkippableTheory]
    [InlineData(1016)]
    [InlineData(101)]
    public async Task The_accepted_quests_open_at_trigger_one_and_hand_over_nothing(int questId)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var quest = await source.GetAsync(questId, CancellationToken.None);

        Assert.NotNull(quest);
        Assert.Equal("meet npc", quest!.ObjectiveTypeCode);
        Assert.Equal(1u, quest.InitialTrigger);
        Assert.Empty(quest.EventItems ?? []);
    }

    /// <summary>
    /// A quest whose objective declares counts opens at those counts, packed nine bits apiece.
    /// </summary>
    /// <remarks>
    /// The strided branches are transcribed from the reference server rather than observed on the
    /// wire -- nothing has yet accepted a hunting quest here. What this pins is that the stride is
    /// applied at all: the archive is searched for a quest that uses one, so the test fails if the
    /// branch stops being reached rather than passing vacuously.
    /// </remarks>
    [SkippableFact]
    public async Task A_hunting_quest_opens_at_the_counts_its_objective_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var hunting = (await source.GetAllAsync(CancellationToken.None))
            .Where(quest => quest.ObjectiveTypeCode is "hunt monster" or "hunt enemy")
            .ToArray();

        Assert.NotEmpty(hunting);
        Assert.Contains(hunting, quest => quest.InitialTrigger != 1);
        Assert.All(hunting, quest => Assert.True(
            quest.InitialTrigger <= 0x7FF_FFFF,
            $"quest {quest.QuestId} packs a trigger wider than three nine-bit channels"));
    }

    [Fact]
    public void Kill_targets_recognize_complete_four_and_five_field_grammars()
    {
        var ordinary = QuestKillObjectiveParser.Parse("hunt monster", [144, 1, 13099, 2, -1, -1, -3, 1]);
        Assert.Equal(QuestKillParseStatus.Parsed, ordinary.Status);
        Assert.Equal(514u, ordinary.InitialTrigger);
        Assert.Equal([0, 1], ordinary.Targets.Select(target => target.ChannelIndex));
        var regional = QuestKillObjectiveParser.Parse("hunt monster", [-2, 31, -1, -3, 5]);
        Assert.Equal(QuestKillParseStatus.Parsed, regional.Status);
        Assert.Equal(new QuestKillScope(QuestKillScopeKind.WorldMap, 31), Assert.Single(regional.Targets).Scope);
        Assert.Equal(5u, regional.InitialTrigger);
        Assert.Equal(QuestKillParseStatus.Invalid,
            QuestKillObjectiveParser.Parse("hunt monster", [144, 1, 13099]).Status);
    }

    [Fact]
    public void Clear_quest_projects_distinct_dependencies_within_trigger_capacity()
    {
        Assert.Equal([1776], PvfQuestDefinitionSource.ProjectClearedQuestRequirements(
            "clear quest", [1776]));
        Assert.Equal([1776, 1777], PvfQuestDefinitionSource.ProjectClearedQuestRequirements(
            "clear quest", [1776, 1777]));
        Assert.Empty(PvfQuestDefinitionSource.ProjectClearedQuestRequirements(
            "clear quest", [1776, 1776]));
    }

    [SkippableFact]
    public async Task Current_archive_exposes_real_server_counted_hunt_and_clear_dependencies()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);
        var quests = await source.GetAllAsync(CancellationToken.None);

        Assert.Contains(quests, quest => quest.ObjectiveTypeCode == "hunt monster"
            && quest.KillObjectives.Status == QuestKillParseStatus.Parsed);
        var clearQuest = quests.Where(quest => quest.ObjectiveTypeCode == "clear quest").ToArray();
        Assert.NotEmpty(clearQuest);
        Assert.All(clearQuest, quest => Assert.Single(quest.ClearedQuestRequirements));
    }

    /// <summary>
    /// The archive's event items are read as pairs, and every one it declares is usable.
    /// </summary>
    [SkippableFact]
    public async Task Quests_that_declare_event_items_read_as_item_and_count_pairs()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfQuestDefinitionSource(provider);

        var giving = (await source.GetAllAsync(CancellationToken.None))
            .Where(quest => quest.EventItems is { Count: > 0 })
            .ToArray();

        Assert.NotEmpty(giving);
        Assert.All(giving, quest => Assert.All(quest.EventItems!, item =>
        {
            Assert.True(item.ItemId > 0, $"quest {quest.QuestId} declares item {item.ItemId}");
            Assert.True(item.Count > 0, $"quest {quest.QuestId} declares count {item.Count}");
        }));
    }
}
