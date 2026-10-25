using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Dfo.Server.Protocol.Game.QuestGiveUp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Tests.Quests;

public sealed class SeekingQuestCompletionTests
{
    [Fact]
    public async Task Giving_up_removes_the_carried_quest_and_prevents_later_completion()
    {
        using var shop = new ShopFixture(true,services =>
        {
            services.RemoveAll<IQuestContentRuleService>();
            services.AddSingleton<IQuestContentRuleService>(new SeekingQuestRules());
        });
        shop.Sql("INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) "
            + "VALUES(1,0,2001,0)");

        var commands = new QuestTestCommands(shop);
        var givenUp = await commands.SendAsync(new QuestGiveUpTestInput(
            new AccountId(1), new CharacterId(1), 2001));
        var finish = await commands.SendAsync(new QuestFinishTestInput(
            new AccountId(1), new CharacterId(1), 2001, ushort.MaxValue, 1, 0));

        Assert.True(givenUp.GivenUp);
        Assert.Empty(Assert.IsType<QuestProgressSnapshot>(givenUp.QuestProgress).Quests);
        Assert.Equal(0, shop.Scalar("SELECT COUNT(*) FROM character_active_quests "
            + "WHERE character_id=1 AND quest_id=2001"));
        Assert.False(finish.Finished);
    }

    [Fact]
    public async Task A_stale_complete_trigger_cannot_finish_after_the_required_item_is_gone()
    {
        using var shop = new ShopFixture(true,services =>
        {
            services.RemoveAll<IQuestContentRuleService>();
            services.AddSingleton<IQuestContentRuleService>(new SeekingQuestRules());
        });
        shop.Sql("INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) "
            + "VALUES(1,0,2001,0)");

        var result = await new QuestTestCommands(shop).SendAsync(
            new QuestFinishTestInput(new AccountId(1), new CharacterId(1), 2001,
                ushort.MaxValue, 1, 0));

        Assert.False(result.Finished);
        Assert.Equal(0, shop.Scalar(
            "SELECT trigger_value FROM character_active_quests WHERE character_id=1 AND quest_id=2001"));
        Assert.Equal(1, Assert.Single(result.QuestProgress!.Quests).TriggerValue);
    }

    private sealed class SeekingQuestRules : IQuestContentRuleService
    {
        private static readonly QuestDefinition Quest = new(
            2001, "hold an item", QuestGrade.Epic, null, new LevelRange(1, 99), null,
            null, null, [], "seeking", "M", null, null, null, null, null, null, null,
            [], [], [], [], SeekingItems: [new SeekingItemRequirement(5000, 1)]);

        public ValueTask<IReadOnlyList<QuestDefinition>> AllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([Quest]);

        public ValueTask<QuestDefinition?> FindAsync(int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<QuestDefinition?>(questId == Quest.QuestId ? Quest : null);

        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> TrainingAvailabilityAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);

        public QuestParameters Parameters() => new([100, 100], new Dictionary<char, int>(),
            100, 100, 100, 100);
    }
}
