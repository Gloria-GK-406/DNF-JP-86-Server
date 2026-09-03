using Dfo.Server.Application.World;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Quests;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Quests;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Microsoft.Data.Sqlite;

namespace Dfo.Server.Protocol.Tests.Game.QuestCompletion;

/// <summary>
/// What handing a quest in still does, now that it grants through the shared implementation.
/// </summary>
/// <remarks>
/// This path worked and was verified against a live client before the kill-experience work moved
/// its granting out. Nothing covered it then: the only automated checks on quest completion were
/// over the frame's bytes, which a refactor of the granting could neither break nor be caught by.
/// <para>
/// What these hold is the part a person cannot see. What a quest pays is invisible until someone
/// replays that quest, so a refactor that quietly changed the amount -- or that reported a
/// completion whose award never landed -- would survive a green build, a green suite, and a
/// session of play.
/// </para>
/// The log and its store are the production ones over a real database, for the reason the
/// acceptance tests use them: a fake log would answer the question being asked, which is whether
/// the quest really left the carried list and what the character really ended up holding.
/// </remarks>
public sealed class FinishQuestCommandHandlerTests
{
    private static readonly AccountId Account = new(6);

    private static readonly CharacterId Character = new(60);

    private const ushort QuestId = 1016;

    private static CommandExecutionContext Transactional => new(
        Guid.Empty, typeof(FinishQuestCommand), typeof(FinishQuestCommandHandler),
        CommandExecutionKind.Transactional);

    /// <summary>The quest's own amount is what the character gains and what the outcome reports.</summary>
    [Fact]
    public async Task AFinishedQuestPaysItsOwnAmountAndSaysWhereItLeftTheCharacter()
    {
        await using var fixture = await Fixture.CreateAsync(level: 1, experience: 1000);
        await fixture.CarryAsync(QuestId, triggerValue: 0);
        var handler = fixture.Handler(Quest(QuestId));

        await using var boundary = fixture.Scope.Begin();
        var outcome = await handler.HandleAsync(
            Transactional,
            new FinishQuestCommand(Account, Character, QuestId, AcquiredAtUnixTime: 0),
            CancellationToken.None);
        await boundary.CommitAsync(CancellationToken.None);

        Assert.True(outcome.Finished);
        Assert.Equal(Fixture.QuestAward, outcome.Experience);
        Assert.Equal(1000u + Fixture.QuestAward, outcome.NewExperience);
        Assert.Equal((1, 1000u + Fixture.QuestAward), fixture.Characters.LastWrite);
    }

    /// <remarks>
    /// The defect the shared grant exists to remove, in the path it came from: two completions of
    /// one quest both wrote the same total, because the figure each added to was the entry-time
    /// snapshot rather than the store. The second completion here starts from what the first left.
    /// </remarks>
    [Fact]
    public async Task ASecondCompletionAddsToWhatTheFirstLeftBehind()
    {
        await using var fixture = await Fixture.CreateAsync(level: 1, experience: 0);
        var handler = fixture.Handler(Quest(QuestId, repeatable: true));

        var first = await fixture.FinishAsync(handler, QuestId);
        var second = await fixture.FinishAsync(handler, QuestId);

        Assert.Equal(Fixture.QuestAward, first.NewExperience);
        Assert.Equal(Fixture.QuestAward * 2, second.NewExperience);
    }

    /// <summary>An award that could not happen is a refusal, not a completion that paid nothing.</summary>
    /// <remarks>
    /// This was a refusal before the granting was shared, and had to stay one. Reporting a
    /// completion instead would send the client a level and total taken from a placeholder -- and
    /// the placeholder for a character the store does not hold is level 1 with no experience, so a
    /// character would watch itself reset.
    /// </remarks>
    [Fact]
    public async Task ACompletionWhoseAwardCannotHappenIsRefused()
    {
        await using var fixture = await Fixture.CreateAsync(level: 4, experience: 500, exists: false);
        await fixture.CarryAsync(QuestId, triggerValue: 0);
        var handler = fixture.Handler(Quest(QuestId));

        await using var boundary = fixture.Scope.Begin();
        var outcome = await handler.HandleAsync(
            Transactional,
            new FinishQuestCommand(Account, Character, QuestId, AcquiredAtUnixTime: 0),
            CancellationToken.None);

        Assert.False(outcome.Finished);
        Assert.Equal(QuestFinishRefusal.Unusable, outcome.Refusal);
    }

    /// <summary>A quest whose objective is unmet is refused before anything is granted.</summary>
    [Fact]
    public async Task AQuestWhoseObjectiveIsUnmetPaysNothing()
    {
        await using var fixture = await Fixture.CreateAsync(level: 1, experience: 1000);
        await fixture.CarryAsync(QuestId, triggerValue: 1);
        var handler = fixture.Handler(Quest(QuestId));

        await using var boundary = fixture.Scope.Begin();
        var outcome = await handler.HandleAsync(
            Transactional,
            new FinishQuestCommand(Account, Character, QuestId, AcquiredAtUnixTime: 0),
            CancellationToken.None);

        Assert.False(outcome.Finished);
        Assert.Null(fixture.Characters.LastWrite);
    }

    /// <remarks>
    /// A quest pays at the character's level, so the level the grant reads is the one the amount is
    /// worked out from. Two characters at different levels finishing the same quest are paid
    /// differently, and the difference is what proves the level came from the store.
    /// </remarks>
    [Fact]
    public async Task AQuestIsPricedAtTheLevelTheStoreHolds()
    {
        await using var low = await Fixture.CreateAsync(level: 1, experience: 0);
        await using var high = await Fixture.CreateAsync(level: 40, experience: 0);
        var quest = Quest(QuestId, paysAtCharacterLevel: true);

        var atOne = await low.FinishAsync(low.Handler(quest), QuestId);
        var atForty = await high.FinishAsync(high.Handler(quest), QuestId);

        Assert.True(
            atForty.Experience > atOne.Experience,
            $"level 40 was paid {atForty.Experience} and level 1 was paid {atOne.Experience}");
    }

    private static QuestDefinition Quest(
        int questId, bool repeatable = false, bool paysAtCharacterLevel = false) =>
        new(questId, "", repeatable ? QuestGrade.Daily : QuestGrade.Epic, null,
            new LevelRange(1, 99), null, null, null, [], "meet npc", "M",
            null, null, null, null, null, null, null, [], [], [], [],
            PaysAtCharacterLevel: paysAtCharacterLevel);

    private sealed class Fixture : IAsyncDisposable
    {
        /// <summary>What quest 1016 pays a level-1 character, confirmed on the wire.</summary>
        /// <remarks>
        /// The reference server answered 101 for this quest on 2026-08-27, and the archive's own
        /// table reproduces it: level 1 is worth 100, difficulty M adds one per cent, and the level
        /// penalty is none. Asserted here so the refactor cannot quietly change what a quest pays.
        /// </remarks>
        public const uint QuestAward = 101;

        private Fixture(string path, RecordingCharacterTable characters)
        {
            this.Path = path;
            this.Characters = characters;
        }

        public string Path { get; }

        public RecordingCharacterTable Characters { get; }

        public DomainTransactionScope Scope { get; } = new();

        public static async Task<Fixture> CreateAsync(int level, uint experience, bool exists = true)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), $"dfo-questfinish-{Guid.NewGuid():N}.db");
            _ = new SqliteDatabase(path, new DomainTransactionScope());
            var fixture = new Fixture(
                path, new RecordingCharacterTable(level, experience) { Exists = exists });
            await using var connection = Open(path);
            await using var character = connection.CreateCommand();
            character.CommandText =
                "INSERT INTO characters(character_id,account_id,name) VALUES(60,6,'finish')";
            await character.ExecuteNonQueryAsync();
            return fixture;
        }

        public FinishQuestCommandHandler Handler(QuestDefinition definition) =>
            new(new QuestDefinitionDomainService(
                    new QuestContentRuleService(
                        new Quests(definition), new InventedQuestParameters())),
                new CharacterQuestLogDomainService(
                    new SqliteQuestDomainStore(this.Path), new AmbientStoreAccess(this.Scope)),
                new CharacterContainerDomainService(),
                new CharacterExperienceDomainService(this.Characters, new FixedProgression()));

        /// <summary>Puts the quest in the carried list with its objective already at that value.</summary>
        public async Task CarryAsync(int questId, int triggerValue)
        {
            await using var connection = Open(this.Path);
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO character_active_quests(character_id,slot,quest_id,trigger_value) "
                + $"VALUES(60,0,{questId},{triggerValue})";
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Carries the quest with its objective met, then hands it in.</summary>
        public async Task<QuestFinishOutcome> FinishAsync(
            FinishQuestCommandHandler handler, ushort questId)
        {
            await this.CarryAsync(questId, triggerValue: 0);
            await using var boundary = this.Scope.Begin();
            var outcome = await handler.HandleAsync(
                Transactional,
                new FinishQuestCommand(Account, Character, questId, AcquiredAtUnixTime: 0),
                CancellationToken.None);
            await boundary.CommitAsync(CancellationToken.None);
            return outcome;
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(this.Path);
            }
            catch (IOException)
            {
                // A held file is not a failed test. The temp directory outlives this.
            }

            return ValueTask.CompletedTask;
        }

        private static SqliteConnection Open(string path)
        {
            var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            return connection;
        }
    }

    /// <summary>Thresholds far enough apart that these awards never cross one.</summary>
    /// <summary>Only the level thresholds; nothing here asks what a kill or a clear is worth.</summary>
    private sealed class FixedProgression : IExperienceRuleService
    {
        public DungeonExperienceProgressionPolicy Progression() =>
            new([.. Enumerable.Range(1, 100).Select(level => (uint)(level * 100_000))], 0, 1, 100);

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

    private sealed class Quests(QuestDefinition definition) : IQuestDefinitionSource
    {
        public ValueTask<IReadOnlyList<QuestDefinition>> GetAllAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<QuestDefinition>>([definition]);

        public ValueTask<QuestDefinition?> GetAsync(int questId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(questId == definition.QuestId ? definition : null);

        public ValueTask<IReadOnlyList<TrainingQuestAvailability>> GetTrainingAvailabilityAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<TrainingQuestAvailability>>([]);
    }

    /// <summary>
    /// A quest table made up here, agreeing with the archive at level 1 and nowhere else.
    /// </summary>
    /// <remarks>
    /// Named for what it is. The archive's own table opens <c>100, 100, 126, 187, 293</c>; this one
    /// is <c>100, 200, 300, 400, 500</c>, and only its first entry is real. That is enough for what
    /// these tests ask -- quest 1016 is a level-1 quest, so 101 comes out right, and it is checked
    /// against the archive itself by <c>PvfQuestParameterSourceTests</c>.
    /// <para>
    /// A name claiming otherwise would be the trap the archive tests already warn about: two things
    /// that look like corroboration and are not. Anyone adding a case at another level here must
    /// read the real table, not this.
    /// </para>
    /// </remarks>
    private sealed class InventedQuestParameters : IQuestParameterSource
    {
        public QuestParameters Load() =>
            new(
                // Level 1 is the archive's 100; the rest are round numbers no dungeon declares.
                [.. Enumerable.Range(1, 200).Select(level => level * 100)],
                new Dictionary<char, int> { ['M'] = 10 },
                GreenPenaltyPercent: 80,
                GreyPenaltyPercent: 30,
                EpicGreenPenaltyPercent: 120,
                EpicGreyPenaltyPercent: 140);
    }
}
