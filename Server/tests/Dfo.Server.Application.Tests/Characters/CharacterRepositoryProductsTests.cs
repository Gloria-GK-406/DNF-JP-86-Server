using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;
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
/// The seam itself: asking the repository for a character gets that character.
/// </summary>
/// <remarks>
/// Everything else about the character is covered by asking the character. What is covered here is
/// the one step before that, and it was covered by nothing: a repository that handed back a
/// different character than the one asked for left the whole suite green, because every other test
/// builds the instance itself and the factory the composition root installs was never run.
/// </remarks>
public sealed class CharacterRepositoryProductsTests
{
    private const int Potion = 8474;

    /// <remarks>
    /// The item is declared for a level the seeded character has and no other restriction, so the
    /// only thing that can make this answer differ between two characters is which character the
    /// repository built the instance for. Character 60 is the one with a row of its own; anyone
    /// else has nothing to be judged against and is told so.
    /// </remarks>
    [Fact]
    public async Task The_repository_hands_back_the_character_it_was_asked_for()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var itemUse = ItemUse();

            Assert.Equal(
                ItemUsability.Allowed,
                await itemUse.CanUseAsync(new CharacterId(60), Potion, CancellationToken.None));
            Assert.Equal(
                ItemUsability.CharacterStateUnavailable,
                await itemUse.CanUseAsync(new CharacterId(61), Potion, CancellationToken.None));
        });
    }

    [Fact]
    public void The_character_it_hands_back_says_which_character_it_is()
    {
        TestGameplay.Run(() =>
        {
            var characters = Build();

            Assert.Equal(new CharacterId(61), characters.Get(new CharacterId(61)).Id);
        });
    }

    /// <summary>
    /// Two asks make two instances, and neither carries anything from the other.
    /// </summary>
    /// <remarks>
    /// A character holds no state, so nothing is lost by rebuilding it; this states that outright,
    /// because the day one of them starts holding something, the two answers here stop agreeing and
    /// this is where it shows.
    /// </remarks>
    [Fact]
    public async Task Two_asks_for_one_character_answer_the_same()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var itemUse = ItemUse();

            Assert.Equal(
                await itemUse.CanUseAsync(new CharacterId(60), Potion, CancellationToken.None),
                await itemUse.CanUseAsync(new CharacterId(60), Potion, CancellationToken.None));
        });
    }

    /// <summary>
    /// The repository the composition root installs, built the way it installs it.
    /// </summary>
    /// <remarks>
    /// Built per call, so these test that `Get` builds a character with the identity it was given.
    /// </remarks>
    private static (ICharacterDomainService Characters, ItemUseJudgementService Judgement, OnlineCharacterRuntimeDomainService Cooldowns,
        Dfo.Server.Abstractions.World.ITownAreaDomainService Presence) Parts()
    {
        var clock = new AdvanceableTimeProvider(
            new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero));
        var rules = new FakeItemUseRules((Potion, ItemUseRules.Potion(cooldownMilliseconds: 0)));
        var judgement = new ItemUseJudgementService(rules);
        var cooldowns = new OnlineCharacterRuntimeDomainService(clock, new OnlineCharacterLocationDomainService());

        // Both are being played, so that the row is the only thing left to tell them apart. A
        // character nobody is playing is refused before its row is ever read, and this case would
        // then pass without the repository having handed back anything in particular.
        var presence = PresentCharacters.StandingInTown(new CharacterId(60), new CharacterId(61));

        return (new CharacterDomainService(new NoCarriedGold(), new StoredCarryLimitOnly(), new OnlyCharacterSixtysRow(),
            new NoSkillPointLedger(), null!, null!, new Dfo.Server.Application.Tests.Support.TestExperienceRules(), null!, null!, null!), judgement, cooldowns, presence);
    }

    private static ICharacterDomainService Build() => Parts().Characters;

    /// <summary>Whether the characters the repository hands back may use an item, asked through the item-use service.</summary>
    private static ICharacterItemUseDomainService ItemUse()
    {
        var (characters, judgement, cooldowns, presence) = Parts();
        return new CharacterItemUseDomainService(characters, judgement, cooldowns, presence, NobodyThere.Create<Dfo.Server.Abstractions.World.IDungeonParticipationDomainService>());
    }

    /// <summary>A table holding a row for character 60 and for nobody else.</summary>
    /// <remarks>
    /// What tells the two characters apart. It used to be the session directory, because what a
    /// character is was read off the session published for it; both facts a use is judged against
    /// are read from the character's own row now, so the row is what has to differ.
    /// </remarks>
    private sealed class OnlyCharacterSixtysRow : ICharacterTable
    {
        /// <summary>High enough that the declared item is not refused for it.</summary>
        private const int Level = 50;

        private static bool Has(CharacterId characterId) => characterId.Value == 60;

        public ValueTask<CharacterIdentityRow?> FindIdentityAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                Has(characterId)
                    ? new CharacterIdentityRow(SlotIndex: 0, Name: [0x41], Job: 0, GrowType: 0)
                    : null);

        public ValueTask<CharacterProgressRow?> FindProgressAsync(
            CharacterId characterId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                Has(characterId) ? new CharacterProgressRow(Level, (Level - 1) * 100) : null);

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
            byte[] appearanceBlob,
            CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask<int> SetGrowTypeAsync(CharacterId characterId, int expectedGrowType, int growType,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<int> SetProgressAsync(
            CharacterId characterId, int level, long experience,
            CancellationToken cancellationToken) => throw Unneeded();

        public ValueTask<bool> MarkDeletedAsync(
            CharacterId characterId, CancellationToken cancellationToken) => throw Unneeded();

        private static NotSupportedException Unneeded() =>
            new("This double says which characters have a row, and nothing else.");
    }

    private sealed class UnusedStats : ICharacterStatRuleService
    {
        public ValueTask InitializeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public IReadOnlyList<CharacterCareer> SupportedCareers => [];
        public CharacterBaseStats GetStats(CharacterCareer career, int level) =>
            throw new NotSupportedException("No test here creates a character.");
    }

    private sealed class UnusedInitialSkills : IInitialSkillDefinitionSource
    {
        public ValueTask<IReadOnlyList<InitialSkillGrant>> GetAsync(
            byte job, CancellationToken cancellationToken) =>
            throw new NotSupportedException("No test here creates a character.");
    }

    private sealed class UnusedInitialEquipment : IInitialEquipmentDefinitionSource
    {
        public ValueTask<IReadOnlyList<InitialEquipmentGrant>> GetAsync(
            byte job, CancellationToken cancellationToken) =>
            throw new NotSupportedException("No test here creates a character.");
    }
}
