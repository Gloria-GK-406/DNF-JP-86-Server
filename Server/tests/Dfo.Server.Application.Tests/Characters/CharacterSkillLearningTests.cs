using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Infrastructure;
using Dfo.Server.Infrastructure.Content;
using Dfo.Server.Persistence;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class CharacterSkillLearningTests
{
    [Fact]
    public async Task Multiple_levels_commit_with_current_costs_and_preserve_existing_configuration_on_reload()
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        var result = await fixture.LearnAsync([new(1, 0, 2), new(2, 0, 3)]);
        Assert.Equal(CharacterSkillLearningRejection.None, result.Rejection);
        Assert.NotNull(result.Receipt);
        Assert.Equal([4, 3], result.Receipt.Skills.Select(skill => (int)skill.Level));
        Assert.Equal([7, 0], result.Receipt.Skills.Select(skill => (int)skill.Slot));
        Assert.Equal([9, 8], result.Receipt.Skills[0].ExtraValues);
        Assert.Empty(result.Receipt.Skills[1].ExtraValues);
        Assert.Equal(1005 - 90 - 60, result.Receipt.Ledger.For(new(0)).Sp);
        await using var reloaded = new LearningFixture(fixture.Path);
        var facts = (await reloaded.State.ReadAsync(new(71), default))!;
        Assert.Equal(4, facts.Pages[0].Skills.Single(skill => skill.SkillId == 1).Level);
        Assert.Equal([9, 8], facts.Pages[0].Skills.Single(skill => skill.SkillId == 1).ExtraValues);
        Assert.Equal(1, Assert.Single(facts.Pages[1].Skills).Level);
        Assert.Equal([7], Assert.Single(facts.Pages[1].Skills).ExtraValues);
        Assert.Equal(result.Receipt.Ledger.For(new(0)).Sp,
            reloaded.Rules.GetLedger(facts.Career, facts.Level, facts.Pages, facts.PointBasis).For(new(0)).Sp);
        Assert.Equal(8, reloaded.Scalar("SELECT level FROM character_skills WHERE character_id=72"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Prerequisites_are_checked_against_the_whole_final_batch(bool reverse)
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        CharacterSkillLearningChange[] changes = [new(3, 0, 2), new(2, 0, 3)];
        var result = await fixture.LearnAsync(reverse ? changes.Reverse().ToArray() : changes);
        Assert.Equal(CharacterSkillLearningRejection.None, result.Rejection);
        Assert.Equal(3, fixture.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND page_index=0 AND skill_id=2"));
        Assert.Equal(2, fixture.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND page_index=0 AND skill_id=3"));
        Assert.Equal(875, result.Receipt!.Ledger.For(new(0)).Sp);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 2, 1)]
    [InlineData(1, 0, 0)]
    public async Task Invalid_changes_leave_all_skills_unchanged(ushort skill, byte refund, byte increment)
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        Assert.Equal(CharacterSkillLearningRejection.InvalidRequest,
            (await fixture.LearnAsync([new(2, 0, 1), new(skill, refund, increment)])).Rejection);
        AssertBaseline(fixture);
    }

    [Fact]
    public async Task Empty_duplicate_oversized_and_unknown_mode_requests_do_not_learn()
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        Assert.Equal(CharacterSkillLearningRejection.InvalidRequest, (await fixture.LearnAsync([])).Rejection);
        Assert.Equal(CharacterSkillLearningRejection.InvalidRequest,
            (await fixture.LearnAsync([new(1, 0, 1), new(1, 0, 1)])).Rejection);
        Assert.Equal(CharacterSkillLearningRejection.InvalidRequest,
            (await fixture.LearnAsync(Enumerable.Range(1, 256).Select(id => new CharacterSkillLearningChange((ushort)id, 0, 1)).ToArray())).Rejection);
        Assert.Equal(CharacterSkillLearningRejection.UnsupportedScenario,
            (await fixture.LearnAsync([new(1, 0, 1)], mode: 1)).Rejection);
        AssertBaseline(fixture);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 1)]
    [InlineData(7, 1)]
    [InlineData(99, 1)]
    [InlineData(1, 100)]
    [InlineData(1, 255)]
    public async Task Invalid_prerequisite_fixed_level_character_level_career_or_target_refuses_the_entire_batch(ushort id, byte increment)
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        Assert.Equal(CharacterSkillLearningRejection.SkillUnavailable,
            (await fixture.LearnAsync([new(2, 0, 1), new(id, 0, increment)])).Rejection);
        AssertBaseline(fixture);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Insufficient_sp_or_tp_refuses_the_whole_batch(bool tp)
    {
        await using var fixture = new LearningFixture(content: new LearningContent { SpBudget = tp ? 1000 : 40 });
        await fixture.SeedAsync();
        Assert.Equal(CharacterSkillLearningRejection.InsufficientPoints,
            (await fixture.LearnAsync(tp ? [new(2, 0, 1), new(6, 0, 22)] : [new(1, 0, 2)])).Rejection);
        AssertBaseline(fixture);
    }

    [Fact]
    public async Task Tp_skill_uses_a_normal_available_slot_and_charges_only_tp()
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        var result = await fixture.LearnAsync([new(6, 0, 3)]);
        Assert.Equal(CharacterSkillLearningRejection.None, result.Rejection);
        Assert.Equal(0, Assert.Single(result.Receipt!.Skills).Slot);
        Assert.Equal(975, result.Receipt.Ledger.For(new(0)).Sp);
        Assert.Equal(34, result.Receipt.Ledger.For(new(0)).Tp);
    }

    [Theory]
    [InlineData("UPDATE characters SET level=1 WHERE character_id=71", CharacterSkillLearningRejection.SkillUnavailable)]
    [InlineData("UPDATE characters SET job=0 WHERE character_id=71", CharacterSkillLearningRejection.SkillUnavailable)]
    [InlineData("UPDATE characters SET grow_type=15 WHERE character_id=71", CharacterSkillLearningRejection.SkillUnavailable)]
    [InlineData("UPDATE characters SET skill_tree_index=1 WHERE character_id=71", CharacterSkillLearningRejection.PageUnavailable)]
    [InlineData("UPDATE characters SET account_id=2 WHERE character_id=71", CharacterSkillLearningRejection.NoSession)]
    [InlineData("UPDATE characters SET delete_flag=1 WHERE character_id=71", CharacterSkillLearningRejection.NoSession)]
    [InlineData("UPDATE characters SET bonus_sp=-1000 WHERE character_id=71", CharacterSkillLearningRejection.InsufficientPoints)]
    public async Task Current_database_facts_override_the_entry_snapshot(string sql, CharacterSkillLearningRejection rejection)
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        fixture.Sql(sql);
        Assert.Equal(rejection, (await fixture.LearnAsync([new(2, 0, 3)])).Rejection);
        AssertBaseline(fixture);
    }

    [Fact]
    public async Task A_career_change_and_active_second_page_are_observed()
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        Assert.Equal(CharacterSkillLearningRejection.PageUnavailable,
            (await fixture.LearnAsync([new(1, 0, 1)], page: 1)).Rejection);
        fixture.Sql("UPDATE characters SET grow_type=1,skill_tree_index=1 WHERE character_id=71");
        var result = await fixture.LearnAsync([new(7, 0, 1), new(1, 0, 2)], page: 1);
        Assert.Equal(CharacterSkillLearningRejection.None, result.Rejection);
        Assert.Equal(2, fixture.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND page_index=0"));
        var existing = result.Receipt!.Skills.Single(skill => skill.SkillId == 1);
        Assert.Equal(15, existing.Slot);
        Assert.Equal([7], existing.ExtraValues);
    }

    [Fact]
    public async Task Stale_or_unpublished_sessions_and_unsupported_mode_or_absent_presence_cannot_learn()
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        var sessions = fixture.Services.GetRequiredService<ISelectedSessionDomainService>();
        // A generation that is not the current selection is refused.
        Assert.Equal(CharacterSkillLearningRejection.NoSession, (await fixture.LearnAsync([new(1, 0, 1)],
            session: fixture.Session with { ConnectionGeneration = fixture.Session.ConnectionGeneration + 1 })).Rejection);
        Assert.Equal(CharacterSkillLearningRejection.UnsupportedScenario,
            (await fixture.LearnAsync([new(1, 0, 1)], mode: 1)).Rejection);
        fixture.Services.GetRequiredService<ITownAreaDomainService>().LeaveExact(
            fixture.Session.CharacterId,
            fixture.Session.TownAreaLease(),
            TownAreaDepartureReason.SessionEnded);
        Assert.Equal(CharacterSkillLearningRejection.UnsupportedScenario, (await fixture.LearnAsync([new(1, 0, 1)])).Rejection);
        // Selected but no longer in the world.
        sessions.ReleaseExact(fixture.Session);
        sessions.Select(fixture.Session);
        Assert.Equal(CharacterSkillLearningRejection.NoSession, (await fixture.LearnAsync([new(1, 0, 1)])).Rejection);
        AssertBaseline(fixture);
    }

    [Theory]
    [InlineData("UPDATE character_skills SET extra_values=zeroblob(256) WHERE character_id=71 AND page_index=0", CharacterSkillLearningRejection.InvalidRequest)]
    [InlineData("UPDATE character_skills SET slot=255 WHERE character_id=71 AND page_index=0", CharacterSkillLearningRejection.InvalidRequest)]
    [InlineData("INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) VALUES(71,'pve',1,16,999,1)", CharacterSkillLearningRejection.IncompleteLedger)]
    public async Task Unrepresentable_configuration_and_incomplete_facts_refuse_before_writes(string sql, CharacterSkillLearningRejection rejection)
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        fixture.Sql(sql);
        Assert.Equal(rejection, (await fixture.LearnAsync([new(1, 0, 1)])).Rejection);
        Assert.Equal(2, fixture.Scalar("SELECT MAX(level) FROM character_skills WHERE character_id=71 AND page_index=0"));
    }

    [Fact]
    public async Task A_second_write_failure_rolls_back_the_first_write_in_real_sqlite()
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        fixture.Sql("CREATE TRIGGER fail_learning BEFORE INSERT ON character_skills WHEN NEW.skill_id=2 BEGIN SELECT RAISE(ABORT,'learning test'); END");
        await Assert.ThrowsAsync<SqliteException>(() => fixture.LearnAsync([new(1, 0, 2), new(2, 0, 1)]));
        AssertBaseline(fixture);
        await using var independent = new LearningFixture(fixture.Path);
        Assert.Equal(2, (await independent.State.ReadAsync(new(71), default))!.Pages[0].Skills[0].Level);
    }

    [Fact]
    public async Task A_missing_update_throws_and_rolls_back_an_earlier_insert()
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        fixture.Sql("CREATE TRIGGER ignore_learning BEFORE UPDATE ON character_skills BEGIN SELECT RAISE(IGNORE); END");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.LearnAsync([new(2, 0, 1), new(1, 0, 2)]));
        AssertBaseline(fixture);
    }

    [Fact]
    public async Task Learning_without_a_transaction_cannot_write()
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Services
            .GetRequiredService<ICharacterSkillLearningDomainService>()
            .LearnAsync(fixture.Session, 0, [new(1, 0, 1)], 0, default).AsTask());
        AssertBaseline(fixture);
    }

    [Fact]
    public async Task The_inactive_pages_negative_balance_does_not_spend_the_active_pages_budget()
    {
        await using var fixture = new LearningFixture();
        await fixture.SeedAsync();
        fixture.Sql("UPDATE character_skills SET level=255 WHERE character_id=71 AND page_index=1");
        var result = await fixture.LearnAsync([new(1, 0, 1)]);
        Assert.Equal(CharacterSkillLearningRejection.None, result.Rejection);
        Assert.True(result.Receipt!.Ledger.For(new(1)).Sp < 0);
        Assert.Equal(945, result.Receipt.Ledger.For(new(0)).Sp);
        Assert.Equal(255, fixture.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND page_index=1"));
    }

    [Fact]
    public async Task No_free_slot_refuses_learning_without_overwriting_or_using_a_sentinel()
    {
        var content = new LearningContent();
        var slots = Enumerable.Range(0, 6).Concat(Enumerable.Range(198, 6)).Concat(Enumerable.Range(150, 48)).ToArray();
        content.Definitions.AddRange(Enumerable.Range(20, slots.Length).Select(LearningContent.Definition));
        await using var fixture = new LearningFixture(content: content);
        await fixture.SeedAsync();
        for (var index = 0; index < slots.Length; index++)
            fixture.Sql($"INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) VALUES(71,'pve',0,{slots[index]},{20 + index},0)");
        Assert.Equal(CharacterSkillLearningRejection.NoSlot, (await fixture.LearnAsync([new(2, 0, 1)])).Rejection);
        Assert.Equal(62, fixture.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=71"));
        Assert.Equal(2, fixture.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND page_index=0 AND skill_id=1"));
    }

    [Theory]
    [InlineData(true, 0, 4, 0)]
    [InlineData(false, 0, 4, 6)]
    [InlineData(false, 0, 2, 54)]
    [InlineData(false, 4, 4, 102)]
    [InlineData(false, 99, 4, 6)]
    public async Task New_skill_slots_follow_activity_and_group_and_skip_occupied_slots(bool active, int group, int grows, int expected)
    {
        var content = new LearningContent();
        content.Definitions[1] = content.Definitions[1] with { IsActive = active, RawGroup = group, NumberOfGrowTypes = grows };
        await using var fixture = new LearningFixture(content: content);
        await fixture.SeedAsync();
        var result = await fixture.LearnAsync([new(2, 0, 1)]);
        Assert.Equal(expected, Assert.Single(result.Receipt!.Skills).Slot);
        Assert.Equal(expected, fixture.Scalar("SELECT slot FROM character_skills WHERE character_id=71 AND skill_id=2"));
    }

    [Theory]
    [InlineData(false, 198)]
    [InlineData(true, 150)]
    public async Task Full_hotkey_bands_fall_back_to_the_next_available_band(bool fillBoth, int expected)
    {
        var content = new LearningContent();
        var slots = Enumerable.Range(0, 6).Concat(fillBoth ? Enumerable.Range(198, 6) : []).ToArray();
        content.Definitions.AddRange(Enumerable.Range(20, slots.Length).Select(LearningContent.Definition));
        await using var fixture = new LearningFixture(content: content);
        await fixture.SeedAsync();
        for (var index = 0; index < slots.Length; index++)
            fixture.Sql($"INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level) VALUES(71,'pve',0,{slots[index]},{20 + index},0)");
        var result = await fixture.LearnAsync([new(2, 0, 1)]);
        Assert.Equal(expected, Assert.Single(result.Receipt!.Skills).Slot);
    }

    [Fact]
    public async Task An_id_in_the_special_wire_lookup_range_can_still_learn_in_a_normal_slot()
    {
        var content = new LearningContent();
        content.Definitions.Add(LearningContent.Definition(200));
        await using var fixture = new LearningFixture(content: content);
        await fixture.SeedAsync();
        var result = await fixture.LearnAsync([new(200, 0, 1)]);
        Assert.Equal(CharacterSkillLearningRejection.None, result.Rejection);
        Assert.Equal(0, Assert.Single(result.Receipt!.Skills).Slot);
    }

    [SkippableFact]
    public async Task Real_guardian_archive_initial_skills_support_learning_one_to_three_and_reloading()
    {
        Skip.If(new PvfContentOptions().ResolveArchivePath() is null, "PVF_ARCHIVE_PATH is not available.");
        await using var fixture = new LearningFixture(useArchive: true);
        await fixture.SeedAsync();
        var initial = await fixture.Services.GetRequiredService<ICharacterCreationRuleService>().StartingStateAsync(12, default);
        Assert.Equal(1, initial.Skills.Single(skill => skill.SkillId == 1).Level);
        fixture.Sql("DELETE FROM character_skills WHERE character_id=71; UPDATE characters SET bonus_sp=0,bonus_tp=0 WHERE character_id=71");
        await using (var transaction = fixture.Scope.Begin())
        {
            var table = fixture.Services.GetRequiredService<ICharacterSkillTable>();
            foreach (var page in new[] { 0, 1 })
                foreach (var skill in initial.Skills)
                    await table.InsertAsync(new(71), page, skill.Slot, (int)skill.SkillId, skill.Level, default);
            await transaction.CommitAsync(default);
        }
        var before = (await fixture.State.ReadAsync(new(71), default))!;
        var oldLedger = fixture.Rules.GetLedger(before.Career, before.Level, before.Pages, before.PointBasis);
        Assert.True(oldLedger.IsComplete);
        var result = await fixture.LearnAsync([new(1, 0, 2)]);
        Assert.Equal(CharacterSkillLearningRejection.None, result.Rejection);
        Assert.Equal(3, Assert.Single(result.Receipt!.Skills).Level);
        Assert.True(result.Receipt.Ledger.IsComplete);
        var skillRule = fixture.Rules.Get(12, 1)!;
        Assert.Equal(oldLedger.For(new(0)).Sp - (skillRule.GetSP(3) - skillRule.GetSP(1)), result.Receipt.Ledger.For(new(0)).Sp);
        await using var independent = new LearningFixture(fixture.Path, useArchive: true);
        var reloaded = (await independent.State.ReadAsync(new(71), default))!;
        Assert.Equal(3, reloaded.Pages[0].Skills.Single(skill => skill.SkillId == 1).Level);
        Assert.Equal(result.Receipt.Ledger.For(new(0)).Sp,
            independent.Rules.GetLedger(reloaded.Career, reloaded.Level, reloaded.Pages, reloaded.PointBasis).For(new(0)).Sp);
    }

    private static void AssertBaseline(LearningFixture fixture)
    {
        Assert.Equal(2, fixture.Scalar("SELECT level FROM character_skills WHERE character_id=71 AND page_index=0 AND skill_id=1"));
        Assert.Equal(2, fixture.Scalar("SELECT COUNT(*) FROM character_skills WHERE character_id=71"));
        Assert.Equal(8, fixture.Scalar("SELECT level FROM character_skills WHERE character_id=72"));
    }
}

internal sealed class LearningFixture : IAsyncDisposable
{
    private readonly bool ownsPath;
    public string Path { get; }
    public ServiceProvider Services { get; }
    public SelectedSessionIdentity Session { get; private set; } = null!;
    public DomainTransactionScope Scope => Services.GetRequiredService<DomainTransactionScope>();
    public ICharacterSkillStateDomainService State => Services.GetRequiredService<ICharacterSkillStateDomainService>();
    public ICharacterSkillRuleService Rules => Services.GetRequiredService<ICharacterSkillRuleService>();
    public LearningContent Content { get; }
    private string ConnectionString => new SqliteConnectionStringBuilder { DataSource = Path }.ToString();
    public LearningFixture(string? existing = null, LearningContent? content = null, bool useArchive = false,
        Action<IServiceCollection>? configure = null)
    {
        ownsPath = existing is null;
        Path = existing ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"dfo-learning-{Guid.NewGuid():N}.db");
        Content = content ?? new LearningContent();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Database:Path"] = Path }).Build();
        var services = new ServiceCollection().AddLogging();
        new InfrastructureModule().ConfigureServices(services, configuration);
        new PersistenceModule().ConfigureServices(services, configuration);
        new ApplicationModule().ConfigureServices(services, configuration);
        services.AddSingleton<ISelectedCharacterCurrentViewDomainService, MinimalCurrentView>();
        if (!useArchive)
        {
            services.AddSingleton<ISkillRuleDefinitionSource>(Content);
            services.AddSingleton<Dfo.Server.Abstractions.Content.IDungeonExperienceProgressionPolicySource>(new Dfo.Server.Application.Tests.Support.TestProgressionSource());
        }
        configure?.Invoke(services);
        Services = services.BuildServiceProvider();
        _ = Services.GetRequiredService<SqliteDatabase>();
    }
    public async Task SeedAsync()
    {
        Sql($"""
            INSERT INTO accounts(account_id,m_id) VALUES(1,'learning'),(2,'other');
            INSERT INTO characters(character_id,account_id,name,job,grow_type,level,exp,bonus_sp,bonus_tp,skill_tree_index)
            VALUES(71,1,'learning',12,0,85,{Services.GetRequiredService<Dfo.Server.Abstractions.World.IExperienceRuleService>().CharacterScale().ExperienceForLevel(85)},5,3,-1),(72,2,'other',12,0,85,{Services.GetRequiredService<Dfo.Server.Abstractions.World.IExperienceRuleService>().CharacterScale().ExperienceForLevel(85)},0,0,-1);
            INSERT INTO character_skills(character_id,mode,page_index,slot,skill_id,level,extra_values)
            VALUES(71,'pve',0,7,1,2,X'0908'),(71,'pve',1,15,1,1,X'07'),(72,'pve',0,7,1,8,X'06');
            """);
        var registry = Services.GetRequiredService<OnlineStateRegistry>();
        var connection = new NetworkConnectionContext(new("learning-test"), NetworkEndpointKind.Game, 10011);
        var user = registry.RegisterConnection(connection).OnlineUserId;
        registry.Authenticate(user, new(1));
        Session = SelectedSessionIdentity.Create(new(1), new(71), connection.ConnectionId,
            registry.GetRequiredUser(user).Generation, 10011);
        var sessions = Services.GetRequiredService<ISelectedSessionDomainService>();
        sessions.Select(Session);
        sessions.EnterWorld(Session);
        Services.GetRequiredService<ITownAreaDomainService>().Enter(new TownAreaMember(
            Session.CharacterId,
            Session.TownAreaLease(),
            new TownAreaKey(1, 0, 0),
            new CharacterPositionState(0, 0, 0, 0, 0),
            true));
    }

    private sealed class MinimalCurrentView(ISelectedSessionDomainService sessions)
        : ISelectedCharacterCurrentViewDomainService
    {
        public ValueTask<SelectCharacterResult?> TryLoadAsync(
            SelectedSessionIdentity session, CancellationToken cancellationToken) =>
            ValueTask.FromResult<SelectCharacterResult?>(sessions.IsInWorld(session)
                ? new SelectCharacterResult(
                    null!, null!, null!, null!, null!,
                    new GameWorldEntryState(DateTimeOffset.UnixEpoch, null!,
                        new ChannelEntryPolicyState(11, null!, true, false)), null!)
                : null);
    }
    public async Task<CharacterSkillLearningResult> LearnAsync(IReadOnlyList<CharacterSkillLearningChange> changes,
        byte page = 0, byte mode = 0, SelectedSessionIdentity? session = null)
    {
        await using var transaction = Scope.Begin();
        var result = await Services.GetRequiredService<ICharacterSkillLearningDomainService>()
            .LearnAsync(session ?? Session, page, changes, mode, default);
        await transaction.CommitAsync(default);
        return result;
    }
    public int Scalar(string sql)
    {
        using var db = new SqliteConnection(ConnectionString);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }
    public void Sql(string sql)
    {
        using var db = new SqliteConnection(ConnectionString);
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        if (ownsPath)
        {
            using var connection = new SqliteConnection(ConnectionString);
            SqliteConnection.ClearPool(connection);
            File.Delete(Path);
        }
    }
}

internal sealed class LearningContent : ISkillRuleDefinitionSource
{
    public List<SkillRuleDefinition> Definitions { get; } =
    [
        Definition(1) with { Costs = [10, 20, 30] },
        Definition(2),
        Definition(3) with { Prerequisites = [new(2, 3)] },
        Definition(4) with { IsFixedLevel = true, FixedLevelBase = 1 },
        Definition(5) with { RequiredLevel = 86 },
        Definition(6) with { UsesTp = true, Costs = [1, 2] },
        Definition(7) with { MaximumLevels = [0, 10] },
    ];
    public List<SkillFreeGrant> Grants { get; } = [];
    public int SpBudget { get; set; } = 1000;
    public SkillRuleCatalog Get(byte job) => new(Definitions.ToDictionary(value => value.SkillId),
        Grants, [(1, SpBudget)], new HashSet<CharacterCareer> { new(12), new(12, 1) });
    public static SkillRuleDefinition Definition(int id) => new(id, $"skill-{id}", string.Empty,
        false, [20], [100], [], [], 1, 1, [], false, 0, 1, 0, true, 0, 4);
}
