using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Characters;

public sealed class EquipmentEligibilityCharacterFactsTests
{
    [Fact]
    public async Task Reader_ExposesOnlyEligibilityFactsForAnOwnedCharacter()
    {
        var accountId = new AccountId(81);
        var summary = new CharacterSummary(
            new CharacterId(91),
            SlotIndex: 4,
            Name: [0x41],
            Job: 7,
            GrowType: 3,
            Level: 86,
            CloneTitleItemId: 0,
            Appearances: []);
        // Reading eligibility facts must not reach for static content: only creation needs it.
        // The last is how a character instance would be built, and reading eligibility facts never
        // builds one.
        IEquipmentEligibilityCharacterReader reader = new CharacterDomainService(
            () => new StubCharacterRepository(summary),
            new UnusedStatSource(),
            new UnusedInitialSkillSource(),
            null!);

        var facts = await reader.GetAsync(
            accountId,
            summary.CharacterId,
            CancellationToken.None);

        Assert.Equal(new EquipmentEligibilityCharacterFacts(summary.CharacterId, 86, 7, 3), facts);
    }

    private sealed class UnusedStatSource : ICharacterStatDefinitionSource
    {
        public ValueTask<CharacterBaseStats> GetInitialAsync(
            byte job,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Eligibility facts do not need job stats.");
    }

    private sealed class UnusedInitialSkillSource : IInitialSkillDefinitionSource
    {
        public ValueTask<IReadOnlyList<InitialSkillGrant>> GetAsync(
            byte job,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Eligibility facts do not need starting skills.");
    }

    private sealed class StubCharacterRepository(CharacterSummary summary) : ICharacterRepository
    {
        public ValueTask<IReadOnlyList<CharacterSummary>> ListByAccountAsync(
            AccountId accountId,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<CharacterSummary>>([summary]);

        public ValueTask<ActiveCharacterState?> FindByAccountAndSlotAsync(
            AccountId accountId,
            ushort slotIndex,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<bool> NameExistsAsync(
            byte[] name,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<CharacterId> CreateAsync(
            NewCharacterSpec spec,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<bool> SoftDeleteAsync(
            CharacterId characterId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
