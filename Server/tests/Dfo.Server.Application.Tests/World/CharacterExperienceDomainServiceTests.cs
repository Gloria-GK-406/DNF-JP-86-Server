using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// The one place experience is added to a character.
/// </summary>
/// <remarks>
/// Every source of experience goes through this, which is the point of it: a second copy is how
/// this code already produced a wrong total, when a quest finished twice wrote the same figure both
/// times because its own copy added to an entry-time snapshot rather than to the store.
/// </remarks>
public sealed class CharacterExperienceDomainServiceTests
{
    private static readonly CharacterId Character = new(7);

    /// <summary>Thresholds close enough together that one award can cross more than one.</summary>
    private static readonly uint[] Thresholds = [100, 250, 450, 700];

    [Fact]
    public async Task AnAwardRaisesTheStoredTotal()
    {
        var characters = new RecordingCharacterTable(level: 1, experience: 10);
        var grant = Grant(characters);

        var award = await grant.AwardAsync(Character, _ => Worth(30), CancellationToken.None);

        Assert.NotNull(award);
        Assert.Equal(30u, award!.Gained);
        Assert.Equal(40u, award!.Total);
        Assert.Equal(1, award!.Level);
        Assert.False(award!.LeveledUp);
        Assert.Equal((1, 40u), characters.LastWrite);
    }

    /// <summary>Crossing a threshold raises the level and keeps the surplus.</summary>
    [Fact]
    public async Task AnAwardPastAThresholdLevelsTheCharacter()
    {
        var characters = new RecordingCharacterTable(level: 1, experience: 90);
        var grant = Grant(characters);

        var award = await grant.AwardAsync(Character, _ => Worth(30), CancellationToken.None);

        Assert.NotNull(award);
        Assert.True(award!.LeveledUp);
        Assert.Equal(2, award!.Level);
        Assert.Equal(120u, award!.Total);
        Assert.Equal((2, 120u), characters.LastWrite);
    }

    /// <remarks>
    /// One award can cross more than one threshold. The early bands are a few hundred apart and a
    /// kill in a weighted dungeon can pay more than that, so the level is walked rather than
    /// incremented once.
    /// </remarks>
    [Fact]
    public async Task AnAwardPastSeveralThresholdsCrossesThemAll()
    {
        var characters = new RecordingCharacterTable(level: 1, experience: 0);
        var grant = Grant(characters);

        var award = await grant.AwardAsync(Character, _ => Worth(460), CancellationToken.None);

        Assert.NotNull(award);
        Assert.Equal(4, award!.Level);
        Assert.Equal(460u, award!.Total);
    }

    /// <summary>The level is read from the store and handed to the valuation, not taken from it.</summary>
    /// <remarks>
    /// The inversion that makes a stale copy impossible. The caller cannot pass a level in, so it
    /// cannot pass a wrong one.
    /// </remarks>
    [Fact]
    public async Task TheValuationIsAskedWithTheStoredLevel()
    {
        var characters = new RecordingCharacterTable(level: 12, experience: 0);
        var grant = Grant(characters);
        byte asked = 0;

        await grant.AwardAsync(
            Character,
            level => { asked = level; return Worth(1); },
            CancellationToken.None);

        Assert.Equal(12, asked);
    }

    /// <remarks>
    /// Two awards in a row see the store move under them. This is the shape of the defect that
    /// motivated the shared grant: the second completion of a quest wrote the same total as the
    /// first, because both added to the same remembered figure.
    /// </remarks>
    [Fact]
    public async Task ASecondAwardAddsToWhatTheFirstLeftBehind()
    {
        var characters = new RecordingCharacterTable(level: 1, experience: 0);
        var grant = Grant(characters);

        var first = await grant.AwardAsync(Character, _ => Worth(25), CancellationToken.None);
        var second = await grant.AwardAsync(Character, _ => Worth(25), CancellationToken.None);

        Assert.Equal(25u, first!.Total);
        Assert.Equal(50u, second!.Total);
    }

    /// <summary>Contributions are part of what is granted, and are reported with it.</summary>
    [Fact]
    public async Task ContributionsAreAddedAndCarriedBackNamed()
    {
        var characters = new RecordingCharacterTable(level: 1, experience: 0);
        var grant = Grant(characters);

        var award = await grant.AwardAsync(
            Character,
            _ => ValueTask.FromResult(new ExperienceBreakdown(Base: 20, Other: 10)),
            CancellationToken.None);

        Assert.NotNull(award);
        Assert.Equal(20u, award!.Base);
        Assert.Equal(30u, award!.Gained);
        Assert.Equal(30u, award!.Total);
        Assert.Equal(10u, award!.Breakdown.Other);
    }

    /// <summary>An occasion that pays nothing writes nothing.</summary>
    [Fact]
    public async Task AnAwardOfNothingLeavesTheStoreAlone()
    {
        var characters = new RecordingCharacterTable(level: 3, experience: 300);
        var grant = Grant(characters);

        var award = await grant.AwardAsync(
            Character, _ => ValueTask.FromResult(new ExperienceBreakdown()), CancellationToken.None);

        Assert.Equal(0u, award!.Gained);
        Assert.Equal(300u, award!.Total);
        Assert.Equal(3, award!.Level);
        Assert.Null(characters.LastWrite);
    }

    /// <remarks>
    /// A refused write is no award at all, not an award of nothing, and the difference is what a
    /// caller acts on: reporting the higher total would have the client draw a figure the store
    /// disagrees with until the next entry, and reporting a zero award would have a quest report
    /// itself completed for nothing.
    /// </remarks>
    [Fact]
    public async Task AWriteTheStoreRefusesIsNoAwardAtAll()
    {
        var characters = new RecordingCharacterTable(level: 1, experience: 10) { AcceptWrites = false };
        var grant = Grant(characters);

        var award = await grant.AwardAsync(Character, _ => Worth(30), CancellationToken.None);

        Assert.Null(award);
    }

    /// <summary>A character the store does not hold has no standing to report.</summary>
    /// <remarks>
    /// Distinct from an award of nothing, which knows the character's level and total. Answering
    /// with a placeholder here is how a caller ends up telling the client it is level 1 with no
    /// experience.
    /// </remarks>
    [Fact]
    public async Task ACharacterTheStoreDoesNotHoldHasNoStanding()
    {
        var characters = new RecordingCharacterTable(level: 1, experience: 0) { Exists = false };
        var grant = Grant(characters);

        var award = await grant.AwardAsync(Character, _ => Worth(30), CancellationToken.None);

        Assert.Null(award);
        Assert.Null(characters.LastWrite);
    }

    /// <summary>The total saturates at the last threshold rather than running past the table.</summary>
    [Fact]
    public async Task ATotalCannotRunPastTheLastThreshold()
    {
        var characters = new RecordingCharacterTable(level: 4, experience: 690);
        var grant = Grant(characters);

        var award = await grant.AwardAsync(Character, _ => Worth(1_000_000), CancellationToken.None);

        Assert.Equal(Thresholds[^1], award!.Total);
    }

    [Fact]
    public async Task CrossingTheCeilingKeepsTheRewardAndCapsOnlyProgression()
    {
        var characters = new RecordingCharacterTable(level: 4, experience: 690);
        var grant = Grant(characters);
        var award = await grant.AwardAsync(Character,
            _ => ValueTask.FromResult(new ExperienceBreakdown(Base: 1_000_000, Channel: 50_000)),
            CancellationToken.None);
        Assert.NotNull(award);
        Assert.Equal(1_050_000u, award.Gained);
        Assert.Equal(1_000_000u, award.Base);
        Assert.Equal(50_000u, award.Breakdown.Channel);
        Assert.Equal(700u, award.Total);
        Assert.Equal(5, award.Level);
    }

    [Fact]
    public async Task ACharacterAtTheCeilingKeepsTheFullRewardBreakdown()
    {
        var characters = new RecordingCharacterTable(level: 5, experience: Thresholds[^1]);
        var grant = Grant(characters);
        var award = await grant.AwardAsync(Character,
            _ => ValueTask.FromResult(new ExperienceBreakdown(Base: 72, Channel: 3)),
            CancellationToken.None);
        Assert.NotNull(award);
        Assert.Equal(75u, award.Gained);
        Assert.Equal(72u, award.Base);
        Assert.Equal(3u, award.Breakdown.Channel);
        Assert.Equal(5, award.Level);
        Assert.False(award.LeveledUp);
        Assert.Equal(Thresholds[^1], award.Total);
        Assert.Null(characters.LastWrite);
    }

    /// <summary>The award explains itself in one line, which is what a live log has to carry.</summary>
    [Fact]
    public async Task AnAwardCanSayWhereItCameFrom()
    {
        var characters = new RecordingCharacterTable(level: 1, experience: 0);
        var grant = Grant(characters);

        var award = await grant.AwardAsync(
            Character,
            _ => ValueTask.FromResult(new ExperienceBreakdown(Base: 72, Other: 3)),
            CancellationToken.None);

        Assert.NotNull(award);
        Assert.Contains("Base = 72", award!.ToString(), StringComparison.Ordinal);
        Assert.Contains("Other = 3", award!.ToString(), StringComparison.Ordinal);
        Assert.Contains("75", award!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TutorialRequestWithStaleLevelNeverLowersCurrentProgress()
    {
        var row = new RecordingCharacterTable(level: 5, experience: 700);
        var handler = new Dfo.Server.Protocol.Game.TutorialLevelUp.GrantTutorialLevelCommandHandler(Grant(row),
            new Dfo.Server.Application.Characters.CharacterDomainService(null!, null!, id =>
                new Dfo.Server.Application.Characters.PlayingCharacter(id, null!, null!, null!, null!, null!, row, null!, null!, null!)));
        var result = await handler.HandleAsync(null!,
            new Dfo.Server.Protocol.Game.TutorialLevelUp.GrantTutorialLevelCommand(Character),
            CancellationToken.None);
        Assert.Equal(Dfo.Server.Protocol.Game.TutorialLevelUp.TutorialLevelUpStatus.AlreadyPast, result.Status);
        Assert.Equal(5, (await row.FindProgressAsync(Character, CancellationToken.None))!.Level);
        Assert.Null(row.LastWrite);
    }

    private static ValueTask<ExperienceBreakdown> Worth(uint amount) => ValueTask.FromResult(new ExperienceBreakdown(Base: amount));

    private static CharacterExperienceDomainService Grant(ICharacterTable characters) =>
        new(characters, new FixedProgression(Thresholds));

    /// <summary>Only the level thresholds; these tests are about the grant's arithmetic.</summary>
    private sealed class FixedProgression(IReadOnlyList<uint> thresholds) : IExperienceRuleService
    {
        public DungeonExperienceProgressionPolicy Progression() => new(thresholds, 0, 1, 100);

        public uint WorthOfKill(in KillCircumstances kill) => throw new NotSupportedException();

        public bool IsMonster(byte declaredActorType) => throw new NotSupportedException();

        public ClearExperienceWorth WorthOfClear(in ClearCircumstances clear) =>
            throw new NotSupportedException();

        public IReadOnlyList<ExperienceModifier> ModifiersFor(
            ExperienceEvent occasion,
            IReadOnlyList<AccountPremiumEntryState> premiums,
            DateTimeOffset now,
            int channelId,
            int dungeonId) => throw new NotSupportedException();
    }
}
