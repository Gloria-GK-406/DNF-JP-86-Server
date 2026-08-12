using Dfo.Server.Abstractions.Characters;
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
            CloneTitleItemId: 0);
        IEquipmentEligibilityCharacterReader reader = new CharacterService(
            () => new StubCharacterRepository(summary));

        var facts = await reader.GetAsync(
            accountId,
            summary.CharacterId,
            CancellationToken.None);

        Assert.Equal(new EquipmentEligibilityCharacterFacts(summary.CharacterId, 86, 7, 3), facts);
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
    }
}
