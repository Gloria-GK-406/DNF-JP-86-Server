using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// What a character has left to spend, asked of the character rather than remembered.
/// </summary>
/// <remarks>
/// There is no column for it. The total comes from the archive by level, the bonus from storage,
/// and the spend from walking the learned skills against the archive's cost table -- so the figure
/// is only ever as fresh as the moment it was worked out. It used to be worked out once, at entry,
/// and kept on the session: a character who levelled up went on being told the figure they entered
/// the world with, and only logging out and back in put it right.
/// <para>
/// So the case below is about the one thing a kept figure cannot do. It asks the same character
/// twice with a level in between, and requires two different answers -- each the one that level
/// works out to. A ledger that answered from anything but the level in the store answers the same
/// figure twice and fails here.
/// </para>
/// </remarks>
public sealed class RemainingSkillPointTests
{
    private static readonly CharacterId Who = new(60);

    /// <summary>What the stand-in archive grants for each level a character has reached.</summary>
    private const int SpPerLevel = 10;

    /// <summary>The bonus stored against the character, which no level changes.</summary>
    private const int BonusSp = 5;

    /// <summary>What each level of the one learned skill costs.</summary>
    private const int CostPerSkillLevel = 7;

    /// <summary>How far the character has taken that skill, on page zero only.</summary>
    private const byte LearnedTo = 3;

    /// <summary>The skill the character has learned.</summary>
    private const int LearnedSkill = 100;

    /// <summary>
    /// The same character at two levels has two different answers, each its level's own.
    /// </summary>
    /// <remarks>
    /// Both halves are needed. That the two differ is what a kept figure fails; that each equals
    /// what its level works out to is what a ledger which merely changed -- because it read a
    /// different level, or a different page -- would fail.
    /// </remarks>
    [Fact]
    public async Task The_points_change_with_the_level_and_each_is_that_levels_own()
    {
        var row = new MovableCharacterRow(level: 3);
        var (character, archive) = Ledger(row);

        var atThree = await character.RemainingSkillPointsAsync(CancellationToken.None);
        row.Level = 6;
        var atSix = await character.RemainingSkillPointsAsync(CancellationToken.None);

        Assert.NotEqual(atThree, atSix);
        Assert.Equal(Expected(3), atThree);
        Assert.Equal(Expected(6), atSix);

        // Priced at the level on the character's own row, not at the one the session carries, and
        // from the skills the ordinary pages hold.
        Assert.Equal([(byte)3, (byte)6], archive.LevelsAsked);
        Assert.Equal([LearnedSkill], archive.LastSkillsAsked);
    }

    /// <summary>
    /// The level the character answers with moves with the row too.
    /// </summary>
    /// <remarks>
    /// The companion fact, and the reason it is worth stating: the frames that carry the points
    /// carry a level beside them, and a character that answered a remembered level while working
    /// its points out afresh would report a pair that disagree with each other.
    /// </remarks>
    [Fact]
    public async Task The_standing_the_character_answers_with_moves_with_the_row()
    {
        var row = new MovableCharacterRow(level: 3);
        var (character, _) = Ledger(row);

        Assert.Equal(
            new CharacterProgressionState(3, MovableCharacterRow.Experience),
            await character.ProgressAsync(CancellationToken.None));

        row.Level = 6;

        Assert.Equal(
            new CharacterProgressionState(6, MovableCharacterRow.Experience),
            await character.ProgressAsync(CancellationToken.None));
    }

    /// <summary>
    /// A character this server does not have has no ledger, which is not a ledger of nothing.
    /// </summary>
    /// <remarks>
    /// Every caller of this acts differently on the two: a character with no points left still
    /// gets an experience frame, and one that is not there gets none at all. Answering zero would
    /// report a figure about somebody who is not there.
    /// </remarks>
    [Fact]
    public async Task A_character_that_is_not_there_has_no_ledger()
    {
        var (character, _) = Ledger(new MovableCharacterRow(level: 3) { Exists = false });

        Assert.Null(await character.RemainingSkillPointsAsync(CancellationToken.None));
        Assert.Null(await character.ProgressAsync(CancellationToken.None));
    }

    /// <summary>What the archive's grant, the stored bonus and the spend come to at that level.</summary>
    /// <remarks>
    /// Written out rather than quoted as a constant, so that the case says which arithmetic it
    /// expects. Page one holds nothing, so it keeps the whole entitlement; page zero has paid for
    /// three levels of one skill.
    /// </remarks>
    private static CharacterSkillProtocolMirrorState Expected(byte level)
    {
        var entitled = (SpPerLevel * level) + BonusSp;
        return new CharacterSkillProtocolMirrorState(
            checked((ushort)(entitled - (CostPerSkillLevel * LearnedTo))),
            checked((ushort)entitled),
            0,
            0);
    }

    /// <summary>The character, over the stand-ins, reached the way business reaches one.</summary>
    private static (ICharacter Character, LevelPricedArchive Archive) Ledger(
        MovableCharacterRow row)
    {
        var archive = new LevelPricedArchive();
        var points = new CharacterSkillPointDomainService(
            row,
            new OneLearnedSkill(),
            new StoredBonusPoints(),
            new CharacterEntryPreparationRuleService(
                archive,
                new StubExpertContractDefinitionSource(),
                new StubCreatureEquipmentLifetimeSource()));
        return (
            new PlayingCharacter(
                Who,
                new ItemUseJudgementService(new FakeItemUseRules()),
                new CharacterItemCooldownDomainService(TimeProvider.System),
                new SessionPresenceDomainService(),
                new NoCarriedGold(),
                new StoredCarryLimitOnly(),
                row,
                points),
            archive);
    }

    /// <summary>One character's row, whose level can be moved between questions.</summary>
    /// <remarks>
    /// Moved rather than re-read from a database, because what is under test is that the ledger
    /// asks again at all. That the store answers a level correctly is the persistence side's.
    /// </remarks>
    private sealed class MovableCharacterRow(int level) : ICharacterTable
    {
        /// <summary>What the character holds, which no case here moves.</summary>
        public const uint Experience = 1_234;

        /// <summary>Whether the character is there at all.</summary>
        public bool Exists { get; init; } = true;

        /// <summary>The level on the row, which a level-up moves.</summary>
        public int Level { get; set; } = level;

        public ValueTask<CharacterStandingRow?> FindStandingAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                this.Exists ? new CharacterStandingRow(Job: 2, GrowType: 0x11, this.Level) : null);

        public ValueTask<CharacterProgressRow?> FindProgressAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                this.Exists ? new CharacterProgressRow(this.Level, Experience) : null);

        public ValueTask<CharacterIdentityRow?> FindIdentityAsync(
            CharacterId characterId, CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask<IReadOnlyList<CharacterRosterRow>> ReadByAccountAsync(
            AccountId accountId, CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask<IReadOnlyList<CharacterSummaryRow>> ReadSummariesByAccountAsync(
            AccountId accountId, CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask<CharacterSummaryRow?> FindBySlotAsync(
            AccountId accountId, int slotIndex, CancellationToken cancellationToken) =>
            throw Unneeded();

        public ValueTask<bool> NameIsTakenAsync(
            byte[] name, CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask<bool> BelongsToAsync(
            CharacterId characterId, AccountId accountId, CancellationToken cancellationToken) =>
            throw Unneeded();

        public ValueTask<AccountId?> FindOwnerAsync(
            CharacterId characterId, CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask<CharacterId> InsertAsync(
            AccountId accountId,
            string name,
            byte[] nameBytes,
            int job,
            int slotIndex,
            byte[] appearanceBlob,
            CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask<int> SetProgressAsync(
            CharacterId characterId, int newLevel, long experience,
            CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask<bool> MarkDeletedAsync(
            CharacterId characterId, CancellationToken cancellationToken) => throw Unneeded();

        private static NotSupportedException Unneeded() =>
            new("This double answers what a character is and where it stands, and nothing else.");
    }

    /// <summary>
    /// The character's ordinary pages: one skill, on page zero, taken to <see cref="LearnedTo"/>.
    /// </summary>
    /// <remarks>
    /// The duel pages answer something else on purpose. They are rows of the same table told apart
    /// by a column, and a ledger that read them would price a tree the player never spends points
    /// in -- a mistake that produces a plausible number rather than a failure, so the two sets are
    /// made to disagree here and the case asserts which one arrived.
    /// </remarks>
    private sealed class OneLearnedSkill : ICharacterSkillTable
    {
        public ValueTask<IReadOnlyList<CharacterSkillEntryRow>> ReadEntryAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<CharacterSkillEntryRow>>(
                [new CharacterSkillEntryRow(0, 0, LearnedSkill, LearnedTo, [])]);

        public ValueTask<IReadOnlyList<CharacterSkillEntryRow>> ReadDuelSkillsAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<CharacterSkillEntryRow>>(
                [new CharacterSkillEntryRow(0, 0, LearnedSkill, 1, [])]);

        public ValueTask<IReadOnlyList<CharacterSkillLevelRow>> ReadLevelsAsync(
            CharacterId characterId, CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask InsertAsync(
            CharacterId characterId, int pageIndex, int slot, int skillId, int level,
            CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask<int> SetLevelAsync(
            CharacterId characterId, int pageIndex, int slot, int level,
            CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask<int> RemoveAsync(
            CharacterId characterId, int pageIndex, int slot,
            CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask InsertDuelSkillAsync(
            CharacterId characterId, int pageIndex, int slot, int skillId, int level,
            byte[] extraValues, CancellationToken cancellationToken) => throw Unneeded();

        private static NotSupportedException Unneeded() =>
            new("This double answers a character's learned skills and nothing else.");
    }

    /// <summary>The bonus points stored against the character.</summary>
    private sealed class StoredBonusPoints : ICharacterEntryTable
    {
        public ValueTask<BonusPointRow?> FindBonusPointsAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<BonusPointRow?>(new BonusPointRow(BonusSp, 0));

        public ValueTask<CharacterEntryRow?> FindEntryAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                "A ledger reads what a character is from the characters table, not from an entry.");
    }

    /// <summary>
    /// An archive that grants points by level, and remembers what it was asked about.
    /// </summary>
    /// <remarks>
    /// A flat grant would make the whole subject unobservable: the case is that the answer moves
    /// with the level, so the stand-in has to price by level. It records the levels it was asked
    /// for because there is a second level within reach -- the session's -- and pricing from that
    /// one would produce a figure that still looked reasonable.
    /// </remarks>
    private sealed class LevelPricedArchive : ISkillPointDefinitionSource
    {
        private readonly List<byte> _levels = [];

        public IReadOnlyList<byte> LevelsAsked => this._levels;

        public IReadOnlyList<int> LastSkillsAsked { get; private set; } = [];

        public ValueTask<SkillPointDefinitionSet> GetAsync(
            byte job,
            byte packedGrowType,
            byte characterLevel,
            IReadOnlyCollection<int> learnedSkillIds,
            CancellationToken cancellationToken)
        {
            // The job and the advancement come off the character's own row too, and an archive is
            // entitled to price differently by either -- so a ledger that guessed at them would be
            // asking about a character that does not exist.
            Assert.Equal(2, job);
            Assert.Equal(0x11, packedGrowType);

            this._levels.Add(characterLevel);
            this.LastSkillsAsked = [.. learnedSkillIds];
            return ValueTask.FromResult(new SkillPointDefinitionSet(
                SpPerLevel * characterLevel,
                0,
                [],
                new Dictionary<int, SkillPointCostDefinition>
                {
                    [LearnedSkill] = new(
                        LearnedSkill,
                        UsesTp: false,
                        Costs: [CostPerSkillLevel],
                        IsFixedLevel: false,
                        RequiredLevel: 1,
                        MaximumLevel: 20,
                        FixedLevelBase: 0,
                        FixedLevelInterval: 1,
                        FixedLevelAddPerInterval: 0),
                }));
        }
    }
}
