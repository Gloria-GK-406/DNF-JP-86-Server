using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

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
    /// repository built the instance for. Character 60 is the one the session directory knows;
    /// anyone else has no published state and is told so.
    /// </remarks>
    [Fact]
    public async Task The_repository_hands_back_the_character_it_was_asked_for()
    {
        var characters = Build();

        Assert.Equal(
            ItemUsability.Allowed,
            await characters.Get(new CharacterId(60)).CanUseAsync(Potion, CancellationToken.None));
        Assert.Equal(
            ItemUsability.CharacterStateUnavailable,
            await characters.Get(new CharacterId(61)).CanUseAsync(Potion, CancellationToken.None));
    }

    [Fact]
    public void The_character_it_hands_back_says_which_character_it_is()
    {
        var characters = Build();

        Assert.Equal(new CharacterId(61), characters.Get(new CharacterId(61)).Id);
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
        var characters = Build();

        Assert.Equal(
            await characters.Get(new CharacterId(60)).CanUseAsync(Potion, CancellationToken.None),
            await characters.Get(new CharacterId(60)).CanUseAsync(Potion, CancellationToken.None));
    }

    /// <summary>
    /// The repository the composition root installs, built the way it installs it.
    /// </summary>
    /// <remarks>
    /// The factory is the point. Passing a ready-made instance would test the field it was stored
    /// in; passing the factory tests that `Get` runs it and runs it with the identity it was given.
    /// </remarks>
    private static ICharacterDomainService Build()
    {
        var clock = new AdvanceableTimeProvider(
            new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero));
        var rules = new FakeItemUseRules((Potion, ItemUseRules.Potion(cooldownMilliseconds: 0)));
        var judgement = new ItemUseJudgementService(rules);
        var cooldowns = new CharacterItemCooldownDomainService(clock);
        var sessions = new OnlyCharacterSixty();
        var runtime = new FixedRuntimeSessions(level: 50, job: 0);
        var presence = new SessionPresenceDomainService();

        return new CharacterDomainService(
            () => throw new NotSupportedException("No test here reaches the roster."),
            new CharacterCreationRuleService(
                new UnusedStats(),
                new UnusedInitialSkills(),
                new UnusedInitialEquipment()),
            characterId => new PlayingCharacter(
                characterId, judgement, cooldowns, sessions, runtime, presence, new NoCarriedGold(),
                new StoredCarryLimitOnly(), CharacterRow.At(50), new NoSkillPointLedger()));
    }

    /// <summary>A directory that has published a session for character 60 and for nobody else.</summary>
    private sealed class OnlyCharacterSixty : ISelectedSessionDomainService
    {
        private static readonly SelectedSessionIdentity Session = SelectedSessionIdentity.Create(
            new AccountId(6), new CharacterId(60), new NetworkConnectionId("game-products"), 1, 10011);

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => null;
        public bool ReleaseExact(SelectedSessionIdentity session) => false;
        public SelectedSessionIdentity? Find(AccountId accountId) => Session;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => Session;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => Session;

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
            characterId.Value == 60 ? Session : null;
    }

    private sealed class UnusedStats : ICharacterStatDefinitionSource
    {
        public ValueTask<CharacterBaseStats> GetInitialAsync(
            byte job, CancellationToken cancellationToken) =>
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
