using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Tests.Quests;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Testing;
namespace Dfo.Server.Application.Tests.Characters;

public sealed class CareerReconciliationTests
{
    [Fact]
    public async Task Advancement_resets_all_allocated_pages_and_rebuilds_new_career_grants()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = Fixture();
            await f.SeedAsync();
            f.Sql($"UPDATE characters SET level=20,exp={f.Services.GetRequiredService<Dfo.Server.Abstractions.World.IExperienceRuleService>().CharacterScale().ExperienceForLevel(20)} WHERE character_id=71; INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) VALUES(71,'pve',0,8,8,1),(71,'pve',1,8,8,1),(71,'pve',0,9,5,1)");
            await using (var tx = f.Scope.Begin())
            {
                Assert.True((await Complete(f)).Finished);
                await tx.CommitAsync(default);
            }
            Assert.Equal(1, f.Scalar("SELECT grow_type FROM characters WHERE character_id=71"));
            var character = f.Services.GetRequiredService<ICharacterDomainService>().Get(new(71));
            Assert.Equal(1, (await character.IdentityAsync(default))!.GrowType);
            Assert.Equal(1240, (await character.BaseStatsAsync(default))!.HpMax);
            var facts = (await f.State.ReadAsync(new(71), default))!;
            foreach (var page in facts.Pages)
            {
                Assert.DoesNotContain(page.Skills, s => s.SkillId == 8);
                Assert.Equal(3, page.Skills.Single(s => s.SkillId == 7).Level);
                Assert.Equal(1, page.Skills.Single(s => s.SkillId == 4).Level);
            }
            Assert.All(facts.Pages, page => Assert.DoesNotContain(page.Skills, skill => skill.SkillId is 1 or 5));
            Assert.Equal(1005, f.Rules.GetLedger(facts.Career, facts.Level, facts.Pages, facts.PointBasis).For(new(0)).Sp);
            var count = f.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=71");
            await using (var tx = f.Scope.Begin()) { await f.State.ReconcileAsync(new(71), default); await tx.CommitAsync(default); }
            Assert.Equal(count, f.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=71"));
            Assert.Equal(0, f.Scalar("SELECT grow_type FROM characters WHERE character_id=72"));
        });
    }

    [Fact]
    public async Task Career_reset_removes_unmodelled_learning_instead_of_retaining_old_allocations()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = Fixture(); await f.SeedAsync();
            f.Sql("INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level,extra_values) VALUES(71,'pve',0,0,999,3,X'0102')");
            await using (var tx = f.Scope.Begin())
            {
                Assert.True((await Complete(f)).Finished);
                await tx.CommitAsync(default);
            }
            var facts = (await f.State.ReadAsync(new(71), default))!;
            Assert.DoesNotContain(facts.Pages.SelectMany(page => page.Skills), skill => skill.SkillId == 999);
            Assert.True(f.Rules.GetLedger(facts.Career, facts.Level, facts.Pages, facts.PointBasis).IsComplete);
        });
    }

    [Fact]
    public async Task A_failed_skill_write_rolls_back_career_and_prior_skill_writes()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = Fixture(); await f.SeedAsync();
            f.Sql("CREATE TRIGGER fail_grant BEFORE INSERT ON character_skills WHEN NEW.character_id=71 AND NEW.skill_id=7 BEGIN SELECT RAISE(ABORT,'injected grant failure'); END;");
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await using var tx = f.Scope.Begin();
                await Complete(f);
                await tx.CommitAsync(default);
            });
            Assert.Equal(0, f.Scalar("SELECT grow_type FROM characters WHERE character_id=71"));
            Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=71"));
        });
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 15)]
    public async Task Invalid_owner_or_career_leaves_character_unchanged(int account, byte grow)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = Fixture(); await f.SeedAsync();
            if (account == 2 || grow == 0)
            {
                await using var tx = f.Scope.Begin();
                Assert.False((await Complete(f, account, grow)).Finished);
                await tx.CommitAsync(default);
            }
            else await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await using var tx = f.Scope.Begin();
                await Complete(f, account, grow);
                await tx.CommitAsync(default);
            });
            Assert.Equal(0, f.Scalar("SELECT grow_type FROM characters WHERE character_id=71"));
        });
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_spitfire_quest_commits_or_rolls_back_the_whole_completion(bool failGrant)
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        await using var f = new LearningFixture(useArchive: true, configure: services =>
            services.AddCommandHandler<FinishQuestCommand, QuestFinishOutcome, FinishQuestCommandHandler>(CommandExecutionKind.Transactional));
        await f.SeedAsync();
        Prepare(f);
        f.Sql($"UPDATE characters SET job=2,level=20,exp={f.Services.GetRequiredService<Dfo.Server.Abstractions.World.IExperienceRuleService>().CharacterScale().ExperienceForLevel(20)} WHERE character_id=71; DELETE FROM character_skills WHERE character_id=71; INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) VALUES(71,0,7840,0)");
        if (failGrant)
            f.Sql("CREATE TRIGGER fail_grant BEFORE INSERT ON character_skills WHEN NEW.character_id=71 BEGIN SELECT RAISE(ABORT,'injected grant failure'); END;");
        var bus = f.Services.GetRequiredService<ICommandBus>();
        var command = new FinishQuestCommand(new(1), new(71), 7840, ushort.MaxValue, 1, 0);
        if (failGrant)
        {
            await Assert.ThrowsAsync<QuestFinishRejectedException>(async () => await bus.SendAsync(command));
            Assert.Equal(0, f.Scalar("SELECT grow_type FROM characters WHERE character_id=71"));
            Assert.Equal((int)f.Services.GetRequiredService<Dfo.Server.Abstractions.World.IExperienceRuleService>().CharacterScale().ExperienceForLevel(20), f.Scalar("SELECT exp FROM characters WHERE character_id=71"));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=71 AND quest_id=7840"));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=71"));
        }
        else
        {
            var outcome = await bus.SendAsync(command);
            Assert.True(outcome.Finished);
            Assert.Equal((byte?)4, outcome.GrowType);
            Assert.Equal(4, f.Scalar("SELECT grow_type FROM characters WHERE character_id=71"));
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=71 AND quest_id=7840"));
            Assert.Equal(2, f.Scalar("SELECT COUNT(DISTINCT page_index) FROM character_skills WHERE character_id=71"));
            var experience = f.Scalar("SELECT exp FROM characters WHERE character_id=71");
            Assert.False((await bus.SendAsync(command)).Finished);
            Assert.Equal(experience, f.Scalar("SELECT exp FROM characters WHERE character_id=71"));
        }
    }

    private static LearningFixture Fixture()
    {
        var content = new LearningContent();
        content.Definitions.Add(LearningContent.Definition(8) with { MaximumLevels = [10, 0] });
        content.Definitions[4] = content.Definitions[4] with { RequiredLevel = 25 };
        content.Grants.Add(new(7, 3, 1, 0));
        var rules = new OrdinaryQuestEffectTests.Rules();
        rules.Quest = rules.Quest with { ItemRewards = QuestItemRewardDefinition.Empty,
            Rewards = [new(QuestRewardKind.GrowType,[1])] };
        return new(content: content, configure: services =>
        {
            services.AddSingleton<Dfo.Server.Abstractions.World.IExperienceRuleService>(new OrdinaryQuestEffectTests.FixedProgression(Enumerable.Range(1,85).Select(x => (uint)x * 1000000).ToArray()));
            services.AddSingleton<ICharacterStatDefinitionSource>(new Stats());
            services.AddSingleton<IQuestContentRuleService>(rules);
        });
    }
    internal static void Prepare(LearningFixture f)
    {
        f.Sql("INSERT OR IGNORE INTO character_stats(character_id,carried_gold) VALUES(71,0)");
        var bag = new PersistentItemContainer(ContainerId.ForCharacter(new(71),InventoryContainerType.Main),new(InventoryContainerType.Main,0),f.Services.GetRequiredService<IItemDomainStore>(),
            f.Services.GetRequiredService<IDomainTransactionAccessor>(),f.Services.GetRequiredService<IItemPlacementRuleService>(),f.Services.GetRequiredService<IItemSlotRuleService>());
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(new(71),bag));
    }
    private static async ValueTask<Dfo.Server.Abstractions.Effects.QuestCompletionReceipt> Complete(LearningFixture f,int account=1,byte grow=1)
    {
        Prepare(f);
        var rules=(OrdinaryQuestEffectTests.Rules)f.Services.GetRequiredService<IQuestContentRuleService>();
        rules.Quest=rules.Quest with {Rewards=[new(QuestRewardKind.GrowType,[grow])]};
        f.Sql("INSERT OR IGNORE INTO character_active_quests VALUES(71,0,2001,0)");
        return await f.Services.GetRequiredService<Dfo.Server.Abstractions.Settlement.IQuestCompletionSettlementDomainService>()
            .FinishAsync(new(account),new(71),2001,ushort.MaxValue,1,DateTimeOffset.UnixEpoch,default);
    }
    private sealed class Stats : ICharacterStatDefinitionSource
    {
        public ValueTask<CharacterBaseStats> GetInitialAsync(byte job, CancellationToken cancellationToken)
            => ValueTask.FromResult(new CharacterStatValues(HpMax: 100).ToFixedPoint());
        public ValueTask<IReadOnlyList<CharacterStatDefinition>> GetAllAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<CharacterStatDefinition>>([new(12, new(HpMax: 100),
                new Dictionary<CharacterCareer, CharacterStatValues> { [new(12)] = new(HpMax: 1), [new(12, 1)] = new(HpMax: 2) })]);
    }
}
