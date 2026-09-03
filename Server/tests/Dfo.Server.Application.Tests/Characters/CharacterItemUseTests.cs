using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Networking;

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
        fixture.Cooldowns.Start(Character, ItemCooldownKey.ForItem(8474), TimeSpan.FromSeconds(10));

        fixture.Clock.Advance(TimeSpan.FromSeconds(9));

        Assert.Equal(
            ItemUsability.OnCooldown,
            await usability.CanUseAsync(8474, CancellationToken.None));
    }

    [Fact]
    public async Task A_cooldown_that_has_elapsed_no_longer_refuses()
    {
        var usability = Build(out var fixture, (8474, ItemUseRules.Potion()));
        fixture.Cooldowns.Start(Character, ItemCooldownKey.ForItem(8474), TimeSpan.FromSeconds(10));

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
        fixture.Cooldowns.Start(Character, ItemCooldownKey.ForGroup(70), TimeSpan.FromSeconds(3));

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
        fixture.Cooldowns.Start(Character, ItemCooldownKey.ForItem(8474), TimeSpan.FromSeconds(10));

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
        fixture.Cooldowns.Start(Character, ItemCooldownKey.ForItem(70), TimeSpan.FromSeconds(10));

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
        fixture.Cooldowns.Start(Character, ItemCooldownKey.ForItem(8474), TimeSpan.FromMinutes(30));

        fixture.Cooldowns.Clear(Character);

        Assert.False(fixture.Cooldowns.IsCoolingDown(Character, ItemCooldownKey.ForItem(8474)));
    }

    /// <remarks>
    /// Entry registers the session and binds the container before it publishes the runtime state
    /// (`SelectCharacterPhaseCommands`: RegisterReplacing, then PrepareLeaseAsync, then the publish
    /// command), so a request arriving in between finds a character it cannot ask about. Answering
    /// that with the archive's own refusal would tell the player their item is not a consumable.
    /// </remarks>
    [Fact]
    public async Task A_character_whose_state_is_not_published_yet_is_not_the_players_fault()
    {
        var clock = new AdvanceableTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var usability = new PlayingCharacter(
            Character,
            new ItemUseJudgementService(new FakeItemUseRules((8474, ItemUseRules.Potion()))),
            new CharacterItemCooldownDomainService(clock),
            new OneSession(),
            FixedRuntimeSessions.Unpublished,
            new SessionPresenceRegistry(),
            new NoCarriedGold(),
            new StoredCarryLimitOnly());

        Assert.Equal(
            ItemUsability.CharacterStateUnavailable,
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
        fixture.Cooldowns.Start(Character, ItemCooldownKey.ForGroup(70), TimeSpan.FromSeconds(3));

        Assert.Equal(
            ItemUsability.LevelTooLow,
            await usability.CanUseAsync(9000, CancellationToken.None));
    }

    private sealed record Fixture(
        AdvanceableTimeProvider Clock,
        ICharacterItemCooldownDomainService Cooldowns);

    private static ICharacter Build(
        out Fixture fixture,
        byte level,
        byte job,
        params (int ItemId, ItemUseRule Rule)[] rules)
    {
        var clock = new AdvanceableTimeProvider(new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero));
        var cooldowns = new CharacterItemCooldownDomainService(clock);
        fixture = new Fixture(clock, cooldowns);
        return new PlayingCharacter(
            Character,
            new ItemUseJudgementService(new FakeItemUseRules(rules)),
            cooldowns,
            new OneSession(),
            new FixedRuntimeSessions(level, job),
            new SessionPresenceRegistry(),
            new NoCarriedGold(),
            new StoredCarryLimitOnly());
    }

    private static ICharacter Build(
        out Fixture fixture,
        params (int ItemId, ItemUseRule Rule)[] rules) =>
        Build(out fixture, level: 50, job: 0, rules);

    private sealed class OneSession : ISelectedSessionDirectory
    {
        private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
            new AccountId(6), new CharacterId(60), new NetworkConnectionId("game-usable"), 1, 10011);

        public SelectedSessionIdentity? RegisterReplacing(SelectedSessionIdentity session) => null;
        public bool TryRemoveExact(SelectedSessionIdentity session) => false;
        public SelectedSessionIdentity? Find(AccountId accountId) => Session;
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => Session;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Session;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => Session;
        public IReadOnlyList<SelectedSessionIdentity> Current() => [Session];
    }
}
