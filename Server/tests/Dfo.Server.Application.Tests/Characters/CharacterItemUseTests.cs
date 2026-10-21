using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Networking;
using Dfo.Server.Persistence.Characters;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// Whether a character may use an item, asked of the character itself.
/// </summary>
/// <remarks>
/// The archive declares four restrictions for a stackable item -- who, what level, where, and how
/// often -- and until now the server read none of them. Each fact below comes from the archive's
/// own tags: <c>[usable job]</c> on 34,533 files, <c>[minimum level]</c> on 28,673,
/// <c>[cool time]</c> on 6,254, and <c>[action usable place]</c> on 792.
/// </remarks>
public sealed class CharacterItemUseTests
{
    private static readonly CharacterId Character = new(60);

    /// <summary>
    /// The level the session this character is played on used to carry. Deliberately not the row's.
    /// </summary>
    /// <remarks>
    /// A wrong level, kept as the figure the cases below are set around. The session carried a copy
    /// of the character's standing that every path moving the character had to remember to correct,
    /// and a judgement made against the copy was made against whatever was last written there. The
    /// character reads its own row now and cannot reach that copy at all, so what this names is the
    /// answer a regression would produce rather than one it can.
    /// </remarks>
    private const byte SessionLevel = 7;

    [Fact]
    public async Task An_item_the_archive_does_not_list_is_not_usable()
    {
        var usability = Build(out _);

        Assert.Equal(
            ItemUsability.NotAUsableItem,
            await usability.CanUseAsync(itemId: 1000, CancellationToken.None));
    }

    /// <remarks>
    /// The archive spells five items' job as <c>[prist]</c> and twenty-one as
    /// <c>[creator mage]</c>, neither of which this server models. Reading an unknown restriction
    /// as no restriction would hand out exactly the items the archive meant to withhold, and
    /// nothing would report it.
    /// </remarks>
    [Fact]
    public async Task A_declaration_the_server_cannot_read_is_a_refusal()
    {
        var unreadable = ItemUseRules.RequiringJob(QuestJobRequirement.Unrecognized);
        var usability = Build(out _, (8474, unreadable));

        Assert.Equal(
            ItemUsability.NotAUsableItem,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    [Fact]
    public async Task A_level_below_the_declared_minimum_is_refused()
    {
        var usability = Build(out _, level: 19, job: 0, (8474, ItemUseRules.RequiringLevel(20)));

        Assert.Equal(
            ItemUsability.LevelTooLow,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    [Fact]
    public async Task A_level_at_the_declared_minimum_is_allowed()
    {
        var usability = Build(out _, level: 20, job: 0, (8474, ItemUseRules.RequiringLevel(20)));

        Assert.Equal(
            ItemUsability.Allowed,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    /// <remarks>
    /// Job 4 is the priest. The item is declared for swordmen, which is a family the character is
    /// not in and never becomes.
    /// </remarks>
    [Fact]
    public async Task A_job_outside_the_declared_families_is_refused()
    {
        var usability = Build(
            out _, level: 50, job: 4, (8474, ItemUseRules.RequiringJob(QuestJobRequirement.Swordman)));

        Assert.Equal(
            ItemUsability.JobMismatch,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    [Fact]
    public async Task A_job_inside_the_declared_families_is_allowed()
    {
        var usability = Build(
            out _, level: 50, job: 4, (8474, ItemUseRules.RequiringJob(QuestJobRequirement.Priest)));

        Assert.Equal(
            ItemUsability.Allowed,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    /// <remarks>
    /// Dungeons are not served, so a character asking anything is standing in a town. An item the
    /// archive allows only underground is therefore refused everywhere this server can put anyone.
    /// </remarks>
    [Fact]
    public async Task An_item_only_usable_in_a_dungeon_is_refused_in_town()
    {
        var usability = Build(out _, (8474, ItemUseRules.UsableIn(ItemUsePlace.Dungeon)));

        Assert.Equal(
            ItemUsability.PlaceNotAllowed,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    [Fact]
    public async Task A_tower_member_uses_the_existing_dungeon_item_rules()
    {
        var usability = BuildInTower(out _, level: 50, job: 0,
            (8474, ItemUseRules.UsableIn(ItemUsePlace.Dungeon)));

        Assert.Equal(ItemUsability.Allowed,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    [Fact]
    public async Task An_item_usable_in_a_village_is_allowed_in_town()
    {
        var usability = Build(
            out _, (8474, ItemUseRules.UsableIn(ItemUsePlace.Village, ItemUsePlace.Dungeon)));

        Assert.Equal(
            ItemUsability.Allowed,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    /// <remarks>
    /// An item declaring no place at all is unrestricted, which is what 33,740 of the 34,532 files
    /// do. Reading absence as a restriction would refuse almost everything.
    /// </remarks>
    [Fact]
    public async Task An_item_declaring_no_place_is_allowed_anywhere()
    {
        var usability = Build(out _, (8474, ItemUseRules.Potion(cooldownMilliseconds: 0)));

        Assert.Equal(
            ItemUsability.Allowed,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    [Fact]
    public async Task An_item_used_moments_ago_is_still_cooling_down()
    {
        var usability = Build(out var fixture, (8474, ItemUseRules.Potion()));
        fixture.Cooldowns.StartCooldown(fixture.Session, ItemCooldownKey.ForItem(8474), TimeSpan.FromSeconds(10));

        fixture.Clock.Advance(TimeSpan.FromSeconds(9));

        Assert.Equal(
            ItemUsability.OnCooldown,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    [Fact]
    public async Task A_cooldown_that_has_elapsed_no_longer_refuses()
    {
        var usability = Build(out var fixture, (8474, ItemUseRules.Potion()));
        fixture.Cooldowns.StartCooldown(fixture.Session, ItemCooldownKey.ForItem(8474), TimeSpan.FromSeconds(10));

        fixture.Clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(
            ItemUsability.Allowed,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    /// <remarks>
    /// 1,713 items declare a <c>[cooltime group]</c>, and the whole point of declaring one is that
    /// using any member starts the others' wait.
    /// </remarks>
    [Fact]
    public async Task Items_sharing_a_cooltime_group_share_the_cooldown()
    {
        var usability = Build(
            out var fixture,
            (8474, ItemUseRules.Potion(3_000, cooltimeGroup: 70)),
            (9000, ItemUseRules.Potion(3_000, cooltimeGroup: 70)));
        fixture.Cooldowns.StartCooldown(fixture.Session, ItemCooldownKey.ForGroup(70), TimeSpan.FromSeconds(3));

        Assert.Equal(
            ItemUsability.OnCooldown,
            await usability.CanUseAsync(9000, CancellationToken.None));
    }

    /// <remarks>
    /// A group of zero is the archive's way of saying "shares with nothing", written as often as
    /// the tag is omitted. Two such items must not wait on each other.
    /// </remarks>
    [Fact]
    public async Task Items_without_a_group_cool_down_separately()
    {
        var usability = Build(
            out var fixture,
            (8474, ItemUseRules.Potion(cooltimeGroup: 0)),
            (9000, ItemUseRules.Potion(cooltimeGroup: 0)));
        fixture.Cooldowns.StartCooldown(fixture.Session, ItemCooldownKey.ForItem(8474), TimeSpan.FromSeconds(10));

        Assert.Equal(
            ItemUsability.Allowed,
            await usability.CanUseAsync(9000, CancellationToken.None));
    }

    /// <remarks>
    /// A group number and an item identifier are unrelated numbering spaces. Keyed on the bare
    /// integer, using item 70 would silence every item in cooldown group 70.
    /// </remarks>
    [Fact]
    public async Task A_group_number_and_an_item_number_are_not_the_same_cooldown()
    {
        var usability = Build(
            out var fixture,
            (70, ItemUseRules.Potion(cooltimeGroup: 0)),
            (9000, ItemUseRules.Potion(3_000, cooltimeGroup: 70)));
        fixture.Cooldowns.StartCooldown(fixture.Session, ItemCooldownKey.ForItem(70), TimeSpan.FromSeconds(10));

        Assert.Equal(
            ItemUsability.Allowed,
            await usability.CanUseAsync(9000, CancellationToken.None));
    }

    /// <remarks>
    /// The question is a question. A judgement that started the wait would make asking twice
    /// different from asking once, and the effects -- which run only after a use succeeds -- would
    /// be starting a cooldown that was already running.
    /// </remarks>
    [Fact]
    public async Task Asking_does_not_start_a_cooldown()
    {
        var usability = Build(out var fixture, (8474, ItemUseRules.Potion()));

        Assert.Equal(
            ItemUsability.Allowed,
            await usability.CanUseAsync(8474, CancellationToken.None));
        Assert.Equal(
            ItemUsability.Allowed,
            await usability.CanUseAsync(8474, CancellationToken.None));
        Assert.False(fixture.Cooldowns.IsCoolingDown(Character, ItemCooldownKey.ForItem(8474)));
    }

    /// <remarks>
    /// Leaving clears the record, so a relog resets every wait. That is wrong for a thirty-minute
    /// summon and unnoticeable for a ten-second potion; it is written down here so the day it
    /// matters, the choice is visible rather than discovered.
    /// </remarks>
    [Fact]
    public void Leaving_clears_the_characters_cooldowns()
    {
        Build(out var fixture, (8474, ItemUseRules.Potion()));
        fixture.Cooldowns.StartCooldown(fixture.Session, ItemCooldownKey.ForItem(8474), TimeSpan.FromMinutes(30));

        fixture.Cooldowns.EndExact(fixture.Session);

        Assert.False(fixture.Cooldowns.IsCoolingDown(Character, ItemCooldownKey.ForItem(8474)));
    }

    /// <summary>
    /// A character whose state is not published yet is not the player's fault.
    /// </summary>
    /// <remarks>
    /// Entry registers the session and binds the container before it publishes the runtime state
    /// (`SelectCharacterPhaseCommands`: Select, then PrepareLeaseAsync, then the publish
    /// command), so a request arriving in between finds a character it cannot ask about. Answering
    /// that with the archive's own refusal would tell the player their item is not a consumable.
    /// PrepareLeaseAsync is a database round trip, so the window is real rather than theoretical.
    /// <para>
    /// Said here as an absent presence record, because that is where the character reads it. The
    /// step that publishes the runtime state admits the character to the presence record with
    /// nothing awaited in between, and the cleanup that withdraws one removes the other in the same
    /// synchronous run, so a character inside this window is in neither.
    /// </para>
    /// <para>
    /// The row is present, which is what separates this from the case below: that one has the
    /// character published and no row, this one has a row and no character published, and the two
    /// answer alike.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_character_whose_state_is_not_published_yet_is_not_the_players_fault()
    {
        var clock = new AdvanceableTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var usability = new PlayingCharacter(
            Character,
            new ItemUseJudgementService(new FakeItemUseRules((8474, ItemUseRules.Potion()))),
            new OnlineCharacterRuntimeDomainService(clock),
            new SessionPresenceDomainService(),
            new NoCarriedGold(),
            new StoredCarryLimitOnly(),
            CharacterRow.At(level: 50),
            new NoSkillPointLedger(), null!, null!, experienceRules: new Dfo.Server.Application.Tests.Support.TestExperienceRules());

        Assert.Equal(
            ItemUsability.CharacterStateUnavailable,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    /// <summary>
    /// A character being played but with no row of its own is not the player's fault either.
    /// </summary>
    /// <remarks>
    /// Both facts a use is judged against -- what the character is and how far along it is -- come
    /// from the row now, so a character this server does not have is a character with nothing to
    /// judge against. It answers the way the unpublished character above answers, and it has to:
    /// what is being reported either way is that there is nothing to judge against, and the
    /// archive's own refusal would tell the player their potion is not a consumable.
    /// </remarks>
    [Fact]
    public async Task A_character_with_no_row_of_its_own_is_not_the_players_fault()
    {
        var clock = new AdvanceableTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var usability = new PlayingCharacter(
            Character,
            new ItemUseJudgementService(new FakeItemUseRules((8474, ItemUseRules.Potion()))),
            new OnlineCharacterRuntimeDomainService(clock),
            PresentCharacters.StandingInTown(Character),
            new NoCarriedGold(),
            new StoredCarryLimitOnly(),
            CharacterRow.None,
            new NoSkillPointLedger(), null!, null!, experienceRules: new Dfo.Server.Application.Tests.Support.TestExperienceRules());

        Assert.Equal(
            ItemUsability.CharacterStateUnavailable,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    /// <summary>
    /// A character whose row has been marked deleted under it is still judged against that row.
    /// </summary>
    /// <remarks>
    /// <b>The case a filter on one of the two reads would fail.</b> The two facts a use is judged
    /// against arrive as two narrow reads of one row, and either of them answering nothing is
    /// reported as the same <see cref="ItemUsability.CharacterStateUnavailable"/> -- so what the
    /// pair needs is to see the same rows. The progress read has never excluded a deleted
    /// character; a <c>delete_flag</c> condition on the identity read alone would invent a refusal
    /// for a state this server previously judged.
    /// <para>
    /// The state is reachable rather than theoretical: deleting a character checks only that the
    /// connection is authenticated, not that the character is the one being played, so the presence
    /// record admitted at entry outlives the mark. Whether refusing a deleted character would be
    /// the better answer is a separate question -- nothing here chose to start refusing it.
    /// </para>
    /// <para>
    /// Asked of a real table, because the condition under test is in the statement. A double would
    /// answer both reads however it was written to, whatever the two statements said.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_character_marked_deleted_under_the_player_is_still_judged()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dfo-deleted-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var database = new SqliteDatabase(
                Path.Combine(directory, "characters.db"), new DomainTransactionScope());
            await using (var connection = new SqliteConnection(database.ConnectionString))
            {
                await connection.OpenAsync(CancellationToken.None);
                await using var insert = connection.CreateCommand();
                insert.CommandText =
                    "INSERT INTO characters(character_id,account_id,name,job,level,delete_flag) "
                    + "VALUES(@cid,6,'deleted',0,50,1);";
                insert.Parameters.AddWithValue("@cid", Character.Value);
                Assert.Equal(1, await insert.ExecuteNonQueryAsync(CancellationToken.None));
            }

            var usability = new PlayingCharacter(
                Character,
                new ItemUseJudgementService(new FakeItemUseRules((8474, ItemUseRules.Potion()))),
                new OnlineCharacterRuntimeDomainService(
                    new AdvanceableTimeProvider(
                        new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero))),
                PresentCharacters.StandingInTown(Character),
                new NoCarriedGold(),
                new StoredCarryLimitOnly(),
                new SqliteCharacterTable(database),
                new NoSkillPointLedger(), null!, null!, experienceRules: new Dfo.Server.Application.Tests.Support.TestExperienceRules());

            Assert.Equal(
                ItemUsability.Allowed,
                await usability.CanUseAsync(8474, CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The level a use is judged against is the row's, not the session's.
    /// </summary>
    /// <remarks>
    /// <b>The link the rest of this file would not catch.</b> Every other case here would pass
    /// against either level, because the two used to be the same object. They are not any more:
    /// the row is what a level-up writes, and the copy the session held had to be corrected by
    /// hand. So the row is put above the item's requirement and that copy's figure below it, and
    /// the answer says which one arrived.
    /// </remarks>
    [Fact]
    public async Task The_level_a_use_is_judged_against_is_the_rows()
    {
        var usability = Build(
            out _, level: 50, job: 0, (8474, ItemUseRules.RequiringLevel(SessionLevel + 1)));

        Assert.Equal(
            ItemUsability.Allowed,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    /// <remarks>
    /// A cooldown expires and a level does not. Reported the other way round, the player waits ten
    /// seconds and tries again -- and cooldowns are shared, so the item that started the wait need
    /// not be the item being refused.
    /// </remarks>
    [Fact]
    public async Task A_permanent_reason_is_reported_ahead_of_one_that_will_expire()
    {
        var usability = Build(
            out var fixture,
            level: 10,
            job: 0,
            (8474, ItemUseRules.Potion(3_000, cooltimeGroup: 70)),
            (9000, ItemUseRules.RequiringLevel(50) with { CooltimeGroup = 70, CooldownMilliseconds = 3_000 }));
        fixture.Cooldowns.StartCooldown(fixture.Session, ItemCooldownKey.ForGroup(70), TimeSpan.FromSeconds(3));

        Assert.Equal(
            ItemUsability.LevelTooLow,
            await usability.CanUseAsync(9000, CancellationToken.None));
    }

    private sealed record Fixture(
        AdvanceableTimeProvider Clock,
        OnlineCharacterRuntimeDomainService Cooldowns,
        Dfo.Server.Data.World.SelectedSessionIdentity Session);

    private static ICharacter Build(
        out Fixture fixture,
        byte level,
        byte job,
        params (int ItemId, ItemUseRule Rule)[] rules)
        => Build(out fixture, level, job, false, rules);

    private static ICharacter BuildInTower(
        out Fixture fixture,
        byte level,
        byte job,
        params (int ItemId, ItemUseRule Rule)[] rules)
        => Build(out fixture, level, job, true, rules);

    private static ICharacter Build(
        out Fixture fixture,
        byte level,
        byte job,
        bool inTower,
        params (int ItemId, ItemUseRule Rule)[] rules)
    {
        var clock = new AdvanceableTimeProvider(new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero));
        var cooldowns = new OnlineCharacterRuntimeDomainService(clock);
        var session = Dfo.Server.Data.World.SelectedSessionIdentity.Create(
            new Dfo.Server.Data.Accounts.AccountId(1), Character,
            new Dfo.Server.Data.Networking.NetworkConnectionId("Game:item-use"), 2, 10011);
        cooldowns.Begin(session, new CharacterPositionState(1, 0, 0, 0, 0), 100);
        fixture = new Fixture(clock, cooldowns, session);
        var tower = TowerPresenceProxy.Create(out var presence);
        if (inTower) presence.Members.Add(Character);
        return new PlayingCharacter(
            Character,
            new ItemUseJudgementService(new FakeItemUseRules(rules)),
            cooldowns,
            PresentCharacters.StandingInTown(Character),
            new NoCarriedGold(),
            new StoredCarryLimitOnly(),
            CharacterRow.At(level, job),
            new NoSkillPointLedger(), null!, null!, null, () => tower, experienceRules: new Dfo.Server.Application.Tests.Support.TestExperienceRules());
    }

    private static ICharacter Build(
        out Fixture fixture,
        params (int ItemId, ItemUseRule Rule)[] rules) =>
        Build(out fixture, level: 50, job: 0, rules);
}
