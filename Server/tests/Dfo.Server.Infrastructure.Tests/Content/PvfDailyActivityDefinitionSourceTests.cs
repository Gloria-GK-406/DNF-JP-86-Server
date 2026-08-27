using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfDailyActivityDefinitionSourceTests
{
    [SkippableFact]
    public async Task Projects_every_group_the_table_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDailyActivityDefinitionSource(provider);

        var groups = await source.GetAllAsync(CancellationToken.None);

        Assert.NotEmpty(groups);
        Assert.All(groups, group => Assert.True(group.GroupId > 0));
        Assert.All(groups, group => Assert.NotEmpty(group.Slots));
    }

    [SkippableFact]
    public async Task Projects_a_known_group_field_by_field()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDailyActivityDefinitionSource(provider);

        var group = await source.GetAsync(1, CancellationToken.None);
        Skip.If(group is null, "This archive does not define activity group 1.");

        Assert.Equal(new LevelRange(1, 100), group!.Level);
        Assert.Equal(6, group.Slots.Count);

        // [slot num table] 1 39 3  40 54 4  55 84 5  85 100 6
        Assert.Equal(4, group.SlotAllowances.Count);
        Assert.Equal(3, group.SlotCountForLevel(1));
        Assert.Equal(3, group.SlotCountForLevel(39));
        Assert.Equal(4, group.SlotCountForLevel(40));
        Assert.Equal(5, group.SlotCountForLevel(84));
        Assert.Equal(6, group.SlotCountForLevel(100));

        var firstSlot = group.Slots[0];
        Assert.Equal(1, firstSlot.SlotIndex);
        Assert.Contains(14710, firstSlot.QuestIds);
        Assert.DoesNotContain(1, firstSlot.QuestIds);

        // [reward table] 1 29 10099407 2  30 49 10099407 3  50 100 10100021 1
        Assert.Equal(3, group.Rewards.Count);
        Assert.Equal(new LevelRange(1, 29), group.Rewards[0].Level);
        Assert.Equal(10099407, group.Rewards[0].ItemTemplateId);
        Assert.Equal(2, group.Rewards[0].Count);
    }

    [SkippableFact]
    public async Task Slot_indexes_are_read_from_the_block_rather_than_inferred()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDailyActivityDefinitionSource(provider);

        foreach (var group in await source.GetAllAsync(CancellationToken.None))
        {
            Assert.All(group.Slots, slot => Assert.True(slot.SlotIndex > 0));
            Assert.All(group.Slots, slot => Assert.NotEmpty(slot.QuestIds));

            // The first operand is the slot index, so it must not leak into the quest list.
            var indexes = group.Slots.Select(slot => slot.SlotIndex).ToList();
            Assert.Equal(indexes.Count, indexes.Distinct().Count());
        }
    }

    [SkippableFact]
    public async Task A_group_without_an_allowance_table_opens_every_slot()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDailyActivityDefinitionSource(provider);
        var groups = await source.GetAllAsync(CancellationToken.None);

        var group = groups.FirstOrDefault(candidate => candidate.SlotAllowances.Count == 0);
        Skip.If(group is null, "Every group in this archive declares an allowance table.");

        Assert.Equal(group!.Slots.Count, group.SlotCountForLevel(group.Level.Minimum));
    }

    /// <summary>
    /// SC-6, matrix row 14: the packet carries daily-challenge state, which is the group's
    /// slots for the character's level combined with what has been claimed today.
    /// </summary>
    [SkippableFact]
    public async Task Definitions_support_projecting_todays_challenge_state()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDailyActivityDefinitionSource(provider);

        var group = await source.GetAsync(1, CancellationToken.None);
        Skip.If(group is null, "This archive does not define activity group 1.");

        const int characterLevel = 60;
        var claimed = new HashSet<int> { group!.Slots[0].SlotIndex };

        var openSlots = group.Slots.Take(group.SlotCountForLevel(characterLevel)).ToList();
        var state = openSlots
            .Select(slot => new { slot.SlotIndex, Claimed = claimed.Contains(slot.SlotIndex), slot.QuestIds.Count })
            .ToList();

        Assert.Equal(5, state.Count);
        Assert.Single(state, entry => entry.Claimed);
        Assert.All(state, entry => Assert.True(entry.Count > 0));

        var reward = group.Rewards.FirstOrDefault(candidate => candidate.Level.Includes(characterLevel));
        Assert.NotEqual(0, reward.ItemTemplateId);
    }
}
