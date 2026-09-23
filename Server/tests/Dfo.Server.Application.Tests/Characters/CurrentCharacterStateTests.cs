using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Persistence;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Protocol.Game.TutorialLevelUp;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class CurrentCharacterStateTests
{
    private static readonly CharacterId Id = new(71);
    private static readonly AccountId Account = new(1);

    [SkippableFact]
    public async Task Same_character_reads_transaction_changes_rollback_commit_and_reload()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var character = fixture.Character;
        var baseline = await ReadAsync(character);
        var baselineStat = Assert.IsType<CharacterBaseStats>(baseline.Stats);
        Assert.Equal(16, baseline.Progress!.Level);
        Assert.Same(fixture.Stats.GetStats(new CharacterCareer(0, 1), 16), baselineStat);
        Assert.Equal(2, baseline.Learned!.Count);
        Assert.Equal(2, Assert.Single(baseline.Learned[0].Skills).Level);
        Assert.True(baseline.Ledger!.IsComplete);

        await using (var rollback = fixture.Scope.Begin())
        {
            await fixture.ChangeAsync();
            await AssertChangedAsync(fixture, character, baselineStat);
        }
        var rolledBack = await ReadAsync(character);
        Assert.Same(baselineStat, rolledBack.Stats);
        Assert.Equal(baseline.Progress, rolledBack.Progress);
        Assert.Equal(baseline.Ledger.For(new(0)).Sp, rolledBack.Ledger!.For(new(0)).Sp);
        Assert.Equal(2, Assert.Single(rolledBack.Learned![0].Skills).Level);
        Assert.True(rolledBack.Ledger.IsComplete);
        Assert.Equal(1, (await fixture.Entry.LoadAsync(Account, Id, default)).Public.Extras.SkillTreeIndex);

        await using (var committed = fixture.Scope.Begin())
        {
            await fixture.ChangeAsync();
            await AssertChangedAsync(fixture, character, baselineStat);
            await committed.CommitAsync(default);
        }
        await AssertChangedAsync(fixture, character, baselineStat);
        await using var reloaded = new Fixture(fixture.Path);
        var afterReload = await ReadAsync(reloaded.Character);
        var afterCommit = await ReadAsync(character);
        Assert.Equal(afterCommit.Stats, afterReload.Stats);
        Assert.Equal(afterCommit.Progress, afterReload.Progress);
        Assert.Equal(afterCommit.Ledger!.For(new(0)).Sp, afterReload.Ledger!.For(new(0)).Sp);
        Assert.Equal(afterCommit.Ledger.For(new(1)).Tp, afterReload.Ledger.For(new(1)).Tp);
        Assert.Equal([999999], afterReload.Ledger.MissingSkillIds);
        Assert.Equal(70002, afterReload.Ledger.For(new(0)).Tp);
        Assert.Equal(65535, (await reloaded.Character.RemainingSkillPointsAsync(default))!.Page0Tp);
        var stored = await reloaded.Services.GetRequiredService<ICharacterEntryStateRepository>().LoadAsync(Account, Id, default);
        Assert.Equal(17, stored.Profile.DerivedStats.HpMaximum);
        Assert.Equal(4, stored.Profile.DerivedStats.Progress1);
        Assert.Equal(73, stored.Profile.DerivedStats.CurrentHpMpPercent);
        Assert.Equal(1, stored.Profile.SkillTreeIndex);
        Assert.Equal([9, 8], stored.Skill.Skills.Single(row => row.PageIndex == 0 && row.Slot == 7).ExtraValues);
    }

    [SkippableFact]
    public async Task Selection_and_refresh_replace_only_current_base_facts_preserving_page_and_inventory_configuration()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var entry = await fixture.Entry.LoadAsync(Account, Id, default);
        Assert.Equal((await fixture.Character.BaseStatsAsync(default))!.HpMax, entry.Public.DerivedStats.HpMaximum);
        Assert.NotEqual(17, entry.Public.DerivedStats.HpMaximum);
        Assert.Equal(4, entry.Public.DerivedStats.Progress1);
        Assert.Equal(73, entry.Public.DerivedStats.CurrentHpMpPercent);
        var stale = await CharacterEntryInventoryTests.CreateLifecycleResultAsync(Account, Id.Value, 0);
        var equipment = new InventoryContainerState(InventoryContainerType.Equipment, 24,
            [new InventoryItemState(new ItemIdentity(456), 2, StackableItemCore.Create(2, 9001, 1))]);
        stale = stale with
        {
            CharacterEntry = entry,
            CharacterInventory = stale.CharacterInventory with { Containers = [equipment] },
        };
        await using var transaction = fixture.Scope.Begin();
        await fixture.ChangeAsync();
        var refreshed = await fixture.Services.GetRequiredService<ICharacterStateProjectionDomainService>()
            .RefreshAsync(stale, default);
        Assert.Equal(51, refreshed.Character.Progression.Level);
        Assert.Equal(123456u, refreshed.Character.Progression.Experience);
        Assert.Equal(0x12, refreshed.Character.Identity.GrowType);
        Assert.Equal(123456, refreshed.CharacterEntry.Public.Extras.CharacterExperience);
        Assert.Equal((await fixture.Character.BaseStatsAsync(default))!.HpMax, refreshed.CharacterEntry.Public.DerivedStats.HpMaximum);
        Assert.Equal(4, refreshed.CharacterEntry.Public.DerivedStats.Progress1);
        Assert.Equal(73, refreshed.CharacterEntry.Public.DerivedStats.CurrentHpMpPercent);
        Assert.Same(stale.CharacterEntry.Public.Identity, refreshed.CharacterEntry.Public.Identity);
        Assert.Same(stale.CharacterEntry.Settings, refreshed.CharacterEntry.Settings);
        Assert.Same(stale.CharacterInventory, refreshed.CharacterInventory);
        Assert.Same(equipment, Assert.Single(refreshed.CharacterInventory.Containers));
        Assert.Same(stale.World, refreshed.World);
        Assert.Equal(1, refreshed.CharacterEntry.Public.Extras.SkillTreeIndex);
        var page = refreshed.CharacterEntry.Skills.Pages.Single(page => page.PageIndex == 0);
        var skill = page.Skills.Single(skill => skill.SkillId == 1);
        Assert.Equal(7, skill.Slot);
        Assert.Equal(255, skill.Level);
        Assert.Equal([9, 8], skill.ExtraValues);
        Assert.Equal(0, refreshed.CharacterEntry.Skills.ProtocolMirror!.Page0Sp);
        Assert.Equal(65535, refreshed.CharacterEntry.Skills.ProtocolMirror.Page1Tp);
        Assert.Equal(16u, entry.Progression.DailyChallengeCharacterLevel);
    }

    [SkippableFact]
    public async Task Tutorial_grant_checks_the_actual_row_inside_the_command_transaction()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var handler = new GrantTutorialLevelCommandHandler(
            fixture.Services.GetRequiredService<ICharacterExperienceDomainService>(),
            fixture.Services.GetRequiredService<ICharacterDomainService>());
        await using var transaction = fixture.Scope.Begin();
        await fixture.SqlAsync("UPDATE characters SET level=5,exp=700 WHERE character_id=71;");
        var result = await handler.HandleAsync(null!, new GrantTutorialLevelCommand(Id), default);
        Assert.Equal(TutorialLevelUpStatus.AlreadyPast, result.Status);
        Assert.Equal(new CharacterProgressionState(5, 700), await fixture.Character.ProgressAsync(default));
    }

    private static async Task AssertChangedAsync(Fixture fixture, ICharacter character, CharacterBaseStats baseline)
    {
        var state = await ReadAsync(character);
        Assert.Equal(new CharacterProgressionState(51, 123456), state.Progress);
        Assert.Same(fixture.Stats.GetStats(new CharacterCareer(0, 2, 1), 51), state.Stats);
        Assert.Same(baseline, fixture.Stats.GetStats(new CharacterCareer(0, 1), 16));
        Assert.NotEqual(baseline, state.Stats);
        Assert.Equal(255, state.Learned![0].Skills.Single(skill => skill.SkillId == 1).Level);
        Assert.Equal(1, Assert.Single(state.Learned[1].Skills).Level);
        Assert.Contains(state.Learned[0].Skills, skill => skill.SkillId == 999999 && skill.Level == 2);
        Assert.False(state.Ledger!.IsComplete);
        Assert.Equal([999999], state.Ledger.MissingSkillIds);
        Assert.True(state.Ledger.For(new(0)).Sp < 0);
        Assert.True(state.Ledger.For(new(0)).Tp > ushort.MaxValue);
        Assert.Equal(0, (await character.RemainingSkillPointsAsync(default))!.Page0Sp);
        var pages = (await fixture.Services.GetRequiredService<ICharacterSkillStateDomainService>().ReadAsync(Id, default))!.Pages;
        Assert.Equal([9, 8], pages[0].Skills.Single(skill => skill.SkillId == 1).ExtraValues);
        Assert.Equal(15, Assert.Single(pages[1].Skills).Slot);
    }

    private static async Task<(CharacterBaseStats? Stats, CharacterProgressionState? Progress,
        IReadOnlyList<CharacterLearnedSkillPage>? Learned, CharacterSkillPointLedger? Ledger)> ReadAsync(ICharacter character) =>
        (await character.BaseStatsAsync(default), await character.ProgressAsync(default),
            await character.LearnedSkillsAsync(default), await character.SkillPointLedgerAsync(default));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly bool ownsPath;
        public string Path { get; }
        public ServiceProvider Services { get; }
        public DomainTransactionScope Scope => Services.GetRequiredService<DomainTransactionScope>();
        public ICharacter Character { get; }
        public ICharacterStatRuleService Stats => Services.GetRequiredService<ICharacterStatRuleService>();
        public ICharacterEntryStateDomainService Entry => Services.GetRequiredService<ICharacterEntryStateDomainService>();
        public Fixture(string? existing = null)
        {
            ownsPath = existing is null;
            Path = existing ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dfo-current-character-{Guid.NewGuid():N}.db");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Database:Path"] = Path }).Build();
            var services = new ServiceCollection().AddLogging();
            new InfrastructureModule().ConfigureServices(services, configuration);
            new PersistenceModule().ConfigureServices(services, configuration);
            new ApplicationModule().ConfigureServices(services, configuration);
            Services = services.BuildServiceProvider();
            _ = Services.GetRequiredService<SqliteDatabase>();
            Character = Services.GetRequiredService<ICharacterDomainService>().Get(Id);
        }
        public async Task SeedAsync()
        {
            await using var transaction = Scope.Begin();
            await SqlAsync("""
                INSERT INTO accounts(account_id,m_id) VALUES(1,'current');
                INSERT INTO characters(character_id,account_id,name,name_bytes,job,grow_type,level,exp,bonus_sp,bonus_tp,skill_tree_index)
                VALUES(71,1,'current',X'4142',0,1,16,1234,5,3,1);
                INSERT INTO character_condition(character_id,pc_room_state) VALUES(71,0);
                INSERT INTO character_stats(character_id,stat_hp_max,progress1,progress2,current_hp_mp_percent) VALUES(71,17,4,5,73);
                INSERT INTO character_pvp(character_id) VALUES(71);
                INSERT INTO character_settings(character_id) VALUES(71);
                INSERT INTO character_expert_job(character_id) VALUES(71);
                INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level,extra_values)
                VALUES(71,'pve',0,7,1,2,X'0908'),(71,'pve',1,15,1,1,X'07');
                """);
            await transaction.CommitAsync(default);
        }
        public ValueTask ChangeAsync() => SqlAsync("""
            UPDATE characters SET grow_type=18,level=51,exp=123456,bonus_sp=19,bonus_tp=70000 WHERE character_id=71;
            UPDATE character_skills SET level=255 WHERE character_id=71 AND mode='pve' AND page_index=0 AND slot=7;
            INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) VALUES(71,'pve',0,8,999999,2),(71,'pve',0,9,64,255);
            """);
        public ValueTask SqlAsync(string sql) => Services.GetRequiredService<SqliteDatabase>().WriteAsync(async (command, token) =>
        {
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(token);
        }, default);
        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            if (ownsPath)
            {
                SqliteConnection.ClearPool(new SqliteConnection($"Data Source={Path}"));
                File.Delete(Path);
            }
        }
    }
}
