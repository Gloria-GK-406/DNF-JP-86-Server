using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Guilds;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.World;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Guilds;
using Dfo.Server.Persistence.Inventory;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dfo.Server.Persistence.Tests.Guilds;

public sealed class GuildDomainTests : IDisposable
{
    private const int Fee = 300_000;
    private readonly string path = Path.Combine(Path.GetTempPath(), $"dfo-guild-{Guid.NewGuid():N}.db");
    private readonly DomainTransactionScope scope = TestRequired.TransactionScope();
    private readonly SqliteCharacterTable characters;
    private readonly GuildDomainService guilds;
    private readonly GuildQueryDomainService query;
    private readonly CreationCost creationCost = new();

    public GuildDomainTests()
    {
        var database = new SqliteDatabase(path, scope);
        var store = new SqliteGuildDomainStore(database);
        characters = new(database);
        var players = new CharacterDomainService(new SqliteCharacterCarriedGoldTable(database),
            TestRequired.Absent<ICarriedGoldRuleService>(), characters, TestRequired.Absent<ICharacterSkillPointDomainService>(),
            TestRequired.Absent<ICharacterStatRuleService>(), TestRequired.Absent<ICharacterSkillStateDomainService>(),
            TestRequired.Absent<IExperienceRuleService>(), TestRequired.Absent<IExperienceCapsuleDomainService>(),
            scope, new DiscardingEventService());
        guilds = new(store, new(creationCost), players, characters, TimeProvider.System);
        query = new(store);
        Execute("""
            INSERT INTO accounts(account_id,m_id) VALUES(1,'guild-test');
            INSERT INTO characters(character_id,account_id,name) VALUES
                (11,1,'Leader'),(12,1,'Other'),(13,1,'Applicant'),(14,1,'Member'),(15,1,'Pending'),(16,1,'Deleted');
            INSERT INTO character_stats(character_id,carried_gold) VALUES
                (11,900000),(12,900000),(13,100),(16,900000);
            UPDATE characters SET delete_flag=1 WHERE character_id=16;
            """);
    }

    [Fact]
    public async Task Creation_permit_is_bound_to_the_exact_selected_session_and_cleared_by_cancel()
    {
        var recruitment = new GuildRecruitmentSessionDomainService(guilds, new(creationCost));
        var original = SelectedSessionIdentity.Create(new(1), new(11), new("connection"), 1, 10011);
        var replacement = SelectedSessionIdentity.Create(new(1), new(11), new("connection"), 2, 10011);
        await TestGameplay.RunAsync(async () =>
        {
            Assert.Equal(GuildMutationStatus.Success, await recruitment.StartAsync(original, "Iris", default));
            Assert.False(recruitment.Authorize(original));
            Assert.True(recruitment.SetPromotion(original, ""));
            Assert.True(recruitment.Authorize(original));
            Assert.NotNull(recruitment.AuthorizedCreation(original));
            Assert.Null(recruitment.AuthorizedCreation(replacement));
            recruitment.Cancel(original);
            Assert.Null(recruitment.AuthorizedCreation(original));
        });
    }

    [Fact]
    public async Task Creates_one_guild_and_leader_for_one_payment()
    {
        var created = await Write(() => guilds.CreateAsync(new(11), "Iris", "Hello", default));
        Assert.Equal(GuildMutationStatus.Success, created.Status);
        Assert.Equal(600_000, created.RemainingGold);
        Assert.Equal(new GuildPublicIdentity(created.Guild!.Value, "Iris", 1),
            await query.FindPublicIdentityAsync(new(11), default));
        Assert.True(Assert.Single(await query.ReadRosterAsync(new(11), default)).IsLeader);
        Assert.Equal(GuildMutationStatus.AlreadyMember,
            (await Write(() => guilds.CreateAsync(new(11), "More", "", default))).Status);
        Assert.Equal(GuildMutationStatus.NameTaken,
            (await Write(() => guilds.CreateAsync(new(12), "iRIS", "", default))).Status);
        Assert.Equal(GuildMutationStatus.InsufficientGold,
            (await Write(() => guilds.CreateAsync(new(13), "Silver", "", default))).Status);
        Assert.Equal(GuildMutationStatus.CharacterUnavailable,
            (await Write(() => guilds.CreateAsync(new(16), "Hidden", "", default))).Status);
        Assert.Equal(1L, Execute("SELECT COUNT(*) FROM guilds"));
        Assert.Equal(600_000L, Execute("SELECT carried_gold FROM character_stats WHERE character_id=11"));
        Assert.Equal(900_000L, Execute("SELECT carried_gold FROM character_stats WHERE character_id=12"));
    }

    [Fact]
    public async Task Failure_inserting_the_leader_rolls_back_the_guild_and_payment()
    {
        Execute("""
            CREATE TRIGGER fail_guild_member BEFORE INSERT ON guild_members
            BEGIN SELECT RAISE(ABORT,'forced guild membership fault'); END;
            """);
        await Assert.ThrowsAsync<SqliteException>(() => Write(() => guilds.CreateAsync(new(11), "Iris", "", default)));
        Assert.Null(await query.FindPublicIdentityAsync(new(11), default));
        Assert.Equal(0L, Execute("SELECT COUNT(*) FROM guilds"));
        Assert.Equal(900_000L, Execute("SELECT carried_gold FROM character_stats WHERE character_id=11"));
    }

    [Fact]
    public async Task Missing_creation_price_cannot_create_a_free_guild()
    {
        creationCost.Value = null;
        var result = await Write(() => guilds.CreateAsync(new(11), "Iris", "", default));
        Assert.Equal(GuildMutationStatus.CreationUnavailable, result.Status);
        Assert.Null(await query.FindMembershipAsync(new(11), default));
        Assert.Equal(0L, Execute("SELECT COUNT(*) FROM guilds"));
        Assert.Equal(900_000L, Execute("SELECT carried_gold FROM character_stats WHERE character_id=11"));
    }

    [Fact]
    public async Task Application_is_visible_and_only_the_intended_leader_can_approve_it_once()
    {
        var first = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        var second = (await Write(() => guilds.CreateAsync(new(12), "Violet", "", default))).Guild!.Value;
        Assert.Equal(GuildMutationStatus.Success,
            (await Write(() => guilds.ApplyAsync(new(13), first, "Let me in", default))).Status);
        var application = Assert.IsType<GuildApplication>(await query.FindApplicationAsync(new(13), default));
        Assert.Equal(first, application.Guild);
        Assert.Equal("Let me in", application.Message);
        Assert.Equal(application, Assert.Single(await query.ReadApplicationsAsync(new(11), default)));
        Assert.Empty(await query.ReadApplicationsAsync(new(12), default));
        Assert.Equal(GuildMutationStatus.ApplicationPending,
            (await Write(() => guilds.ApplyAsync(new(13), second, "", default))).Status);
        Assert.Equal(GuildMutationStatus.LeaderRequired,
            (await Write(() => guilds.ApproveAsync(new(14), new(13), default))).Status);
        Assert.Equal(GuildMutationStatus.ApplicationNotFound,
            (await Write(() => guilds.ApproveAsync(new(12), new(13), default))).Status);
        Assert.Equal(GuildMutationStatus.Success,
            (await Write(() => guilds.ApproveAsync(new(11), new(13), default))).Status);
        Assert.Equal(GuildMutationStatus.ApplicationNotFound,
            (await Write(() => guilds.ApproveAsync(new(11), new(13), default))).Status);
        Assert.Equal(GuildMutationStatus.AlreadyMember,
            (await Write(() => guilds.ApplyAsync(new(13), second, "", default))).Status);
        Assert.Null(await query.FindApplicationAsync(new(13), default));
        await Write(() => guilds.ApplyAsync(new(14), first, "Another", default));
        Assert.Equal(new CharacterId(14), Assert.Single(await query.ReadApplicationsAsync(new(11), default)).Character);
        Assert.Empty(await query.ReadApplicationsAsync(new(13), default));
        Assert.Equal(first, (await query.FindPublicIdentityAsync(new(13), default))!.Id);
        Assert.Equal(new[] { 11, 13 }, (await query.ReadRosterAsync(new(13), default)).Select(member => member.Character.Value));
        Assert.Single(await query.ReadRosterAsync(new(12), default));
    }

    [Fact]
    public async Task Leaving_removing_and_disbanding_clear_the_right_relations()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        await Join(new(13));
        await Join(new(14));
        Assert.Equal(GuildMutationStatus.LeaderCannotLeave, (await Write(() => guilds.LeaveAsync(new(11), default))).Status);
        Assert.Equal(GuildMutationStatus.LeaderRequired,
            (await Write(() => guilds.RemoveMemberAsync(new(13), new(14), default))).Status);
        Assert.Equal(GuildMutationStatus.Success,
            (await Write(() => guilds.RemoveMemberAsync(new(11), new(13), default))).Status);
        Assert.Null(await query.FindMembershipAsync(new(13), default));
        Assert.Equal(GuildMutationStatus.Success, (await Write(() => guilds.LeaveAsync(new(14), default))).Status);
        Assert.Null(await query.FindMembershipAsync(new(14), default));
        await Join(new(14));
        await Write(() => guilds.ApplyAsync(new(15), guild, "Pending application", default));
        var destroyed = await Write(() => guilds.DisbandAsync(new(11), default));
        Assert.Equal(GuildMutationStatus.Success, destroyed.Status);
        Assert.Equal(new[] { 11, 14, 15 }, destroyed.AffectedCharacters!.Select(character => character.Value));
        Assert.Empty(await query.ReadRosterAsync(new(11), default));
        Assert.Null(await query.FindMembershipAsync(new(14), default));
        Assert.Null(await query.FindApplicationAsync(new(15), default));
        Assert.Equal(0L, Execute("SELECT COUNT(*) FROM guilds"));

        async Task Join(CharacterId applicant)
        {
            Assert.Equal(GuildMutationStatus.Success,
                (await Write(() => guilds.ApplyAsync(applicant, guild, "", default))).Status);
            Assert.Equal(GuildMutationStatus.Success,
                (await Write(() => guilds.ApproveAsync(new(11), applicant, default))).Status);
        }
    }

    [Fact]
    public async Task Deletion_rejects_member_and_clears_a_nonmembers_application()
    {
        var guild = (await Write(() => guilds.CreateAsync(new(11), "Iris", "", default))).Guild!.Value;
        await Write(() => guilds.ApplyAsync(new(13), guild, "", default));
        Assert.False(await Delete(new(11)));
        Assert.NotNull(await query.FindPublicIdentityAsync(new(11), default));
        Assert.Equal(0L, Execute("SELECT delete_flag FROM characters WHERE character_id=11"));
        Assert.True(await Delete(new(13)));
        Assert.Equal(1L, Execute("SELECT delete_flag FROM characters WHERE character_id=13"));
        Assert.Null(await query.FindApplicationAsync(new(13), default));

        Task<bool> Delete(CharacterId character) => TestGameplay.RunAsync(async () =>
        {
            await using var unit = scope.Begin();
            Assert.True(await characters.MarkDeletedAsync(character, default));
            if (!await guilds.PrepareCharacterDeletionAsync(character, default))
                return false;
            await unit.CommitAsync(default);
            return true;
        });
    }

    private Task<T> Write<T>(Func<ValueTask<T>> action) => TestGameplay.RunAsync(async () =>
    {
        await using var unit = scope.Begin();
        var result = await action();
        await unit.CommitAsync(default);
        return result;
    });

    private object? Execute(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);
    }

    private sealed class CreationCost : IGuildCreationCostSource
    {
        public int? Value { get; set; } = Fee;
        public int? ReadGoldCost() => Value;
    }
}
