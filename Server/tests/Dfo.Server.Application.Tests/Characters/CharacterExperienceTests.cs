using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// The one place experience is added to a character.
/// </summary>
/// <remarks>
/// Every source of experience goes through this, which is the point of it: a second copy is how
/// this code already produced a wrong total, when a quest finished twice wrote the same figure both
/// times because its own copy added to an entry-time snapshot rather than to the store.
/// </remarks>
public sealed class CharacterExperienceTests
{
    private static readonly CharacterId Character = new(7);

    /// <summary>Thresholds close enough together that one award can cross more than one.</summary>
    private static readonly uint[] Thresholds = [100, 250, 450, 700];

    [Fact]
    public async Task AnAwardRaisesTheStoredTotal()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var characters = new AwardFixture(level: 1, experience: 10);
            var grant = characters;

            var award = await grant.AwardAsync( _ => Worth(30), CancellationToken.None);

            Assert.NotNull(award);
            Assert.Equal(30u, award!.Gained);
            Assert.Equal(40u, award!.Total);
            Assert.Equal(1, award!.Level);
            Assert.False(award!.LeveledUp);
            Assert.Equal((1, 40u), characters.LastWrite);
        });
    }

    /// <summary>Crossing a threshold raises the level and keeps the surplus.</summary>
    [Fact]
    public async Task AnAwardPastAThresholdLevelsTheCharacter()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var characters = new AwardFixture(level: 1, experience: 90);
            var grant = characters;

            var award = await grant.AwardAsync( _ => Worth(30), CancellationToken.None);

            Assert.NotNull(award);
            Assert.True(award!.LeveledUp);
            Assert.Equal(2, award!.Level);
            Assert.Equal(120u, award!.Total);
            Assert.Equal((2, 120u), characters.LastWrite);
        });
    }

    /// <remarks>
    /// One award can cross more than one threshold. The early bands are a few hundred apart and a
    /// kill in a weighted dungeon can pay more than that, so the level is walked rather than
    /// incremented once.
    /// </remarks>
    [Fact]
    public async Task AnAwardPastSeveralThresholdsCrossesThemAll()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var characters = new AwardFixture(level: 1, experience: 0);
            var grant = characters;

            var award = await grant.AwardAsync( _ => Worth(460), CancellationToken.None);

            Assert.NotNull(award);
            Assert.Equal(4, award!.Level);
            Assert.Equal(460u, award!.Total);
        });
    }

    /// <summary>The level is derived from stored experience before valuation.</summary>
    /// <remarks>
    /// The inversion that makes a stale copy impossible. The caller cannot pass a level in, so it
    /// cannot pass a wrong one.
    /// </remarks>
    [Fact]
    public async Task TheValuationIsAskedWithTheExperienceDerivedLevel()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var characters = new AwardFixture(level: 12, experience: 0);
            var grant = characters;
            byte asked = 0;

            await grant.AwardAsync(
                level => { asked = level; return Worth(1); },
                CancellationToken.None);

            Assert.Equal(1, asked);
        });
    }

    /// <remarks>
    /// Two awards in a row see the store move under them. This is the shape of the defect that
    /// motivated the shared grant: the second completion of a quest wrote the same total as the
    /// first, because both added to the same remembered figure.
    /// </remarks>
    [Fact]
    public async Task ASecondAwardAddsToWhatTheFirstLeftBehind()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var characters = new AwardFixture(level: 1, experience: 0);
            var grant = characters;

            var first = await grant.AwardAsync( _ => Worth(25), CancellationToken.None);
            var second = await grant.AwardAsync( _ => Worth(25), CancellationToken.None);

            Assert.Equal(25u, first!.Total);
            Assert.Equal(50u, second!.Total);
        });
    }

    /// <summary>Contributions are part of what is granted, and are reported with it.</summary>
    [Fact]
    public async Task ContributionsAreAddedAndCarriedBackNamed()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var characters = new AwardFixture(level: 1, experience: 0);
            var grant = characters;

            var award = await grant.AwardAsync(
                _ => ValueTask.FromResult(new ExperienceBreakdown(Base: 20, Other: 10)),
                CancellationToken.None);

            Assert.NotNull(award);
            Assert.Equal(20u, award!.Base);
            Assert.Equal(30u, award!.Gained);
            Assert.Equal(30u, award!.Total);
            Assert.Equal(10u, award!.Breakdown.Other);
        });
    }

    /// <summary>An occasion that pays nothing writes nothing.</summary>
    [Fact]
    public async Task AnAwardOfNothingLeavesTheStoreAlone()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var characters = new AwardFixture(level: 3, experience: 300);
            var grant = characters;

            var award = await grant.AwardAsync(
                _ => ValueTask.FromResult(new ExperienceBreakdown()), CancellationToken.None);

            Assert.Equal(0u, award!.Gained);
            Assert.Equal(300u, award!.Total);
            Assert.Equal(3, award!.Level);
            Assert.Null(characters.LastWrite);
        });
    }

    /// <summary>A refused basic write aborts the complete award transaction.</summary>
    [Fact]
    public async Task AWriteTheStoreRefusesAbortsTheAward()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var characters = new AwardFixture(level: 1, experience: 10) { AcceptWrites = false };
            var grant = characters;

            await Assert.ThrowsAsync<SqliteException>(async () => await grant.AwardAsync(_ => Worth(30), CancellationToken.None));
        });
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
        await TestGameplay.RunAsync(async () =>
        {
            using var characters = new AwardFixture(level: 1, experience: 0) { Exists = false };
            var award = await characters.AwardAsync(_ => Worth(30), CancellationToken.None);
            Assert.Null(award);
            Assert.Null(characters.LastWrite);
        });
    }

    /// <summary>The total saturates at the last threshold rather than running past the table.</summary>
    [Fact]
    public async Task ATotalCannotRunPastTheLastThreshold()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var characters = new AwardFixture(level: 4, experience: 690);
            var grant = characters;

            var award = await grant.AwardAsync( _ => Worth(1_000_000), CancellationToken.None);

            Assert.Equal(Thresholds[^1], award!.Total);
        });
    }

    [Fact]
    public async Task CrossingTheCeilingKeepsTheRewardAndCapsOnlyProgression()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var characters = new AwardFixture(level: 4, experience: 690);
            var grant = characters;
            var award = await grant.AwardAsync(
                _ => ValueTask.FromResult(new ExperienceBreakdown(Base: 1_000_000, Channel: 50_000)),
                CancellationToken.None);
            Assert.NotNull(award);
            Assert.Equal(1_050_000u, award.Gained);
            Assert.Equal(1_000_000u, award.Base);
            Assert.Equal(50_000u, award.Breakdown.Channel);
            Assert.Equal(700u, award.Total);
            Assert.Equal(5, award.Level);
        });
    }

    [Fact]
    public async Task ACharacterAtTheCeilingKeepsTheFullRewardBreakdown()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var characters = new AwardFixture(level: 5, experience: Thresholds[^1]);
            var grant = characters;
            var award = await grant.AwardAsync(
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
        });
    }

    /// <summary>The award explains itself in one line, which is what a live log has to carry.</summary>
    [Fact]
    public async Task AnAwardCanSayWhereItCameFrom()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var characters = new AwardFixture(level: 1, experience: 0);
            var grant = characters;

            var award = await grant.AwardAsync(
                _ => ValueTask.FromResult(new ExperienceBreakdown(Base: 72, Other: 3)),
                CancellationToken.None);

            Assert.NotNull(award);
            Assert.Contains("Base = 72", award!.ToString(), StringComparison.Ordinal);
            Assert.Contains("Other = 3", award!.ToString(), StringComparison.Ordinal);
            Assert.Contains("75", award!.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task TutorialRequestWithStaleLevelNeverLowersCurrentProgress()
    {
        var row = new RecordingCharacterTable(level: 5, experience: 700);
        var handler = new Dfo.Server.Protocol.Game.TutorialLevelUp.GrantTutorialLevelCommandHandler(
            new Dfo.Server.Application.Characters.CharacterDomainService(null!, null!, id =>
                new Dfo.Server.Application.Characters.PlayingCharacter(id, null!, null!, null!, null!, null!, row, null!, null!, null!, experienceRules: new FixedProgression(Thresholds)), null!));
        var result = await handler.HandleAsync(null!,
            new Dfo.Server.Protocol.Game.TutorialLevelUp.GrantTutorialLevelCommand(Character),
            CancellationToken.None);
        Assert.Equal(Dfo.Server.Protocol.Game.TutorialLevelUp.TutorialLevelUpStatus.AlreadyPast, result.Status);
        Assert.Equal(5, (await row.FindProgressAsync(Character, CancellationToken.None))!.Level);
        Assert.Null(row.LastWrite);
    }

    [Fact]
    public void ExtraPvfThresholdsMustNotRaiseThePlayableLevelCeiling()
    {
        IExperienceRuleService rules = new FixedProgression(Enumerable.Range(1, 99).Select(x => (uint)x * 100).ToArray());
        var decision = rules.DecideAward(new(86, 8500), new(Base: 14));
        Assert.Equal(86, decision.Award.Level);
        Assert.Equal(8500u, decision.Award.Total);
        Assert.Equal(14u, decision.Award.Gained);
    }

    [Fact]
    public void ExperienceDeterminesLevelEvenWhenTheOldLevelColumnDisagrees()
    {
        IExperienceRuleService rules = new FixedProgression(Thresholds);
        var decision = rules.DecideAward(new(5, 10), new(Base: 20));
        Assert.Equal(1, decision.Award.Level);
        Assert.Equal(30u, decision.Award.Total);
    }

    [Fact]
    public async Task AtCapEachNotificationUsesOnlyThatAwardAndCapsuleReceivesEachOnce()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new AwardFixture(5, 700);
            var first = await fixture.AwardAsync(_ => Worth(14), default);
            var second = await fixture.AwardAsync(_ => Worth(57), default);
            Assert.Equal(700u, first!.Total);
            Assert.Equal(714u, first.NotificationTotal);
            Assert.Equal(700u, second!.Total);
            Assert.Equal(757u, second.NotificationTotal);
            Assert.Equal(71, fixture.Capsule);
            Assert.Equal(71, fixture.Honour);
            Assert.Null(fixture.LastWrite);
        });
    }

    [Fact]
    public async Task CrossingCapFillsOnlyTheOverflowIntoCapsule()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new AwardFixture(4, 690);
            var award = await fixture.AwardAsync(_ => Worth(30), default);
            Assert.Equal(700u, award!.Total);
            Assert.Equal(720u, award.NotificationTotal);
            Assert.Equal(20u, award.Overflow);
            Assert.Equal(20, fixture.Capsule);
        });
    }

    [Fact]
    public async Task LegacyOverflowIsNormalizedWithoutBecomingANewReward()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new AwardFixture(86, 3347);
            var award = await fixture.AwardAsync(_ => Worth(14), default);
            Assert.Equal(700u, award!.Total);
            Assert.Equal(714u, award.NotificationTotal);
            Assert.Equal(14, fixture.Capsule);
            Assert.Equal((5, 700u), fixture.LastWrite);
        });
    }

    [Fact]
    public async Task AProgressWriteFailureRollsBackTheCapsuleToo()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new AwardFixture(4, 690) { AcceptWrites = false };
            await Assert.ThrowsAsync<SqliteException>(async () => await fixture.AwardAsync(_ => Worth(30), default));
            Assert.Equal(0, fixture.Capsule);
            Assert.Equal(0, fixture.Honour);
        });
    }

    [Fact]
    public async Task ATransactionDiscardedAfterRewardRollsBackBothOwners()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new AwardFixture(4, 690);
            await fixture.AwardWithoutCommitAsync(30);
            Assert.Equal(0, fixture.Capsule);
            Assert.Equal(0, fixture.Honour);
            Assert.Null(fixture.LastWrite);
        });
    }

    [Fact]
    public async Task UnrepresentableNotificationRefusesBeforeAnyOwnerChanges()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new AwardFixture(5, 700);
            await Assert.ThrowsAsync<OverflowException>(async () => await fixture.AwardAsync(_ => Worth(uint.MaxValue), default));
            Assert.Equal(0, fixture.Capsule);
            Assert.Null(fixture.LastWrite);
        });
    }

    [Fact]
    public async Task CapsuleSaturatesWithoutReducingTheReturnedReward()
    {
        await TestGameplay.RunAsync(async () =>
        {
            using var fixture = new AwardFixture(5, 700);
            var award = await fixture.AwardAsync(_ => Worth(100000), default);
            Assert.Equal(100000u, award!.Gained);
            Assert.Equal(100700u, award.NotificationTotal);
            Assert.Equal(10000, fixture.Capsule);
            await fixture.AwardAsync(_ => Worth(14), default);
            Assert.Equal(10000, fixture.Capsule);
        });
    }

    private static ValueTask<ExperienceBreakdown> Worth(uint amount) => ValueTask.FromResult(new ExperienceBreakdown(Base: amount));

    private sealed class AwardFixture : IDisposable, IExperienceCalculationDomainService
    {
        private readonly ShopFixture shop;
        private Func<byte, ValueTask<ExperienceBreakdown>> price = null!;
        public AwardFixture(int level, uint experience)
        {
            shop = new ShopFixture(s =>
            {
                s.AddSingleton<IExperienceCalculationDomainService>(this);
                s.AddSingleton<IExperienceRuleService>(new FixedProgression(Thresholds));
            });
            shop.Sql($"UPDATE characters SET level={level},exp={experience} WHERE character_id=1; CREATE TABLE xp_writes(level INTEGER,exp INTEGER); CREATE TRIGGER xp_audit AFTER UPDATE OF exp ON characters BEGIN INSERT INTO xp_writes VALUES(NEW.level,NEW.exp); END");
        }
        public int Capsule => shop.Scalar("SELECT growth_capsule_exp FROM accounts WHERE account_id=1");
        public int Honour => shop.Scalar("SELECT honor_exp FROM accounts WHERE account_id=1");
        public async Task AwardWithoutCommitAsync(uint gain)
        {
            await using var tx = shop.Services.GetRequiredService<DomainTransactionScope>().Begin();
            await shop.Services.GetRequiredService<ICharacterDomainService>().Get(new(1)).AddExpAsync(new(Base: gain), null, default);
        }
        public bool AcceptWrites { init { if (!value) shop.Sql("CREATE TRIGGER refuse_xp BEFORE UPDATE OF exp ON characters BEGIN SELECT RAISE(ABORT,'XP refused'); END"); } }
        public bool Exists { init { if (!value) shop.Sql("DELETE FROM characters WHERE character_id=1"); } }
        public (int, uint)? LastWrite => shop.Scalar("SELECT COUNT(*) FROM xp_writes") == 0 ? null :
            (shop.Scalar("SELECT level FROM characters WHERE character_id=1"), checked((uint)shop.Scalar("SELECT exp FROM characters WHERE character_id=1")));
        public async ValueTask<ExperienceAward?> AwardAsync(Func<byte, ValueTask<ExperienceBreakdown>> valuation, CancellationToken ct)
        {
            price = valuation;
            await using var tx = shop.Services.GetRequiredService<DomainTransactionScope>().Begin();
            var award = await shop.Services.GetRequiredService<ICharacterDomainService>().Get(new(1)).AddExpAsync(
                new QuestExperienceContext(new(ExperienceEvent.QuestCompletion, new(1), new(1), 0), 0),
                shop.Clock.GetUtcNow(), null, ct);
            await tx.CommitAsync(ct);
            return award;
        }
        public ValueTask<ExperienceBreakdown> CalculateAsync(ExperienceCalculationContext source, byte level, CancellationToken ct) => price(level);
        public void Dispose() => shop.Dispose();
    }

    /// <summary>Only the level thresholds; these tests are about the grant's arithmetic.</summary>
    private sealed class FixedProgression(IReadOnlyList<uint> thresholds) : IExperienceRuleService
    {
        public DungeonExperienceProgressionPolicy Progression() => new(thresholds, 100000, 10000, 100);

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
