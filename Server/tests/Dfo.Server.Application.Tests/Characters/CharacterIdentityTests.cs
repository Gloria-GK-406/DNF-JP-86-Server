using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Characters;

/// <summary>
/// A character answering what it is: which slot it sits in, what it is called, what job it took and
/// how it grew.
/// </summary>
/// <remarks>
/// These four used to be answerable only off the session's copy of the character, which meant
/// anything holding a character's own identifier had to find a session first and then trust
/// whatever was last written into it. They are read from the character's own row now, and read at
/// the moment of the question -- so the cases below move the row between two questions and assert
/// that the second answer moved with it. An answer kept from the first question would pass every
/// other case in this file.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class CharacterIdentityTests
{
    private static readonly CharacterId Who = new(60);

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(17, 1, 17)]
    [InlineData(33, 1, 33)]
    [InlineData(32, 0, 0)]
    public async Task Avatar_candidates_adapt_to_current_career_and_automatically_fall_back(
        byte packed, byte career, byte awakening)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var row = new MovableIdentityRow { Job = 0, GrowType = packed };
            var character = Character(row);
            Assert.Equal(new CharacterCareer(0), await character.GetDefaultAvatarAsync(default));
            Assert.Equal(CharacterCareer.FromPacked(0, career), await character.GetCareerAvatarAsync(default));
            Assert.Equal(CharacterCareer.FromPacked(0, awakening), await character.GetAwakeningAvatarAsync(default));
            row.Job = 4; row.GrowType = 34;
            Assert.Equal(new CharacterCareer(4, 2, 2), await character.GetAwakeningAvatarAsync(default));
            Assert.Null(await Character(new MovableIdentityRow { Exists = false }).GetAvatarCandidatesAsync(default));
        });
    }

    [Fact]
    public async Task A_characters_identity_is_the_one_its_row_holds()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var identity = await Character(new MovableIdentityRow())
                .IdentityAsync(CancellationToken.None);

            Assert.NotNull(identity);
            Assert.Equal(Who, identity.CharacterId);
            Assert.Equal(3, identity.SlotIndex);
            Assert.Equal<byte[]>([0x41, 0x42], identity.Name);
            Assert.Equal(4, identity.Job);
            Assert.Equal(0x11, identity.GrowType);
        });
    }

    /// <summary>
    /// What the store holds now, not what it held when the question was first asked.
    /// </summary>
    /// <remarks>
    /// <b>The case a cache would fail.</b> Every other case here would pass against an answer
    /// worked out once and kept, because they ask once. This one asks, moves all four columns, and
    /// asks again; a character holding a copy would answer the first row twice.
    /// </remarks>
    [Fact]
    public async Task An_identity_the_store_moved_is_what_the_next_question_gets()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var row = new MovableIdentityRow();
            var character = Character(row);

            var before = await character.IdentityAsync(CancellationToken.None);

            row.SlotIndex = 5;
            row.Name = [0x43];
            row.Job = 2;
            row.GrowType = 0x22;
            var after = await character.IdentityAsync(CancellationToken.None);

            Assert.NotNull(before);
            Assert.NotNull(after);
            Assert.Equal(3, before.SlotIndex);
            Assert.Equal(5, after.SlotIndex);
            Assert.Equal<byte[]>([0x43], after.Name);
            Assert.Equal(2, after.Job);
            Assert.Equal(0x22, after.GrowType);
        });
    }

    /// <remarks>
    /// Nothing rather than a made-up slot zero with an empty name: a character in slot zero with no
    /// name is a standing a real character can have, so inventing it here would be
    /// indistinguishable from the truth about somebody else. The same reason
    /// <c>ProgressAsync</c> gives.
    /// </remarks>
    [Fact]
    public async Task A_character_with_no_row_of_its_own_has_no_identity()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var identity = await Character(new MovableIdentityRow { Exists = false })
                .IdentityAsync(CancellationToken.None);

            Assert.Null(identity);
        });
    }

    /// <summary>
    /// The job a use is judged against comes from the character, and comes from it each time.
    /// </summary>
    /// <remarks>
    /// The link the identity cases above would not catch. The judgement used to take the job off
    /// the session's copy of the character; it asks the character now, so a job that moves has to
    /// move the answer with it. Job 4 is the priest and job 0 is the swordman, and the item is
    /// declared for swordmen only.
    /// </remarks>
    [Fact]
    public async Task The_job_a_use_is_judged_against_is_the_one_the_row_holds_now()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var row = new MovableIdentityRow();
            var itemUse = ItemUse(
                row, (8474, ItemUseRules.RequiringJob(QuestJobRequirement.Swordman)));

            Assert.Equal(
                ItemUsability.JobMismatch,
                await itemUse.CanUseAsync(Who, 8474, CancellationToken.None));

            row.Job = 0;

            Assert.Equal(
                ItemUsability.Allowed,
                await itemUse.CanUseAsync(Who, 8474, CancellationToken.None));
        });
    }

    private static ICharacter Character(MovableIdentityRow row) =>
        new PlayingCharacter(Who, new NoCarriedGold(), new StoredCarryLimitOnly(), row,
            new NoSkillPointLedger(), null!, null!, new Dfo.Server.Application.Tests.Support.TestExperienceRules(), null!, null!, null!);

    private static ICharacterItemUseDomainService ItemUse(
        MovableIdentityRow row,
        params (int ItemId, ItemUseRule Rule)[] rules) =>
        new CharacterItemUseDomainService(new CharacterDomainStub(_ => Character(row)),
            new ItemUseRuleService(new FakeItemUseRules(rules)),
            new OnlineCharacterRuntimeDomainService(TimeProvider.System, new OnlineCharacterLocationDomainService()),
            PresentCharacters.StandingInTown(Who), NobodyThere.Create<Dfo.Server.Abstractions.World.IDungeonParticipationDomainService>());

    /// <summary>One character's row, whose identity can be moved between questions.</summary>
    /// <remarks>
    /// Moved rather than re-read from a database, because what is under test is that the character
    /// asks again at all. That a store answers these four columns correctly is the persistence
    /// side's subject and is covered there.
    /// <para>
    /// Everything else the table can be asked refuses outright, so that a change which started
    /// reading something nobody decided fails here rather than passing against a plausible answer.
    /// </para>
    /// </remarks>
    private sealed class MovableIdentityRow : ICharacterTable
    {
        /// <summary>Whether the character is there at all.</summary>
        public bool Exists { get; init; } = true;

        public int SlotIndex { get; set; } = 3;

        public byte[] Name { get; set; } = [0x41, 0x42];

        public int Job { get; set; } = 4;

        public int GrowType { get; set; } = 0x11;

        /// <summary>High enough that no case here is refused for it.</summary>
        public int Level { get; set; } = 50;

        public ValueTask<CharacterIdentityRow?> FindIdentityAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                this.Exists
                    ? new CharacterIdentityRow(
                        this.SlotIndex, this.Name, this.Job, this.GrowType)
                    : null);

        public ValueTask<CharacterProgressRow?> FindProgressAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                this.Exists ? new CharacterProgressRow(this.Level, 0) : null);

        public ValueTask<CharacterStandingRow?> FindStandingAsync(
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
            CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask<int> SetGrowTypeAsync(CharacterId characterId, int expectedGrowType, int growType,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<int> SetProgressAsync(
            CharacterId characterId, int level, long experience,
            CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask<bool> MarkDeletedAsync(
            CharacterId characterId, CancellationToken cancellationToken) => throw Unneeded();

        private static NotSupportedException Unneeded() =>
            new("This double answers what a character is, and where it stands, and nothing else.");
    }
}
