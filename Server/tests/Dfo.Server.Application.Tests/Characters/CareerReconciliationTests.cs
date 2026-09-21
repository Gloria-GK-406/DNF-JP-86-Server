using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Characters;

public sealed class CareerReconciliationTests
{
    [Fact]
    public async Task Advancement_reconciles_both_pages_without_losing_paid_levels_or_configuration()
    {
        await using var f = Fixture();
        await f.SeedAsync();
        f.Sql("UPDATE characters SET level=20 WHERE character_id=71; INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) VALUES(71,'pve',0,8,8,1),(71,'pve',1,8,8,1),(71,'pve',0,9,5,1)");
        await using (var tx = f.Scope.Begin())
        {
            await f.Services.GetRequiredService<ICharacterCareerDomainService>().AdvanceAsync(new(1), new(71), 1, default);
            await f.State.ReconcileAsync(new(71), default);
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
        var kept = facts.Pages[0].Skills.Single(s => s.SkillId == 1);
        Assert.Equal(2, kept.Level);
        Assert.Equal(7, kept.Slot);
        Assert.Equal(new byte[] {9,8}, kept.ExtraValues);
        Assert.Equal(1, facts.Pages[0].Skills.Single(s => s.SkillId == 5).Level);
        Assert.Equal(1000 + 5 - (10 + 20) - 20, f.Rules.GetLedger(facts.Career, facts.Level, facts.Pages, facts.PointBasis).Page0Sp);
        var count = f.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=71");
        await using (var tx = f.Scope.Begin()) { await f.State.ReconcileAsync(new(71), default); await tx.CommitAsync(default); }
        Assert.Equal(count, f.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=71"));
        Assert.Equal(0, f.Scalar("SELECT grow_type FROM characters WHERE character_id=72"));
    }

    [Fact]
    public async Task Unmodelled_learned_skills_keep_their_level_slot_and_configuration()
    {
        await using var f = Fixture(); await f.SeedAsync();
        f.Sql("INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level,extra_values) VALUES(71,'pve',0,0,999,3,X'0102')");
        await using (var tx = f.Scope.Begin())
        {
            await f.Services.GetRequiredService<ICharacterCareerDomainService>().AdvanceAsync(new(1), new(71), 1, default);
            await f.State.ReconcileAsync(new(71), default);
            await tx.CommitAsync(default);
        }
        var facts = (await f.State.ReadAsync(new(71), default))!;
        var unknown = Assert.Single(facts.Pages[0].Skills, s => s.SkillId == 999);
        Assert.Equal(0, unknown.Slot);
        Assert.Equal(3, unknown.Level);
        Assert.Equal(new byte[] {1,2}, unknown.ExtraValues);
        Assert.Equal(facts.Pages[0].Skills.Count, facts.Pages[0].Skills.Select(s => s.Slot).Distinct().Count());
        Assert.False(f.Rules.GetLedger(facts.Career, facts.Level, facts.Pages, facts.PointBasis).IsComplete);
    }

    [Fact]
    public async Task A_failed_skill_write_rolls_back_career_and_prior_skill_writes()
    {
        await using var f = Fixture(); await f.SeedAsync();
        f.Sql("CREATE TRIGGER fail_grant BEFORE INSERT ON character_skills WHEN NEW.character_id=71 AND NEW.skill_id=7 BEGIN SELECT RAISE(ABORT,'injected grant failure'); END;");
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var tx = f.Scope.Begin();
            await f.Services.GetRequiredService<ICharacterCareerDomainService>().AdvanceAsync(new(1), new(71), 1, default);
            await tx.CommitAsync(default);
        });
        Assert.Equal(0, f.Scalar("SELECT grow_type FROM characters WHERE character_id=71"));
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=71"));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 15)]
    public async Task Invalid_owner_or_career_leaves_character_unchanged(int account, byte grow)
    {
        await using var f = Fixture(); await f.SeedAsync();
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var tx = f.Scope.Begin();
            await f.Services.GetRequiredService<ICharacterCareerDomainService>().AdvanceAsync(new(account), new(71), grow, default);
            await tx.CommitAsync(default);
        });
        Assert.Equal(0, f.Scalar("SELECT grow_type FROM characters WHERE character_id=71"));
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
        f.Sql("UPDATE characters SET job=2,level=20 WHERE character_id=71; DELETE FROM character_skills WHERE character_id=71; INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) VALUES(71,0,7840,0)");
        if (failGrant)
            f.Sql("CREATE TRIGGER fail_grant BEFORE INSERT ON character_skills WHEN NEW.character_id=71 BEGIN SELECT RAISE(ABORT,'injected grant failure'); END;");
        var bus = f.Services.GetRequiredService<ICommandBus>();
        var command = new FinishQuestCommand(new(1), new(71), 7840, ushort.MaxValue, 1, 0);
        if (failGrant)
        {
            await Assert.ThrowsAsync<QuestFinishRejectedException>(async () => await bus.SendAsync(command));
            Assert.Equal(0, f.Scalar("SELECT grow_type FROM characters WHERE character_id=71"));
            Assert.Equal(0, f.Scalar("SELECT exp FROM characters WHERE character_id=71"));
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
        return new(content: content, configure: services => services.AddSingleton<ICharacterStatDefinitionSource>(new Stats()));
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
