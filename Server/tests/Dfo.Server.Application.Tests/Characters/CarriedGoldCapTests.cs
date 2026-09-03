using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// How much gold a character may carry, and that the cap actually reaches the store.
/// </summary>
/// <remarks>
/// Two halves, and the second is the one that was missing. The rule can be right while nothing
/// asks it: <see cref="PlayingCharacter.AddGoldAsync"/> reads the character, works the cap out and
/// hands it to the statement that adds, and if any link in that chain were cut the gold would
/// simply arrive uncapped -- with every other case still green, because every other case in this
/// project constructs a character whose gold table answers "no such character" and so returns
/// before reaching the chain at all.
/// </remarks>
public sealed class CarriedGoldCapTests
{
    private static readonly CharacterId Who = new(60);

    /// <summary>The floor the stand-in archive puts under a level.</summary>
    private const int ArchiveFloor = 200_000;

    /// <summary>
    /// The cap is the archive's floor when nobody has bought an upgrade.
    /// </summary>
    /// <remarks>
    /// Which is every character today: nothing in this server writes the stored column, so it
    /// reads zero for all of them. A rule that took the stored figure alone would cap every player
    /// at nothing and clamp their gold away, and it would do it silently.
    /// </remarks>
    [Fact]
    public void An_unbought_upgrade_leaves_the_archives_floor_standing()
    {
        Assert.Equal(ArchiveFloor, Rule().CarryLimitFor(characterLevel: 40, storedCarryLimit: 0));
    }

    /// <summary>A bought upgrade above the floor is what applies.</summary>
    [Fact]
    public void An_upgrade_above_the_floor_is_what_applies()
    {
        Assert.Equal(
            ArchiveFloor + 1, Rule().CarryLimitFor(40, storedCarryLimit: ArchiveFloor + 1));
    }

    /// <summary>An upgrade below the floor never takes anything away.</summary>
    /// <remarks>
    /// The two are a floor and a purchase, not two candidates. A character that levelled past its
    /// old upgrade keeps what its level entitles it to.
    /// </remarks>
    [Fact]
    public void An_upgrade_below_the_floor_takes_nothing_away()
    {
        Assert.Equal(ArchiveFloor, Rule().CarryLimitFor(40, storedCarryLimit: 5));
    }

    /// <summary>A stored figure below zero is read as none rather than as a debt.</summary>
    [Fact]
    public void A_negative_stored_upgrade_is_read_as_none()
    {
        Assert.Equal(ArchiveFloor, Rule().CarryLimitFor(40, storedCarryLimit: -1));
    }

    /// <summary>
    /// The cap the rule works out is the cap the store is given.
    /// </summary>
    /// <remarks>
    /// <b>This is the link nothing else covers.</b> The store's own case proves it clamps at
    /// whatever ceiling it is handed; the rule's cases above prove the ceiling is worked out
    /// correctly. Neither says the two are joined, and they are joined inside
    /// <see cref="PlayingCharacter.AddGoldAsync"/> -- which every other case in this project skips
    /// entirely, because their gold table answers "no such character".
    /// <para>
    /// Asserted by recording what the store was handed rather than by watching a figure move,
    /// because a chain that passed <c>int.MaxValue</c> would move the figure exactly the same way
    /// and cap nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_cap_the_rule_works_out_is_the_ceiling_the_store_is_given()
    {
        var gold = new RecordingCarriedGold(carried: 1_000, level: RowLevel, storedCarryLimit: 0);
        var (character, archive) = Carrier(gold);

        var carried = await character.AddGoldAsync(500, CancellationToken.None);

        Assert.Equal(RowLevel, archive.LevelAsked);
        Assert.Equal(ArchiveFloor, gold.CeilingGiven);
        Assert.Equal(500, gold.AmountGiven);
        Assert.Equal(1_500, carried);
    }

    /// <summary>
    /// A bought upgrade reaches the store too, not just the floor.
    /// </summary>
    /// <remarks>
    /// The pair to the case above: with only that one, a chain that ignored the stored column and
    /// passed the archive's floor straight through would still pass.
    /// </remarks>
    [Fact]
    public async Task A_bought_upgrade_reaches_the_store_as_the_ceiling()
    {
        var gold = new RecordingCarriedGold(
            carried: 0, level: RowLevel, storedCarryLimit: ArchiveFloor * 2);

        await Carrier(gold).Character.AddGoldAsync(1, CancellationToken.None);

        Assert.Equal(ArchiveFloor * 2, gold.CeilingGiven);
    }

    /// <summary>A character this server does not have is given nothing and asked nothing.</summary>
    /// <remarks>
    /// No row means no level to price a cap by, so there is nothing to hand the store. Opening the
    /// character a row here would put a figure on somebody nobody can play.
    /// </remarks>
    [Fact]
    public async Task A_character_that_does_not_exist_is_given_nothing()
    {
        var gold = new RecordingCarriedGold(carried: 0, level: 0, storedCarryLimit: 0, exists: false);

        Assert.Equal(0, await Carrier(gold).Character.AddGoldAsync(900, CancellationToken.None));
        Assert.Null(gold.CeilingGiven);
    }

    /// <summary>The level on the character's own row, which is what prices the cap.</summary>
    private const int RowLevel = 40;

    /// <summary>
    /// The level on the session this character is played on. Deliberately not <see cref="RowLevel"/>.
    /// </summary>
    /// <remarks>
    /// A second level within reach of the same object, so that a chain which priced the cap from
    /// the session instead of from the row would be asking about this figure and would be caught.
    /// </remarks>
    private const byte SessionLevel = 7;

    private static ICarriedGoldRuleService Rule() => new CarriedGoldRuleService(new FlatFloor());

    private static (ICharacter Character, FlatFloor Archive) Carrier(
        ICharacterCarriedGoldTable gold)
    {
        var archive = new FlatFloor();
        return (
            new PlayingCharacter(
                Who,
                new ItemUseJudgementService(new FakeItemUseRules()),
                new CharacterItemCooldownDomainService(TimeProvider.System),
                new SelectedSessionDirectory(),
                new FixedRuntimeSessions(level: SessionLevel, job: 0),
                new SessionPresenceRegistry(),
                gold,
                new CarriedGoldRuleService(archive)),
            archive);
    }

    /// <summary>An archive that puts the same floor under every level, and remembers who asked.</summary>
    /// <remarks>
    /// Flat on purpose. What varies by level is the archive's business and is asserted against the
    /// archive itself; what is asserted here is how a floor and a purchase are weighed against each
    /// other, and a table that varied would let a wrong level lookup pass as a wrong weighing.
    /// <para>
    /// <b>But it records the level it was asked for.</b> Flat alone would let a chain that priced
    /// the cap from the wrong level pass -- and there is a wrong level within easy reach: the
    /// session this character is played on carries one too. The cap has to come from the character's
    /// own row, so the two are given different figures and the one that arrives here is asserted.
    /// </para>
    /// </remarks>
    private sealed class FlatFloor : IGoldCarryLimitSource
    {
        public int? LevelAsked { get; private set; }

        public int BaseLimitFor(int characterLevel)
        {
            this.LevelAsked = characterLevel;
            return ArchiveFloor;
        }
    }

    /// <summary>A character's gold that remembers what the seam handed it.</summary>
    private sealed class RecordingCarriedGold(
        int carried, int level, int storedCarryLimit, bool exists = true)
        : ICharacterCarriedGoldTable
    {
        public int? CeilingGiven { get; private set; }

        public int? AmountGiven { get; private set; }

        public ValueTask<CarriedGoldRow?> FindAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                exists ? new CarriedGoldRow(carried, level, storedCarryLimit) : null);

        public ValueTask<int> AddAsync(
            CharacterId characterId, int amount, int ceiling, CancellationToken cancellationToken)
        {
            this.CeilingGiven = ceiling;
            this.AmountGiven = amount;
            return ValueTask.FromResult(Math.Max(0, Math.Min(ceiling, carried + amount)));
        }

        public ValueTask<int?> TryTakeAsync(
            CharacterId characterId, int amount, CancellationToken cancellationToken) =>
            ValueTask.FromResult<int?>(carried >= amount ? carried - amount : null);
    }
}
